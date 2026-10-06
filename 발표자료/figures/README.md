# 석사논문 1차 발표용 그림·영상 묶음

이 폴더는 `agentic_audio_projection_mapping`의 실제 산출물 중 발표에 바로 쓸 수 있는 파일만 복사한 것이다. 원본 판정과 한계를 유지해야 하며, 아래의 `실패/HOLD` 자료를 성공 사례로 제시하면 안 된다.

연구 파이프라인의 입력·출력·단계별 책임과 E237 구현값의 구분은 [`../연구_파이프라인_정리_2026-10-06.md`](../연구_파이프라인_정리_2026-10-06.md)에 정리했다.

| 파일 | 용도 | 원본 근거와 판정 |
|---|---|---|
| `01_pipeline.svg` | 연구 핵심 파이프라인 도식 | 입력·사건 source 생성·3D/UV 등록·오디오 편성·지속형 합성·최종 UV 비디오 텍스처를 연구 본체로 표시하고, WebGL·Unity·프로젝터는 후단 검증·적용으로 분리 |
| `02_msr_regions_24.png` | 24시점 MSR 영역 | `outputs/pipeline_runs/therock_sb_resonance_prepare_v70/previews/msr_regions_surface_24.png` |
| `03_msr_roles_24.png` | 24시점 역할 제안 | `outputs/pipeline_runs/therock_sb_resonance_prepare_v70/previews/msr_roles_surface_24.png`; 자동 역할은 제안이며 의미 정답이 아님 |
| `04_e56_controlled_3d_comparison.png` | E56 통제 조건 비교 | `outputs/review_exports/experiment56/poster_figure_controlled_3d_comparison.png`; 개발 사례 |
| `05_e56_msrg_metrics.png` | E56 MSRG 지표 | `outputs/review_exports/experiment56/poster_figure_msrg_metrics.png`; 사람 평가 아님 |
| `06_e86_all_mesh_paired_deltas.png` | E86 16개 미사용 메시 평균 차이 | `outputs/review_exports/experiment86/confirmation/all_mesh_paired_deltas.png`; oracle expression capacity 결과 |
| `07_e86_case70_b4_target3.png` | E86 집합 연산 예시 | `outputs/review_exports/experiment86/confirmation/case_70_b4_target3.png` |
| `08_e222_dense_uv_boundary_contact.png` | 다각도 SAM2→UV confidence | `outputs/review_exports/experiment222/therock/uv_ear_field_r1/dense_uv_boundary_contact.png` |
| `09_e227_hard_vs_soft_contact.jpg` | hard/soft multiview fusion | `outputs/review_exports/experiment227/therock/front_carrier_soft_uv_r1/qa_hard_vs_soft_contact.jpg`; cross-face gradient 9.18% 감소 |
| `10_e198_failed_temporal_contact.png` | 실패 사례 | `outputs/review_exports/experiment198/ltx_pilot_145_640_r10/contact_sheet.png`; 급격한 상태 교체로 기각된 계열 |
| `11_e230_30s_eight_view_qa.jpg` | 30초 배타 owner 다각도 QA | `outputs/review_exports/experiment230/therock/native_exclusive_30s_r1/qa_eight_view_30s_contact.jpg`; 오디오 없음 |
| `12_e230_rear_ltx_qa.jpg` | 후면 LTX 진행 | `outputs/review_exports/experiment230/therock/rear_ltx_6s_r1_x2/qa_13_frame_contact.jpg` |
| `13_e231_audio_event_timeline.png` | 오디오 기반 사건 시작·중첩 | `outputs/review_exports/experiment231/therock/audio_orchestrated_30s_r1/audio_event_timeline.png` |
| `14_e231_audio_eight_view_qa.jpg` | E231 최종 다각도 QA | `outputs/review_exports/experiment231/therock/audio_orchestrated_30s_r1/qa_eight_view_audio_30s_contact.jpg` |
| `15_e201_rib_opening_fourview_6s.mp4` | 캥거루 갈비 개방 성공 사례 | `outputs/review_exports/experiment201/rib_event_views_r1/rib_fourview_6s.mp4`; 6초 진단 결과 |
| `16_e231_audio_aligned_uv_30s.mp4` | E231 정렬 조건 UV 영상 | `outputs/review_exports/experiment231/therock/audio_orchestrated_30s_r1/therock_audio_orchestrated_30s_uv_2048.mp4` |
| `17_e231_shifted7s_control_uv_30s.mp4` | +7초 이동 대조군 | `outputs/review_exports/experiment231/therock/audio_orchestrated_30s_r1/therock_audio_orchestrated_30s_uv_2048_shifted7s_control.mp4` |
| `18_e237_three_view_timeline_30s.mp4` | E237 30초 발표용 3시점+타임라인 영상 | 아래 E237 원본만 사용한 고정 카메라 오프라인 렌더; `PASS_PRESENTATION_RENDER_QA` |
| `19_e237_three_view_poster.png` | 귀·눈 동시 시작 직후 포스터 | E237 frame 383, `t=15.958초`; 1920×1080 |
| `20_kangaroo_msr_concept.png` | 캥거루 동일 시점의 원본·hard partition·soft MSR 비교 | `kangaroo_msr_intrinsic_v45`의 고정 `a045/elevation 0°` face-ID와 실제 필드만 사용; 1680×912 |
| `21_kangaroo_msrg_roles.png` | 캥거루 MSRG 자동 역할과 인접 관계 | `automatic_role_assignment_v57/kangaroo/manifest.json`의 자동 역할·무방향 `region_graph`만 사용; 1800×750 |
| `22_slide6_e222_face_boundary_closeup.png` | 슬라이드 6용 face 경계와 SAM2 관측 비교 | E222 실제 결과에서 차이가 잘 보이는 두 귀 경계만 픽셀 그대로 추출; 새 영역·생성·주석 없음; 256×156 |

영상 16·17은 UV atlas 영상이므로 그 자체가 일반 카메라 영상처럼 보이지 않는다. Unity의 원본 메시 UV에 영상 텍스처로 적용한 뒤 3D 또는 실제 projector 결과를 발표 영상으로 보여 주는 것이 올바른 사용법이다.

## 01번 파이프라인 그림의 해석

- 최종 연구 산출물은 고정 메시의 UV 좌표에 적용하는 오디오 포함 비디오 텍스처와 사건 시간표·provenance·QA 정보다.
- WebGL viewer는 다각도 관찰과 QA를 위한 도구이며, 최종 산출물의 형식을 정의하지 않는다.
- Unity와 DLT/projector 단계는 생성된 비디오 텍스처를 원본 메시와 실제 물체에 적용하고 검증하는 후단이다.
- 현재 LTX 사건 source의 길이는 오디오가 자동 산출한 값이 아니라 연구자가 모델 호환 프레임 수 `73/97/145` 중에서 지정한 값이다. 오디오는 사건 종류·시작·중첩·생략을 결정한다.

## 캥거루 MSR·MSRG 그림 재현 정보

두 그림은 `발표자료/scripts/render_kangaroo_msr_figures.py`로 만들었다. 연구 저장소의 기존 캥거루 메시·face-ID·MSR 필드·자동 역할 manifest만 읽으며, 새로운 영역 분할이나 역할 추론을 실행하지 않는다.

- 메시: `outputs/pipeline_runs/kangaroo_msr_intrinsic_v45/inputs/working/Kangaroo_v1_L3.obj`
  - SHA-256: `A121EA93D25500EB5D9233708D5AC4E1C4D2B17E288244D394F1017AF379274B`
- MSR 필드: `outputs/pipeline_runs/kangaroo_msr_intrinsic_v45/artifacts/msr/semantic_shadow/msrg_refined_fields.npz`
  - SHA-256: `C7F49EEDE290DF20EEB113412222A2195D6579C7717EE1D6832ECB026AE20EA0`
- 고정 시점 face-ID: `outputs/pipeline_runs/kangaroo_msr_intrinsic_v45/artifacts/neutral_views/view_a045_ez00/view_a045_ez00_face_id.png`
  - 768×768, yaw 45°, elevation 0°
  - SHA-256: `D11596E258F534335FEF6B1E7827A61758D96E4A091881269BE9106FBB9569A6`
- 고정 시점 neutral RGB: `outputs/pipeline_runs/kangaroo_msr_intrinsic_v45/artifacts/neutral_views/view_a045_ez00/view_a045_ez00_neutral_rgb.png`
  - SHA-256: `E12EE6F5E5204D0C37335FC9CC71424DCE7544E82D82ABA605EE5F63F103CC85`
- 자동 역할 manifest: `outputs/evaluation/automatic_role_assignment_v57/kangaroo/manifest.json`
  - SHA-256: `67DF2AC3C08992DF256ACB49FB8EEA46174FB2364E2FB8FF8FF5DF2F2F43B70F`

### 20번 MSR 그림

- 세 패널은 동일한 768×768 face-ID 렌더를 같은 crop·scale로 사용한다.
- `원본`: OBJ에 저장된 정점 RGB를 면 단위로 평균하고 기존 neutral RGB의 명암만 곱했다.
- `기존 부위 분할 (hard label)`: `region_labels`의 실제 13개 hard partition과 파이프라인의 기존 `REGION_COLORS`를 사용했다.
- `MSR (core · band · halo)`: 실제 7개 `visual_semantic_parent_membership`을 기존 팔레트와 혼합했다. `soft_rgb = membership @ palette`, `opacity = 0.12 + 0.88 × max(membership)^1.7`, `display_rgb = (1−opacity) × 248 + opacity × soft_rgb`다. 이 opacity는 발표용 명암 표현이며 영역 판정을 바꾸지 않는다.
- 중요한 한계: 이 run의 `resolved_config.json`은 provider가 `geometry_fallback`임을 명시한다. 따라서 **캥거루 PartField 기반 hard cut 근거는 없음**. 또한 v45에는 연속 membership과 entropy는 있지만 core/band/halo가 각각 확정된 별도 마스크는 없다. 그림의 진함·중간·옅음은 기존 연속 membership 강도를 보여 주는 표현이다.
- 승인 상태: `artifacts/msr/editor/msr_editor_state.json`의 모든 영역이 `unreviewed`이며 `msr_approved.json`은 없다. 따라서 **사람이 승인한 MSR 근거는 없음**. 세 번째 패널은 자동 정제된 최종 제안이고 승인 결과로 제시하면 안 된다.
- 결과 SHA-256: `FD2CC0B6AC878F8F5BFB7D8D238728B2A2134E10E9F15D1BEED261EBC30115CF`

### 21번 MSRG 그림

- 자동 역할은 저장된 v57 결과 그대로다: `head→Source`, `leg→Path`, `paired_appendage→Path`, `tail→Path`, `torso→Reservoir`, `small_detail→Modifier`, `foot→Sink`.
- 노드는 고정 시점에서 각 semantic parent가 보이는 픽셀의 중심에 가장 가까운 실제 소유 픽셀에 놓았다. 역할 색도 v57 스크립트의 기존 색을 사용했다.
- 실선은 manifest의 무방향 `region_graph` 인접 관계만 중복 없이 그린 것이다.
- **semantic-parent 단위의 방향성 전달 엣지 근거는 없음**. 따라서 화살표를 만들지 않았다. Barrier는 v57에서 독립 노드가 아니라 `path_side_physical_geodesic_interface` 방식의 관계적 역할이므로 별도 노드로 만들지 않았다.
- 결과 SHA-256: `C561236899B1D99FD90F2D7F0FCD02575D271CBC5248056BD0C47D5C0E8A6C7D`

### 22번 슬라이드 6 경계 비교 그림

- 원본: `outputs/review_exports/experiment222/therock/dense_ear_detection_r1/dense_oblique_sam_contact.png`
- 원본 크기: 1792×558
- 사용 관측: 위쪽 행의 right 80°와 right 100° 귀 경계 두 곳만 사용했다.
- 추출 범위: 각각 `128×156` 픽셀. 원본 좌표 `(356, 55)`와 `(868, 55)`에서 crop한 뒤 좌우로 붙였다.
- 처리: crop과 무간격 병치만 수행했다. 크기 변경, 생성형 편집, 색 보정, 선명화, 보간, 새 영역, 새 라벨은 추가하지 않았다.
- 원본 표기 의미: 녹색 선은 기존 face 단위 범위, 흰색 선은 시점별 SAM2 경계, 주황색은 기존 범위 밖에서 추가 관측된 픽셀이다.
- 용도: 슬라이드 6의 `표면 영역 경계 표현의 한계` 항목에서 기존 갈비 개방 이미지를 대체한다. 문구는 `face 단위 이진 영역은 실제 경계가 삼각형 내부를 지날 때 잘림이나 주변 누출이 발생한다`로 제한한다.
- 근거 범위: E222는 일부 삼각형에 귀와 머리가 함께 포함돼 face 전체 선택과 제외 모두 오류가 남는 사례다. 일반적인 지각 품질 향상이나 모든 메시의 경계 개선을 뜻하지 않는다.
- 결과 SHA-256: `D10A8B86BACABAAD8670EEA68C8D2159DDA869B59B58F8AC85FC0CCCB7F7A5CA`

## E237 3시점 발표 영상 재현 정보

연구 저장소 루트는 `C:\Users\minsu\agentic_audio_projection_mapping`이며, 다음 기존 아티팩트만 입력으로 사용했다. 새 텍스처·사건·프레임을 생성하지 않았다.

- 최종 E237 UV+AAC MP4: `outputs/review_exports/experiment237/therock/audio_parallel_30s_r1/therock_audio_generated_events_30s_uv_1024.mp4`
  - SHA-256: `072bb35faec28d02568fc0c5a9319e02164f3ddf56b219b8c59d5845f3d11482`
- E237 사건 시간표: `outputs/review_exports/experiment237/therock/audio_parallel_30s_r1/result.json`
- 원본 고정 메시 정점·면: `outputs/review_exports/experiment95/programs/therock/geometry.npz`
  - SHA-256: `279c68f319d8e988931e049e4e39003b728a07a4232b6cc42062ade82118da27`
- 메시 UV: `outputs/review_exports/experiment102/therock/surface_masks.npz`
  - SHA-256: `737c877e7667eb4c2a5069c2c980d92b8ef5d648b7996a7eee758820193bf227`
- E237 원 오디오: `outputs/review_exports/experiment93/audio/OpenEyeSignal_030.wav`; 최종 영상에는 E237 MP4의 AAC 스트림을 재인코딩 없이 복사했다.

렌더 스크립트는 `발표자료/scripts/render_e237_three_view.py`다. 720개 UV 프레임을 고정 메시로 직접 래스터라이즈하므로 viewer UI, 마우스 커서, 화면 녹화가 개입하지 않는다.

- 화면: 1920×1080, 24fps, 정확히 720프레임/30.000초
- 카메라: 정면 yaw 0°, 측면 yaw 90°, 후면 yaw 180°, elevation +10°, 세 뷰 동일 직교 스케일·unlit 텍스처 조명·검은 배경
- 측면 선택: E237 frame 381→454 화면 변화량을 yaw 90°와 270°에서 비교해 귀 사건이 더 읽히는 yaw 90°를 선택했다(평균 절대 RGB 변화 0.883 대 0.680).
- 타임라인: E237 `result.json`의 전면·코·눈·귀·입·후면 사건 시작–종료를 그대로 사용하고, frame 287(`11.958초`)을 `사건 없음`으로 표시했다.
- 포스터: frame 383, `15.958초`(귀·눈 동시 시작 frame 382 직후).
- 인코딩: H.264 High@4.1, yuv420p, CRF 18, maxrate 8 Mbit/s, BT.709, faststart; AAC 48kHz stereo stream copy.
- 타임라인 제목: `오디오 사건 타임라인`(기존 `E237 · 오디오 사건 타임라인`에서 발표용 문구만 수정, 사건·렌더 설정은 동일).
- 결과 검증: 1920×1080, 24/1fps, 720프레임, 영상·오디오 모두 30.000초, 2,609,366 bytes. 입력/출력 AAC elementary-stream SHA-256가 `38695c2082f4b6df3c837590a4b4ff089027a7da823bd6a134c234a2f445e1e8`로 동일하다.
- 결과 SHA-256: 영상 `66960E7E654F6DB39BBB460674FFBBB861A25282F1535FAA9FBF420A44451708`, 포스터 `167D1CBCB0ADD337AC8EF22A82A37584FCF0765FFE559EE82F9B43ACDDF9DD89`.
