# 석사논문 1차 발표용 그림·영상 묶음

이 폴더는 `agentic_audio_projection_mapping`의 실제 산출물 중 발표에 바로 쓸 수 있는 파일만 복사한 것이다. 원본 판정과 한계를 유지해야 하며, 아래의 `실패/HOLD` 자료를 성공 사례로 제시하면 안 된다.

| 파일 | 용도 | 원본 근거와 판정 |
|---|---|---|
| `01_pipeline.svg` | 전체 파이프라인 도식 | 실제 구현 단계와 완료/미완료 범위를 근거 문서에 맞춰 재도식화 |
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

영상 16·17은 UV atlas 영상이므로 그 자체가 일반 카메라 영상처럼 보이지 않는다. Unity의 원본 메시 UV에 영상 텍스처로 적용한 뒤 3D 또는 실제 projector 결과를 발표 영상으로 보여 주는 것이 올바른 사용법이다.

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
- 결과 검증: 1920×1080, 24/1fps, 720프레임, 영상·오디오 모두 30.000초, 2,614,532 bytes. 입력/출력 AAC elementary-stream SHA-256가 `38695c2082f4b6df3c837590a4b4ff089027a7da823bd6a134c234a2f445e1e8`로 동일하다.
