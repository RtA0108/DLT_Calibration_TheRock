import sys
import os
import numpy as np
import torch
import torch.nn as nn
from scipy.io import loadmat
import trimesh
import pyrender
from scipy.sparse import spmatrix
from scipy.spatial import cKDTree
import cv2

# ==================================================================
# 1. 기존 함수 및 클래스 정의 (그대로 유지)
# ==================================================================

def resize_and_crop(im):
    gray = cv2.cvtColor(im, cv2.COLOR_RGB2GRAY)
    coords = cv2.findNonZero(255 - gray)
    if coords is None:
        return np.full((224, 224, 3), 255, dtype=np.uint8)
    x, y, w, h = cv2.boundingRect(coords)
    crop = im[y:y+h, x:x+w]
    output_size = 224
    min_margin_ratio = 0.1
    max_len = output_size * (1 - min_margin_ratio)
    scale = max_len / max(h, w)
    resized_patch = cv2.resize(crop, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
    rh, rw, _ = resized_patch.shape
    canvas = np.full((output_size, output_size, 3), 255, dtype=np.uint8)
    start_y = (output_size - rh) // 2
    start_x = (output_size - rw) // 2
    canvas[start_y:start_y+rh, start_x:start_x+rw] = resized_patch
    return canvas

def load_images_from_folder(path_to_shape, num_images=24):
    print(f"[CfS] 렌더링 시작: {os.path.basename(path_to_shape)}")
    try:
        vertices, faces = load_obj_manually(path_to_shape)
        if vertices is None: return None
        
        xn1, yn1, zn1 = vertices.max(axis=0)
        xn2, yn2, zn2 = vertices.min(axis=0)
        vertices[:, 0] -= 0.5 * (xn1 + xn2)
        vertices[:, 1] -= 0.5 * (yn1 + yn2)
        vertices[:, 2] -= 0.5 * (zn1 + zn2)
        
        bbox_diagonal = np.sqrt((xn1-xn2)**2 + (yn1-yn2)**2 + (zn1-zn2)**2)
        cam_dist = bbox_diagonal * 1.5

        mesh = trimesh.Trimesh(vertices=vertices, faces=faces)
        scene = pyrender.Scene(bg_color=[1.0, 1.0, 1.0], ambient_light=[0.3, 0.3, 0.3])
        pyrender_mesh = pyrender.Mesh.from_trimesh(mesh, smooth=False)
        scene.add(pyrender_mesh)

        cam = pyrender.PerspectiveCamera(yfov=np.pi / 4.0, aspectRatio=1.0, znear=0.01, zfar=10000)
        azimuths = np.tile(np.arange(0, 360, 30), 2)
        elevations = np.array([30]*12 + [-30]*12)
        
        # 주의: 윈도우 환경에서 오프스크린 렌더링 시 환경 변수 설정이 필요할 수 있음
        r = pyrender.OffscreenRenderer(224, 224)
        camera_forward = np.array([0, 0, -1])
        rendered_images = []

        for i, (az, el) in enumerate(zip(azimuths, elevations)):
            az_rad = np.deg2rad(-az)
            el_rad = np.deg2rad(-el) 
            cam_x = -cam_dist * np.cos(el_rad) * np.sin(az_rad)
            cam_y =  cam_dist * np.cos(el_rad) * np.cos(az_rad)
            cam_z =  cam_dist * np.sin(el_rad)
            camera_position = np.array([cam_x, cam_y, cam_z])
            
            vec_to_origin = -camera_position
            rotation_matrix = trimesh.geometry.align_vectors(camera_forward, vec_to_origin)
            cam_pose = rotation_matrix
            cam_pose[:3, 3] = camera_position

            scene_cam_node = scene.add(cam, pose=cam_pose)
            light = pyrender.DirectionalLight(color=np.ones(3), intensity=2.0)
            scene_light_node = scene.add(light, pose=cam_pose)

            color, _ = r.render(scene)
            processed_img = resize_and_crop(color)
            rendered_images.append(processed_img)
            
            scene.remove_node(scene_cam_node)
            scene.remove_node(scene_light_node)
            
        r.delete()
        return np.array(rendered_images, dtype=np.float32)
    except Exception as e:
        print(f"[CfS] 렌더링 오류: {e}")
        return None

def load_obj_manually(file_path):
    vertices, faces = [], []
    try:
        with open(file_path, 'r') as f:
            for line in f:
                if line.startswith('v '):
                    vertices.append([float(p) for p in line.strip().split()[1:4]])
                elif line.startswith('f '): 
                    faces.append([int(p.split('/')[0]) - 1 for p in line.strip().split()[1:]])
    except Exception as e:
        print(f"[CfS] OBJ 읽기 오류: {e}")
        return None, None
    return np.array(vertices, dtype=np.float32), np.array(faces, dtype=np.int32)

def autocrop(image):
    gray = cv2.cvtColor(image, cv2.COLOR_RGB2GRAY); inverted = 255 - gray
    coords = cv2.findNonZero(inverted)
    if coords is None: return 0, image.shape[0], 0, image.shape[1]
    x, y, w, h = cv2.boundingRect(coords)
    return y, y + h, x, x + w

class VS(nn.Module):
    def forward(self, x):
        x_squeezed = x.squeeze(); x_transposed = x_squeezed.T; dist_matrix = torch.cdist(x_transposed, x_transposed, p=2)
        saliency_scores = torch.sum(dist_matrix, dim=1); saliency_scores = saliency_scores / (x_squeezed.shape[0] * x_squeezed.shape[1])
        return saliency_scores.view(1, 1, -1, 1)

class SP(nn.Module):
    def forward(self, features, weights):
        features_squeezed = features.squeeze(); weights_squeezed = weights.squeeze()
        pooled_features = torch.matmul(features_squeezed, weights_squeezed)
        return pooled_features.view(1, 1, -1, 1)

class CfSCNN(nn.Module):
    def __init__(self):
        super(CfSCNN, self).__init__(); self.conv1=nn.Conv2d(3,96,kernel_size=7,stride=2); self.relu1=nn.ReLU(inplace=True); self.pool1=nn.MaxPool2d(kernel_size=3,stride=2); self.conv2=nn.Conv2d(96,256,kernel_size=5,stride=2,padding=1); self.relu2=nn.ReLU(inplace=True); self.pad_pool2=nn.ZeroPad2d((0,1,0,1)); self.pool2=nn.MaxPool2d(kernel_size=3,stride=2); self.conv3=nn.Conv2d(256,512,kernel_size=3,stride=1,padding=1); self.relu3=nn.ReLU(inplace=True); self.conv4=nn.Conv2d(512,512,kernel_size=3,stride=1,padding=1); self.relu4=nn.ReLU(inplace=True); self.conv5=nn.Conv2d(512,512,kernel_size=3,stride=1,padding=1); self.relu5=nn.ReLU(inplace=True); self.pool5=nn.MaxPool2d(kernel_size=3,stride=2); self.fc6=nn.Conv2d(512,4096,kernel_size=6,stride=1); self.relu6=nn.ReLU(inplace=True); self.fc7=nn.Conv2d(4096,4096,kernel_size=1,stride=1); self.relu7=nn.ReLU(inplace=True); self.viewsa=VS(); self.sapool=SP(); self.fc8=nn.Conv2d(4096,40,kernel_size=1,stride=1); self.prob=nn.Softmax(dim=1)
    def forward(self, x):
        x=self.pool1(self.relu1(self.conv1(x))); x=self.pool2(self.pad_pool2(self.relu2(self.conv2(x)))); x=self.relu3(self.conv3(x)); x=self.relu4(self.conv4(x)); x=self.relu5(self.conv5(x)); x=self.pool5(x); x=self.relu6(self.fc6(x)); features_fc7=self.relu7(self.fc7(x))
        features_mc=features_fc7.permute(2,3,1,0); vweights_mc_shape=self.viewsa(features_mc); pooled_features_mc_shape=self.sapool(features_mc, vweights_mc_shape)
        pooled_features=pooled_features_mc_shape.view(1,4096,1,1)
        scores_before_softmax=self.fc8(pooled_features); scores=self.prob(scores_before_softmax)
        return scores, vweights_mc_shape

def load_weights_from_mat(model, mat_path):
    print("[CfS] 가중치 로딩 중...")
    try:
        mat_contents=loadmat(mat_path,squeeze_me=True,struct_as_record=False)
        mat_params=mat_contents['params']; params_dict={p.name: p.value for p in mat_params}
        for name, layer in model.named_modules():
            if isinstance(layer, nn.Conv2d):
                weight_name, bias_name = f"{name}f", f"{name}b"
                if weight_name in params_dict:
                    mat_weight=params_dict[weight_name]
                    if isinstance(mat_weight, spmatrix): mat_weight=mat_weight.toarray()
                    if mat_weight.ndim == 4: py_weight=torch.from_numpy(mat_weight).permute(3,2,0,1)
                    elif mat_weight.ndim == 2: py_weight=torch.from_numpy(mat_weight.T).view(layer.out_channels,layer.in_channels,1,1)
                    else: continue
                    layer.weight.data.copy_(py_weight)
                if bias_name in params_dict:
                    mat_bias=params_dict[bias_name]; py_bias=torch.from_numpy(mat_bias).squeeze(); layer.bias.data.copy_(py_bias)
        print("[CfS] 가중치 이식 완료")
        return mat_contents['meta'].normalization.averageImage # 평균 이미지 반환
    except Exception as e:
        print(f"[CfS] 가중치 로드 실패: {e}")
        return None

def calculate_density(mesh):
    neighbors = mesh.vertex_neighbors; degrees = np.array([len(n) for n in neighbors]); total_dist = np.zeros(len(mesh.vertices))
    for i, n_indices in enumerate(neighbors):
        if len(n_indices) > 0:
            distances_sq = np.sum((mesh.vertices[i] - mesh.vertices[n_indices])**2, axis=1); total_dist[i] = np.sum(distances_sq)
    W = total_dist / (degrees + 1e-8); W = (W - W.min()) / (W.max() - W.min())
    return W

def get_view_matrix(az, el):
    el_rad, az_rad = np.deg2rad(el), np.deg2rad(az)
    t = np.array([0, 0, -2.5]); T = np.eye(4); T[:3, 3] = t
    Rz = np.array([[np.cos(az_rad), -np.sin(az_rad), 0, 0], [np.sin(az_rad), np.cos(az_rad), 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]])
    Rx = np.array([[1, 0, 0, 0], [0, np.cos(el_rad), -np.sin(el_rad), 0], [0, np.sin(el_rad), np.cos(el_rad), 0], [0, 0, 0, 1]])
    return T @ Rx @ Rz


# ==================================================================
# 2. Main 함수 수정 (Unity 연동용)
# ==================================================================

def main():
    # Unity에서 보낸 인자 받기
    if len(sys.argv) < 3:
        print("Error: 인자 부족. Usage: bridge_cfs.py <MeshPath> <OutputDir>")
        sys.exit(1)

    mesh_path = sys.argv[1]   # Unity가 준 파일 경로
    output_dir = sys.argv[2]  # 결과 저장할 폴더

    # 결과 파일 이름 설정
    base_name = os.path.splitext(os.path.basename(mesh_path))[0]
    output_file = os.path.join(output_dir, f"{base_name}_saliency.txt")

    print(f"[CfS] 시작: {base_name}")

    # 가중치 파일 경로 (★ 여기를 본인 경로에 맞게 꼭 확인하세요!)
    # 상대경로 'data/deploynet/...'는 Unity 실행 위치에 따라 못 찾을 수 있으므로 
    # 가급적 '절대 경로'를 추천하거나 파일 위치를 PythonScripts 폴더로 옮기세요.
    mat_model_path = os.path.join(os.path.dirname(__file__), 'net-deployed.mat') 
    
    if not os.path.exists(mat_model_path):
        # 만약 같은 폴더에 없으면 기존 경로 시도
        mat_model_path = os.path.join('data', 'deploynet', 'net-deployed.mat')
        if not os.path.exists(mat_model_path):
            print(f"Error: 가중치 파일을 찾을 수 없습니다: {mat_model_path}")
            sys.exit(1)

    # 1. 모델 준비
    model = CfSCNN()
    avg_img = load_weights_from_mat(model, mat_model_path)
    if avg_img is None: sys.exit(1)
    
    model.eval()
    avg_img_tensor = torch.from_numpy(avg_img).float().view(1, 3, 1, 1)

    # 2. 렌더링
    images_np = load_images_from_folder(mesh_path)
    if images_np is None:
        print("Error: 렌더링 실패")
        sys.exit(1)

    # 3. 추론 (Inference)
    images_tensor = torch.from_numpy(images_np).permute(0, 3, 1, 2)
    input_tensor = images_tensor - avg_img_tensor
    input_tensor.requires_grad = True

    scores, vweights = model(input_tensor)
    pred_class_idx = torch.argmax(scores.squeeze())
    scores.squeeze()[pred_class_idx].backward()

    grad_images = input_tensor.grad.detach().numpy()
    vweights_np = vweights.detach().numpy().squeeze()

    # 4. Saliency Back-projection
    print("[CfS] 메쉬 Saliency 매핑 중...")
    vertices, faces = load_obj_manually(mesh_path)
    mesh = trimesh.Trimesh(vertices=vertices, faces=faces)
    W = calculate_density(mesh)
    msa = np.zeros((len(mesh.vertices), 24))

    v_normalized = mesh.vertices.copy()
    xn1, yn1, zn1 = v_normalized.max(axis=0); xn2, yn2, zn2 = v_normalized.min(axis=0)
    v_normalized[:, 0] -= 0.5 * (xn1 + xn2); v_normalized[:, 1] -= 0.5 * (yn1 + yn2); v_normalized[:, 2] -= 0.5 * (zn1 + zn2)

    for i in range(24):
        img_view = images_np[i].astype(np.uint8)
        r1, r2, c1, c2 = autocrop(img_view)
        crop_h, crop_w = r2 - r1, c2 - c1
        if crop_h == 0 or crop_w == 0: continue

        saliency_2d = np.max(np.abs(grad_images[i].transpose(1, 2, 0)), axis=2)
        saliency_2d_norm = (saliency_2d - saliency_2d.min()) / (saliency_2d.max() - saliency_2d.min())
        cropsa = saliency_2d_norm[r1:r2, c1:c2]

        az, el = np.tile(np.arange(0, 360, 30), 2)[i], np.array([30]*12 + [-30]*12)[i]
        T = get_view_matrix(az, el)
        pts_h = np.hstack((v_normalized, np.ones((len(v_normalized), 1))))
        x2d = (T @ pts_h.T).T[:, :2]
        
        xxa, xxi = np.max(x2d[:,0]), np.min(x2d[:,0])
        yya, yyi = np.max(x2d[:,1]), np.min(x2d[:,1])
        longside = max(crop_h, crop_w); shortside = min(crop_h, crop_w)
        longx = max(xxa-xxi, yya-yyi) if max(xxa-xxi, yya-yyi) > 0 else 1
        shortx = min(xxa-xxi, yya-yyi) if min(xxa-xxi, yya-yyi) > 0 else 1
        
        sscale = 0.5 * (longside/longx + shortside/shortx)
        x1 = x2d[:,0] * sscale; y1 = x2d[:,1] * sscale
        x2 = x1 + crop_w/2; y2 = y1 - crop_h/2
        
        pixel_grid = np.array([[c, r] for r in range(crop_h) for c in range(crop_w)])
        if pixel_grid.shape[0] == 0: continue
        tree = cKDTree(pixel_grid)
        
        y2 = -y2; x2_min, y2_min = np.min(x2), np.min(y2)
        x2 = x2 - x2_min; y2 = y2 - y2_min
        query_points = np.vstack([x2, y2]).T
        
        dist, indices = tree.query(query_points, k=1)
        for v_idx in range(len(vertices)):
            if v_idx < len(indices):
                px, py = pixel_grid[indices[v_idx]]
                if 0 <= px < crop_w and 0 <= py < crop_h:
                    msa[v_idx, i] = np.exp(1 - W[v_idx]) / (np.exp(1 - cropsa[py, px]) + 1e-8)
        msa[:, i] *= vweights_np[i]

    sa = np.sum(msa, axis=1)
    if np.any(sa): sa = (sa - np.min(sa)) / (np.max(sa) - np.min(sa))

    # 5. 저장
    np.savetxt(output_file, sa, fmt='%.6f')
    print(f"[CfS] 저장 완료: {output_file}")

if __name__ == "__main__":
    main()