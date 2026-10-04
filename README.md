# DLT Calibration (TheRock 정리본)

카메라 없이 프로젝터를 보정하는 프로젝션 매핑 캘리브레이션 도구입니다.
프로젝터 화면의 마커를 실물의 같은 지점에 맞추면, 그 대응점으로 DLT(11개 파라미터)를 풀어 프로젝터의 위치·방향·렌즈를 계산합니다
(기반 논문: Portalés et al. 2020, *An interactive cameraless projector calibration method*).
어떤 지점을 맞출지는 saliency로 추천합니다.

[RtA0108/DLT_Calibration](https://github.com/RtA0108/DLT_Calibration)의 `testPrep` 브랜치에서 **실제로 쓰는 것만** 남긴 정리본입니다.
모델은 **TheRock** 하나만 들어 있습니다.

## 필요한 것
- Unity **2022.3.8f1** (URP)
- Windows x64 + **Visual Studio의 "C++를 사용한 데스크톱 개발"** 설치
  - DLT 계산 DLL(`DLT_Rezero.dll`)과 OpenCV DLL이 디버그 빌드라서, Visual Studio가 설치하는 디버그 런타임(`MSVCP140D.dll`, `ucrtbased.dll` 등)이 있어야 합니다.
- 실제 테스트: 프로젝터 1대 (Unity의 Display 2로 출력)

## 시작하기
1. **처음 한 번:** `DLTcalibration/Assets/Plugin/opencv_world480d.zip`의 압축을 같은 폴더에 풉니다 → `opencv_world480d.dll`(126MB).
   - GitHub는 100MB가 넘는 파일을 받지 않아서 압축본만 올려 두었습니다. 이 DLL이 없으면 보정 계산 때 `DllNotFoundException: DLT_Rezero.dll` 오류가 납니다.
   - 압축을 풀기 전에 Unity를 열었다면, 풀고 나서 Unity를 다시 시작하세요.
2. Unity Hub에서 `DLTcalibration` 폴더를 프로젝트로 엽니다. 처음 열 때는 패키지를 받고 가져오느라 몇 분 걸립니다.
3. `Assets/Scenes/SampleScene`을 열고 플레이합니다.
4. 오른쪽 목록에서 **TheRock**을 고르고 **R**(추천점으로 마커 만들기) → 프로젝터 화면에서 마커를 실물에 맞춥니다. 조작 화면에서 **H**를 누르면 도움말이 나옵니다.

자세한 순서는 [실제_프로젝터_테스트_방법.md](DLTcalibration/실제_프로젝터_테스트_방법.md),
다른 실물을 쓰려면 [새_모델_추가_방법.md](DLTcalibration/새_모델_추가_방법.md)를 보세요.

## 폴더 구성
| 경로 | 내용 |
|---|---|
| `DLTcalibration/` | Unity 프로젝트 |
| `DLTcalibration/Assets/Scenes/SampleScene.unity` | 실행 씬 |
| `DLTcalibration/Assets/Script/` | 캘리브레이션 코드 (`DLT_solve`, `VertexClickTest`, `Marker`, `MainController`, `CalibrationHUD` 등) |
| `DLTcalibration/Assets/Resources/Meshes/TheRock/` | TheRock 모델, 텍스처 |
| `DLTcalibration/Assets/Resources/Result_CfS`, `Result_Tex` | TheRock saliency 결과 (미리 계산되어 있어 Python 없이 바로 사용) |
| `DLTcalibration/Assets/Resources/DefaultLibrary.txt` | 모델 목록 |
| `DLTcalibration/Assets/Editor/ModelImportWindow.cs` | 새 모델 추가 도구 (Unity 메뉴 Tools > 캘리브레이션 > 새 모델 추가) |
| `DLTcalibration/Assets/Plugin/DLT_Rezero.dll` | DLT 계산 DLL (`opencv_world480d.dll`이 옆에 있어야 동작) |
| `DLTcalibration/Assets/Plugin/opencv_world480d.zip` | OpenCV DLL 압축본 (처음 한 번 풀기) |
| `DLTcalibration/PythonScripts/` | 새 모델의 saliency 계산 스크립트 |
| `DLT_Rezero/` | `DLT_Rezero.dll`의 C++ 소스 (Visual Studio 프로젝트) |
| `Docs/` | 원본 저장소에서 작업한 수정 내역 기록 |

## 원본에서 뺀 것
- TheRock 외 모델(Chick, Horse, Kangaroo 등)과 그 saliency 데이터, Kangaroo 4D 텍스처
- 씬에 남아 있던 예전 모델 오브젝트와 디버그용 오브젝트(화면 캡처, 엣지 마스크 디버거)
- 어디서도 쓰지 않는 스크립트(Trash 폴더, 예전 saliency 실험, OpenCV 테스트 등)와 그것만 쓰던 MathNet/NuGet
- 빌드 결과물(TestBulid.2.14, DLL 빌드 중간 파일), 실험 결과 이미지, TextMesh Pro 예제
- `net-deployed.mat` (CfS-CNN 학습 네트워크, 약 370MB): 새 모델의 CfS-CNN saliency를 계산할 때만 필요합니다. 따로 받아서 `DLTcalibration/PythonScripts/`에 넣으세요.

기능은 빼지 않았습니다. 4D 텍스처(P), 히트맵, 패치(T), 모델 추가 도구 등은 그대로 있어서, 모델만 추가하면 원본과 똑같이 쓸 수 있습니다.

## 개발 도구
`.mcp.json`과 `com.coplaydev.unity-mcp` 패키지는 Claude Code가 Unity를 직접 조작해 테스트할 때 쓰는 연결입니다.
필요 없으면 `DLTcalibration/Packages/manifest.json`에서 그 줄을 지워도 됩니다.
