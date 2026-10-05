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

영상 16·17은 UV atlas 영상이므로 그 자체가 일반 카메라 영상처럼 보이지 않는다. Unity의 원본 메시 UV에 영상 텍스처로 적용한 뒤 3D 또는 실제 projector 결과를 발표 영상으로 보여 주는 것이 올바른 사용법이다.
