import sys
import os
import random

def main():
    # 인자 받기 (Unity에서 보낸 것들)
    if len(sys.argv) < 4:
        print("Error: 인자가 부족합니다. (Usage: script.py mesh_path tex_path output_dir)")
        sys.exit(1)

    mesh_path = sys.argv[1]
    tex_path = sys.argv[2]
    output_dir = sys.argv[3]

    print(f"Python: '{os.path.basename(mesh_path)}' 처리 시작...")
    print(f"Python: 텍스처 경로 '{tex_path}' 확인됨.")

    # 1. OBJ 파일 열어서 정점(v) 개수 세기
    vertex_count = 0
    try:
        with open(mesh_path, 'r') as f:
            for line in f:
                if line.startswith('v '):
                    vertex_count += 1
    except Exception as e:
        print(f"Error: 파일 읽기 실패 - {e}")
        sys.exit(1)

    print(f"Python: 정점 {vertex_count}개 감지.")

    # 2. 결과 파일 경로 생성 (_saliency.txt)
    base_name = os.path.splitext(os.path.basename(mesh_path))[0]
    
    # Unity LoadProcess에서 기대하는 이름 형식:
    # 1. MeshSaliency (CfS-CNN용) -> Name_saliency.txt
    # 2. VertexSaliency (TexSaliency용) -> Name_vertex_saliency.txt
    
    out_path_1 = os.path.join(output_dir, f"{base_name}_saliency.txt")
    out_path_2 = os.path.join(output_dir, f"{base_name}_vertex_saliency.txt")

    # 3. 가짜 데이터 쓰기 (랜덤 값)
    # 실제로는 여기서 Deep Learning 모델이 돌아가야 함
    try:
        # 파일 1 생성
        with open(out_path_1, 'w') as f:
            for _ in range(vertex_count):
                f.write(f"{random.random()}\n")
        
        # 파일 2 생성
        with open(out_path_2, 'w') as f:
            for _ in range(vertex_count):
                f.write(f"{random.random()}\n")
                
        print(f"Python: 결과 파일 생성 완료 -> {out_path_1}")
    except Exception as e:
        print(f"Error: 쓰기 실패 - {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()