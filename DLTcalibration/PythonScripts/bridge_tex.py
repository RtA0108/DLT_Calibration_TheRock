import torch
import numpy as np
import yaml
import os
import sys
import argparse
import pymeshlab
from PIL import Image 
from pathlib import Path
import torch.nn.functional as F

# PyTorch3D (기하 정보 추출용)
from pytorch3d.io import load_objs_as_meshes

print("[Bridge] Python 스크립트 시작 (One-stop Pipeline)")

# ==============================================================================
# 0. 경로 설정 및 모델 임포트
# ==============================================================================
BASE_DIR = os.path.dirname(os.path.abspath(__file__)) 
MODEL_PATH = os.path.join(BASE_DIR, 'ckpt_root/MeshNet_best.pkl')
CONFIG_PATH = os.path.join(BASE_DIR, 'config/MeshSaliency.yaml')

try:
    from models import MeshTextureNet
except Exception as e:
    print(f"[Bridge Error] 모델 라이브러리 임포트 실패: {e}")
    sys.exit(1)

# ==============================================================================
# 1. 전처리 헬퍼 함수 (기존 make_npz.py 로직)
# ==============================================================================
def find_neighbor(faces, faces_contain_this_vertex, vf1, vf2, except_face):
    for i in (faces_contain_this_vertex[vf1] & faces_contain_this_vertex[vf2]):
        if i != except_face: return i
    return except_face

IMAGE_EXTS = ('.jpg', '.jpeg', '.png')

def _find_in_folder(folder, name):
    """folder 안에서 name과 대소문자만 다른 파일까지 찾는다 (Linux는 대소문자를 구분함)."""
    exact = folder / name
    if exact.is_file(): return exact
    for p in folder.iterdir():
        if p.is_file() and p.name.lower() == name.lower(): return p
    return None

def find_texture_path(obj_path):
    """OBJ에 맞는 텍스처 이미지 경로. 없으면 None.

    1) OBJ의 mtllib가 가리키는 MTL (없으면 <이름>.mtl, <이름>.obj.mtl)의 map_Kd
    2) 같은 폴더의 <이름>.jpg/.jpeg/.png (대소문자 무관)
    3) 같은 폴더에 이미지가 하나뿐이면 그 이미지

    예전에는 <이름>.mtl만 찾았고(이 프로젝트의 MTL은 <이름>.obj.mtl), 폴백도 '<이름>.*'에
    .obj/.meta 같은 파일이 걸리면 이미지가 없어도 거기서 멈춰서, 대부분의 모델이 빈 텍스처로 계산됐음.
    """
    obj_path = Path(obj_path)
    folder = obj_path.parent

    mtl_candidates = []
    try:
        with open(obj_path, 'r', errors='ignore') as f:
            for line in f:
                if line.startswith('mtllib'):
                    mtl_candidates.append(line[len('mtllib'):].strip())
    except OSError:
        pass
    mtl_candidates += [obj_path.stem + '.mtl', obj_path.name + '.mtl']

    for mtl_name in mtl_candidates:
        mtl_path = _find_in_folder(folder, mtl_name)
        if mtl_path is None: continue
        with open(mtl_path, 'r', errors='ignore') as f:
            for line in f:
                s = line.strip()
                if not s.startswith('map_Kd'): continue
                rest = s[len('map_Kd'):].strip()
                # 파일 이름에 공백이 있을 수 있어 줄 전체를 먼저, 옵션이 붙은 경우를 위해 마지막 토큰을 다음으로 시도
                for name in (rest, rest.split()[-1] if rest else ''):
                    tex = _find_in_folder(folder, name) if name else None
                    if tex is not None: return tex

    images = sorted(p for p in folder.iterdir() if p.is_file() and p.suffix.lower() in IMAGE_EXTS)
    for p in images:
        if p.stem.lower() == obj_path.stem.lower(): return p
    if len(images) == 1: return images[0]
    return None

def get_texture_from_mtl(obj_path, target_size=1024):
    texture_path = find_texture_path(obj_path)

    if texture_path is not None:
        try:
            img = Image.open(texture_path).convert('RGB')
            img_resized = img.resize((target_size, target_size), Image.Resampling.LANCZOS)
            img_np = np.array(img_resized, dtype=np.float32) / 255.0
            print(f"[Bridge] 텍스처 사용: {texture_path.name}")
            return np.transpose(img_np, (2, 0, 1)) # (3, H, W)
        except Exception as e:
            print(f"[Bridge Warning] 텍스처를 읽지 못했습니다 ({texture_path.name}): {e}")

    print("[Bridge Warning] 텍스처를 찾지 못해 빈(Zero) 텍스처로 대체합니다.")
    return np.zeros((3, target_size, target_size), dtype=np.float32)

def extract_topology(obj_path):
    """PyMeshLab을 이용해 이웃 정보(Ring)와 면(Face) 정보 추출"""
    print(f"[Process] 위상(Topology) 정보 추출 시작: {os.path.basename(obj_path)}")
    ms = pymeshlab.MeshSet()
    ms.load_new_mesh(obj_path)
    
    # 너무 크면 단순화 (OOM 방지)
    if ms.current_mesh().face_number() > 1000000:
        try: ms.apply_filter('meshing_decimation_quadric_edge_collapse', targetfacenum=1000000, preservenormal=True)
        except: pass

    mesh = ms.current_mesh()
    vertices = mesh.vertex_matrix()
    faces_idx = mesh.face_matrix()
    current_face_num = faces_idx.shape[0]

    # 가상 병합 (Logical Merge)
    _, inverse_indices = np.unique(vertices.round(decimals=6), axis=0, return_inverse=True)
    faces_logical = inverse_indices[faces_idx]

    num_unique = inverse_indices.max() + 1
    faces_contain = [set() for _ in range(num_unique)]
    for i in range(len(faces_idx)):
        [lv1, lv2, lv3] = faces_logical[i]
        faces_contain[lv1].add(i); faces_contain[lv2].add(i); faces_contain[lv3].add(i)

    # 이웃(Neighbors) 계산
    neighbors = []
    for i in range(len(faces_idx)):
        [lv1, lv2, lv3] = faces_logical[i]
        n1 = find_neighbor(faces_idx, faces_contain, lv1, lv2, i)
        n2 = find_neighbor(faces_idx, faces_contain, lv2, lv3, i)
        n3 = find_neighbor(faces_idx, faces_contain, lv3, lv1, i)
        neighbors.append([n1, n2, n3])
    neighbors = np.array(neighbors)

    # Ring 1~3 계산
    ring_1 = neighbors 
    ring_2 = []
    for i in range(len(neighbors)):
        r1 = ring_1[i]; current_r2 = []
        for n in r1:
            for neighbor in neighbors[n]:
                if neighbor != i: current_r2.append(neighbor)
        if len(current_r2) > 6: current_r2 = current_r2[:6]
        while len(current_r2) < 6: current_r2.append(current_r2[-1] if len(current_r2)>0 else i)
        ring_2.append(current_r2)
    ring_2 = np.array(ring_2)

    ring_3 = []
    for i in range(len(neighbors)):
        r1 = ring_1[i]; r2 = ring_2[i]; current_r3 = []
        for k, n_r2 in enumerate(r2):
            parent = r1[k // 2] 
            for neighbor in neighbors[n_r2]:
                if neighbor != parent: current_r3.append(neighbor)
        if len(current_r3) > 12: current_r3 = current_r3[:12]
        while len(current_r3) < 12: current_r3.append(current_r3[-1] if len(current_r3)>0 else i)
        ring_3.append(current_r3)
    ring_3 = np.array(ring_3)

    texture_data = get_texture_from_mtl(obj_path, 1024)
    uv_grid = np.zeros((current_face_num, 9, 9, 3), dtype=np.float32)

    return {
        'faces': faces_idx.astype(np.int64),
        'neighbors': neighbors.astype(np.int64),
        'ring_1': ring_1.astype(np.int64),
        'ring_2': ring_2.astype(np.int64),
        'ring_3': ring_3.astype(np.int64),
        'texture': texture_data,
        'uv_grid': uv_grid
    }

# ==============================================================================
# 2. 기하 특징 헬퍼 함수 (기존 MeshDataset.collect_data 로직)
# ==============================================================================
def extract_geometry(obj_path, device):
    """PyTorch3D를 이용해 기하학적 특징(verts, centers, normals, corners) 추출"""
    print("[Process] 기하(Geometry) 정보 추출 시작...")
    mesh = load_objs_as_meshes([obj_path], device=device, load_textures=False)
    
    verts = mesh.verts_packed()
    faces = mesh.faces_packed()
    
    # 정규화
    center = (torch.max(verts, 0)[0] + torch.min(verts, 0)[0]) / 2
    verts -= center
    max_len = torch.max(verts[:, 0] ** 2 + verts[:, 1] ** 2 + verts[:, 2] ** 2)
    verts /= torch.sqrt(max_len)

    # Feature 계산
    corners = verts[faces] # (N, 3, 3)
    centers = torch.mean(corners, dim=1) # (N, 3)
    
    v1 = corners[:, 0, :]; v2 = corners[:, 1, :]; v3 = corners[:, 2, :]
    e1 = v2 - v1; e2 = v3 - v1
    normals = torch.cross(e1, e2, dim=1)
    normals = F.normalize(normals, p=2, dim=1)

    corners_reshaped = corners.reshape(-1, 9)

    # 상대좌표 변환 (매우 중요: 모델 학습 방식)
    corners_relative = corners_reshaped - torch.cat([centers, centers, centers], 1)

    # 패딩 (Padding)
    max_ver = 1020500 
    if verts.shape[0] > max_ver: max_ver = verts.shape[0]
    verts_np = verts.cpu().numpy()
    verts_padded = np.pad(verts_np, ((0, max_ver - verts_np.shape[0]), (0, 0)), mode='constant')
    
    return {
        'verts': torch.from_numpy(verts_padded).float(),
        'centers': centers.float(),
        'normals': normals.float(),
        'corners': corners_relative.float(),
        'real_vertex_count': verts.shape[0]
    }

# ==============================================================================
# 3. 모델 추론 함수
# ==============================================================================
def get_saliency_for_single_mesh(model, device, collated_dict):
    verts = collated_dict['verts'].to(device).float()
    faces = collated_dict['faces'].to(device).long()
    
    # (Batch, N, C) -> (Batch, C, N) 으로 차원 뒤집기
    centers = collated_dict['centers'].to(device).float().permute(0, 2, 1)
    normals = collated_dict['normals'].to(device).float().permute(0, 2, 1)
    corners = collated_dict['corners'].to(device).float().permute(0, 2, 1)
    
    neighbor_index = collated_dict['neighbors'].to(device).long()
    ring_1 = collated_dict['ring_1'].to(device).long()
    ring_2 = collated_dict['ring_2'].to(device).long()
    ring_3 = collated_dict['ring_3'].to(device).long()
    
    texture = collated_dict['texture'].to(device).float()
    uv_grid = collated_dict['uv_grid'].to(device).float()

    saliency_scores = model(verts=verts, faces=faces, centers=centers, normals=normals,
                            corners=corners, neighbor_index=neighbor_index, ring_1=ring_1,
                            ring_2=ring_2, ring_3=ring_3, face_colors=0, face_textures=0,
                            texture=texture, uv_grid=uv_grid)
    return saliency_scores.detach().cpu().numpy().flatten()

def convert_face_to_vertex_saliency(faces, face_scores, num_vertices):
    vertex_scores_sum = np.zeros(num_vertices)
    vertex_face_counts = np.zeros(num_vertices)
    for face_index, vertex_indices in enumerate(faces):
        if face_index < len(face_scores):
            score = face_scores[face_index]
            for vertex_index in vertex_indices:
                if vertex_index < num_vertices:
                    vertex_scores_sum[vertex_index] += score
                    vertex_face_counts[vertex_index] += 1
    return vertex_scores_sum / (vertex_face_counts + 1e-8)

# ==============================================================================
# 4. 메인 파이프라인
# ==============================================================================
if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--mesh', type=str, required=True)
    parser.add_argument('--tex', type=str, required=False) # 사용 안하지만 호환성 위해 남김
    parser.add_argument('--out', type=str, required=True)
    args = parser.parse_args()

    target_mesh_name = os.path.splitext(os.path.basename(args.mesh))[0]
    device = torch.device("cuda:0" if torch.cuda.is_available() else "cpu")

    if not os.path.exists(args.mesh):
        print(f"[Bridge Error] Mesh 파일이 존재하지 않습니다: {args.mesh}")
        sys.exit(1)

    # 1. 모델 로드
    try:
        with open(CONFIG_PATH, 'r') as f:
            cfg = yaml.load(f, Loader=yaml.loader.SafeLoader)
        
        model = MeshTextureNet(cfg=cfg)
        state_dict = torch.load(MODEL_PATH, map_location=device)
        new_state_dict = {k[7:] if k.startswith('module.') else k: v for k, v in state_dict.items()}
        model.load_state_dict(new_state_dict)
        model.to(device)
        model.eval()
        print("[Bridge] 모델 로딩 완료.")
    except Exception as e:
        print(f"[Bridge Error] 모델 로딩 실패: {e}")
        sys.exit(1)

    # 2. 데이터 전처리 (On-the-fly)
    try:
        top_data = extract_topology(args.mesh)
        geo_data = extract_geometry(args.mesh, device)
        
        # DataLoader가 해주던 Batch 차원(unsqueeze(0)) 추가 작업 수동 처리
        collated_dict = {
            'faces': torch.from_numpy(top_data['faces']).unsqueeze(0),
            'verts': geo_data['verts'].unsqueeze(0),
            'centers': geo_data['centers'].unsqueeze(0),
            'normals': geo_data['normals'].unsqueeze(0),
            'corners': geo_data['corners'].unsqueeze(0),
            'neighbors': torch.from_numpy(top_data['neighbors']).unsqueeze(0),
            'ring_1': torch.from_numpy(top_data['ring_1']).unsqueeze(0),
            'ring_2': torch.from_numpy(top_data['ring_2']).unsqueeze(0),
            'ring_3': torch.from_numpy(top_data['ring_3']).unsqueeze(0),
            'texture': torch.from_numpy(top_data['texture']).unsqueeze(0),
            'uv_grid': torch.from_numpy(top_data['uv_grid']).unsqueeze(0),
        }
    except Exception as e:
        print(f"[Bridge Error] 전처리 중 실패: {e}")
        sys.exit(1)

    # 3. 추론 및 저장
    if not os.path.exists(args.out): os.makedirs(args.out)

    try:
        print("[Process] 모델 추론(Inference) 중...")
        with torch.no_grad():
            face_scores = get_saliency_for_single_mesh(model, device, collated_dict)
            
            # 면 -> 정점 변환
            faces_np = top_data['faces']
            padded_len = collated_dict['verts'].shape[1]
            vertex_scores = convert_face_to_vertex_saliency(faces_np, face_scores, padded_len)
            
            # 패딩 잘라내기
            real_count = geo_data['real_vertex_count']
            vertex_scores = vertex_scores[:real_count]
            
            # Min-Max 정규화
            v_min, v_max = vertex_scores.min(), vertex_scores.max()
            if v_max - v_min > 1e-8: vertex_scores = (vertex_scores - v_min) / (v_max - v_min)
            else: vertex_scores = np.zeros_like(vertex_scores)
            
            # 저장
            save_path = os.path.join(args.out, f"{target_mesh_name}_vertex_saliency.txt")
            np.savetxt(save_path, vertex_scores, fmt='%.6f')
            
            print(f"[Bridge] ✅ 최종 결과물 생성 성공: {save_path}")
            
    except Exception as e:
        print(f"[Bridge Error] 추론 또는 저장 중 실패: {e}")
        sys.exit(1)

    print("[Bridge] 모든 과정이 무사히 종료되었습니다.")