using UnityEngine;
using System;
using System.Collections.Generic;

// 대응점 선택을 슬롯 단위로 관리한다. 슬롯 i 하나에 아래 네 가지가 항상 함께 묶인다.
//   clickedObjects[i]  : 선택된 버텍스 구
//   verticesStruct[i]  : 3D 좌표 + 프로젝터 2D 좌표(screenCoordinate) + 클릭 당시 투영 위치(screenCoordinateGT)
//   markers[i]         : 프로젝터 화면의 마커 (드래그하면 verticesStruct[i].screenCoordinate가 갱신됨)
//   구 색상             : 빨강 = 선택됨
// 2D 좌표는 모두 projectCam(프로젝터)의 스크린 픽셀 좌표(좌하단 원점)다.
public class VertexClickTest : MonoBehaviour
{
    public const int MaxPoints = 20; // 대응점 슬롯 수 (= 추천점 개수 상한)
    private const float MinMarkerSpreadPixels = 20f; // 추천점이 이보다 좁게 몰려 있으면 경고

    [Header("Data")]
    public GameObject[] clickedObjects; // 선택된 Vertex 오브젝트들
    public int arrayIndex = 0; // 현재 선택된 개수

    public VertexStruct[] verticesStruct; // 데이터 저장용 구조체 배열

    [Header("References")]
    public Camera projectCam;            // 프로젝터 카메라 (2D 좌표의 기준)
    public MarkerManager markerManager;  // 마커를 띄울 CanvasUI의 MarkerManager

    [Header("Selection")]
    public float pickRadiusPixels = 25f; // 커서에서 이 거리(조작 화면 픽셀) 안의 보이는 버텍스를 고른다

    [Header("Patch Marker")]
    // 패치(마커 주변 모양)는 기본으로 끈다. 매끈한 모델에서는 그릴 게 없어 일관성이 없었음.
    // T 키로 끔 -> 선 -> 텍스처 순서로 바꿀 수 있다 (연구 비교용).
    public bool showPatches = false;
    public int patchSize = 64;                                    // 패치 한 변 (프로젝터 픽셀). 0이면 패치 없음
    public PatchSnapshot.Mode patchMode = PatchSnapshot.Mode.Lines;
    // 선 모드에서 이 각도(도)보다 크게 꺾인 모서리를 그린다.
    // 0이면 모델마다 정함: 라이브러리 항목의 patchCreaseAngle -> 없으면 모서리 각도 분포로 자동
    // (매끈한 high poly 35도, 각진 low poly는 중앙값 x 0.8. PatchSnapshot.AutoCreaseAngle 참고).
    // 0보다 크면 모든 모델에 이 값을 쓴다 (실험용).
    [Range(0f, 90f)] public float patchCreaseAngle = 0f;

    private GameObject creaseAngleLoggedFor;

    [Header("Live Calibration")]
    public bool liveSolve = true;          // L 키: 마커를 놓을 때마다 자동으로 DLT를 다시 풂 (점 6개 이상)
    public DLT_solve dltSolver;

    private Marker[] markers;
    // 사용자가 드래그해서 놓은 마커. 실시간 재계산은 이것만 쓴다.
    // (R로 10개를 고르면 바로 "선택 6개 이상"이 되어, 예전에는 아직 안 옮긴 마커들까지 계산에 섞였음.
    //  다 맞출 때까지 매번 불일치 경고가 뜨고 투영과 패치가 중간에 흔들렸다)
    private bool[] placed;
    // 마지막으로 누르거나 끈 마커. 방향키로 1px(Shift 10px)씩 옮긴다. 조작 화면 번호도 이 점을 강조한다.
    public int ActiveSlot { get; private set; } = -1;
    private float nudgeRepeatAt, nudgeSolvedAt;
    private bool nudgeSolvePending;
    private GameObject hoverHighlight;   // 클릭하면 선택될 버텍스를 미리 보여주는 표시 (조작 화면에만 보임)
    private GameObject hoverCandidate;
    private Vector2 lastPickMouse = new Vector2(-1f, -1f);
    private int lastPickFrame;
    private Mesh pickMesh;               // 고르기용 메쉬 데이터 캐시 (매 프레임 배열을 새로 만들지 않게)
    private int[] pickTriangles;
    private Vector3[] pickVertices;
    private readonly PatchSnapshot patchSnapshot = new PatchSnapshot();
    private bool alignmentView;          // V 키: 프로젝터에 모델 없이 마커/패치만 표시
    private int savedCullingMask;

    [Serializable]
    public struct VertexStruct
    {
        public int uniqIndex;
        public Vector3 worldCoordinate;
        public Vector2 screenCoordinate;
        public Vector2 screenCoordinateGT;
    }

    private void Start()
    {
        clickedObjects = new GameObject[MaxPoints];
        verticesStruct = new VertexStruct[MaxPoints];
        markers = new Marker[MaxPoints];
        placed = new bool[MaxPoints];
        arrayIndex = 0;

        if (markerManager == null)
        {
            GameObject canvasUI = GameObject.Find("CanvasUI");
            if (canvasUI != null) markerManager = canvasUI.GetComponent<MarkerManager>();
        }
        if (dltSolver == null) dltSolver = GetComponent<DLT_solve>();
        if (projectCam == null) Debug.LogError("[VertexClickTest] projectCam이 지정되지 않았습니다.");
        if (markerManager == null) Debug.LogError("[VertexClickTest] MarkerManager를 찾을 수 없습니다.");

        // 키보드로 UI를 옮겨 다니는 기능(방향키/Enter/Space)을 끈다. 방향키는 마커 미세 조정에 쓰는데,
        // 마지막에 누른 UI가 선택돼 있으면 같은 키로 회전/크기 슬라이더가 움직이거나 R 버튼이 다시 눌렸음.
        // 드래그 시작 거리도 10px -> 2px: 마커를 조금만 옮기려 하면 10px 넘게 움직여야 드래그가 시작됐음.
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null)
        {
            es.sendNavigationEvents = false;
            es.pixelDragThreshold = 2;
        }
    }

    private void Update()
    {
        ReleaseDestroyedSlots();

        // 조작 화면(Display 1) 위에서 Ctrl을 누르고 있을 때: 고를 버텍스 미리 표시 + 클릭으로 선택/해제.
        // Ctrl 없이 클릭해도 되게 하면, 창을 선택하려고 모델 위를 한 번 누른 것만으로 버텍스가 추가되거나
        // 이미 맞춘 마커가 지워졌음 (번호 글자도 클릭이 통과해서 그 버텍스를 해제했음).
        bool overOperatorView = Display.activeEditorGameViewTarget == 0;
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool canPick = overOperatorView && ctrl && MainController.Instance != null && MainController.Instance.IsCalibrationActive
                       && !IsPointerOverUI();
        // 마우스가 움직였을 때만 다시 찾는다 (가만히 있으면 이전 결과 사용, 가끔은 다시 확인)
        Vector2 mouse = Input.mousePosition;
        if (!canPick) hoverCandidate = null;
        else if (mouse != lastPickMouse || Time.frameCount - lastPickFrame > 15 || Input.GetMouseButtonDown(0))
        {
            hoverCandidate = PickVertexSphere(mouse);
            lastPickMouse = mouse;
            lastPickFrame = Time.frameCount;
        }
        GameObject candidate = hoverCandidate;
        UpdateHoverHighlight(candidate);
        if (Input.GetMouseButtonDown(0) && candidate != null) ToggleSphere(candidate);

        if (HotkeyGuard.Blocked) return; // 입력칸에 글자를 치는 중

        NudgeActiveMarker();

        // 'R' 키: 추천점(빨간 구)을 대응점으로 바로 선택
        if (Input.GetKeyDown(KeyCode.R))
        {
            SelectRecommendedVertices();
        }

        // 'T' 키: 패치 끔 -> 선 -> 텍스처
        if (Input.GetKeyDown(KeyCode.T))
        {
            TogglePatchMode();
        }

        // 'V' 키: 정렬 보기 (프로젝터에 모델 없이 마커/패치만)
        if (Input.GetKeyDown(KeyCode.V))
        {
            ToggleAlignmentView();
        }

        // 'L' 키: 실시간 재계산 켜기/끄기
        if (Input.GetKeyDown(KeyCode.L))
        {
            ToggleLiveSolve();
        }

        // '[' / ']' 키: 십자선 크기 줄이기/키우기
        if (Input.GetKeyDown(KeyCode.LeftBracket)) Marker.ChangeSize(-1);
        if (Input.GetKeyDown(KeyCode.RightBracket)) Marker.ChangeSize(+1);

        // 'M' 키: 마커 숨기기/보이기
        if (Input.GetKeyDown(KeyCode.M)) ToggleMarkersHidden();
    }

    // 보정을 마친 뒤 프로젝터에 텍스처만 보이도록 마커(십자선, 번호, 패치)를 숨긴다.
    // 지우는 게 아니라 마커 층을 투명하게 할 뿐이라, 대응점과 보정 결과는 그대로이고 다시 보이게 할 수 있다.
    // 숨긴 동안은 끌기와 방향키가 막혀서 보이지 않는 마커가 움직이지 않는다.
    public bool MarkersHidden { get; private set; }
    private CanvasGroup markerLayer;

    public void ToggleMarkersHidden() => SetMarkersHidden(!MarkersHidden);

    public void SetMarkersHidden(bool hidden)
    {
        if (markerLayer == null && markerManager != null && markerManager.canvasRectTransform != null)
        {
            markerLayer = markerManager.canvasRectTransform.GetComponent<CanvasGroup>();
            if (markerLayer == null) markerLayer = markerManager.canvasRectTransform.gameObject.AddComponent<CanvasGroup>();
        }
        if (markerLayer == null) return;

        MarkersHidden = hidden;
        markerLayer.alpha = hidden ? 0f : 1f;
        markerLayer.blocksRaycasts = !hidden;
        markerLayer.interactable = !hidden;
        Debug.Log(hidden ? "[Marker] 마커 숨김 (M으로 다시 보이기. 대응점과 보정 결과는 그대로)" : "[Marker] 마커 보이기");
    }

    public bool AlignmentView => alignmentView;

    public void ToggleLiveSolve()
    {
        liveSolve = !liveSolve;
        Debug.Log($"[Live] 실시간 재계산: {(liveSolve ? "켜짐" : "꺼짐 (F 키로 직접 계산)")}");
    }

    // 마커를 누르거나 드래그를 시작할 때 Marker가 호출한다. 이 마커가 방향키 미세 조정 대상이 된다.
    // (누르기만 해도 대상이 되게 함. 예전에는 드래그해야만 대상이 돼서, 마커를 고르려면 일단 움직여야 했음)
    public void OnMarkerPressed(int slot) => OnMarkerDragBegin(slot);

    public void OnMarkerDragBegin(int slot)
    {
        ActiveSlot = slot;
        // 슬라이더 등 UI가 선택돼 있으면 방향키가 그 UI를 움직이므로 선택을 푼다 (예: 회전 슬라이더가 돌아감)
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null) es.SetSelectedGameObject(null);
    }

    // 마커 드래그가 끝날 때 Marker가 호출한다. 옮긴 마커가 6개 이상이면 그 마커들로만 다시 푼다.
    public void OnMarkerDragEnd(int slot)
    {
        MarkPlaced(slot);
    }

    private void MarkPlaced(int slot)
    {
        placed[slot] = true;
        SolveLive();
    }

    private void SolveLive()
    {
        if (liveSolve && dltSolver != null && PlacedCount() >= 6) dltSolver.PerformDLT(false);
    }

    // 방향키: 마지막으로 누른/끈 마커를 1px씩, Shift를 누르면 10px씩 옮긴다. 누르고 있으면 반복.
    // 누르고 있는 동안 다시 계산은 0.2초에 한 번, 손을 떼면 마지막으로 한 번 (매 반복마다 풀면 끊겼음)
    private void NudgeActiveMarker()
    {
        if (ActiveSlot < 0 || clickedObjects[ActiveSlot] == null || MarkersHidden) { nudgeSolvePending = false; return; }

        Vector2 dir = Vector2.zero;
        if (Input.GetKey(KeyCode.LeftArrow)) dir.x -= 1f;
        if (Input.GetKey(KeyCode.RightArrow)) dir.x += 1f;
        if (Input.GetKey(KeyCode.UpArrow)) dir.y += 1f;
        if (Input.GetKey(KeyCode.DownArrow)) dir.y -= 1f;
        if (dir == Vector2.zero)
        {
            if (nudgeSolvePending) { nudgeSolvePending = false; nudgeSolvedAt = Time.unscaledTime; SolveLive(); }
            return;
        }

        bool firstPress = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow)
                          || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow);
        if (!firstPress && Time.unscaledTime < nudgeRepeatAt) return;
        nudgeRepeatAt = Time.unscaledTime + (firstPress ? 0.35f : 0.05f); // 누르고 있으면 0.35초 뒤부터 반복

        float step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 10f : 1f;
        verticesStruct[ActiveSlot].screenCoordinate += dir * step; // 마커는 LateUpdate에서 이 좌표로 옮겨짐
        placed[ActiveSlot] = true;
        nudgeSolvePending = true;
        if (Time.unscaledTime - nudgeSolvedAt > 0.2f)
        {
            nudgeSolvePending = false;
            nudgeSolvedAt = Time.unscaledTime;
            SolveLive();
        }
    }

    public bool IsPlaced(int slot) => placed != null && placed[slot] && clickedObjects[slot] != null;

    // 다른 마커들과 유독 안 맞는 마커 (DLT_solve가 보정할 때마다 정함). 빨간색으로 표시된다.
    public bool IsSuspect(int slot) => dltSolver != null && dltSolver.SuspectSlot == slot && clickedObjects[slot] != null;

    public int PlacedCount()
    {
        int count = 0;
        for (int i = 0; i < clickedObjects.Length; i++) if (IsPlaced(i)) count++;
        return count;
    }

    // DLT 결과가 projectCam에 적용된 뒤 호출된다 (F 키, 실시간 재계산 모두).
    // 패치를 새 카메라 기준으로 다시 잘라, 그 버텍스 주변의 더 정확한 모양으로 바꾼다.
    public void OnCameraSolved()
    {
        RefreshPatches();
    }

    // 모든 마커의 패치를 지금 카메라 기준으로 다시 자른다 (패치를 켤 때, 보정 후)
    private void RefreshPatches()
    {
        GameObject target = MainController.Instance != null ? MainController.Instance.targetMesh : null;
        if (target == null || !showPatches || patchSize <= 0) return;
        patchSnapshot.creaseAngle = EffectiveCreaseAngle(target);
        patchSnapshot.EnsureCaptured(projectCam, target);

        for (int i = 0; i < clickedObjects.Length; i++)
        {
            if (clickedObjects[i] == null || markers[i] == null) continue;

            Vector3 projected = projectCam.WorldToScreenPoint(clickedObjects[i].transform.position);
            if (projected.z <= 0f) continue; // 잘못 풀려 버텍스가 카메라 뒤면 좌표가 뒤집히므로 이전 패치 유지
            Vector2 predicted = new Vector2(projected.x, projected.y);
            markers[i].SetPatches(
                patchSnapshot.Crop(PatchSnapshot.Mode.Lines, predicted, patchSize),
                patchSnapshot.Crop(PatchSnapshot.Mode.Texture, predicted, patchSize),
                patchMode, patchSize);
        }
    }

    public int SelectedCount()
    {
        int count = 0;
        foreach (GameObject o in clickedObjects) if (o != null) count++;
        return count;
    }

    private void OnDestroy()
    {
        patchSnapshot.Release();
        if (hoverHighlight != null)
        {
            Destroy(hoverHighlight.GetComponent<Renderer>().sharedMaterial);
            Destroy(hoverHighlight);
        }
    }

    // 끔 -> 선 -> 텍스처 -> 끔
    public void TogglePatchMode()
    {
        if (!showPatches) { showPatches = true; patchMode = PatchSnapshot.Mode.Lines; }
        else if (patchMode == PatchSnapshot.Mode.Lines) patchMode = PatchSnapshot.Mode.Texture;
        else showPatches = false;

        if (showPatches)
        {
            RefreshPatches();
            foreach (Marker m in markers) if (m != null) m.SetPatchMode(patchMode);
        }
        else
        {
            foreach (Marker m in markers) if (m != null) m.ClearPatches();
        }
        Debug.Log($"[Patch] 패치: {PatchLabel}");
    }

    public string PatchLabel => !showPatches ? "끔" : patchMode == PatchSnapshot.Mode.Lines ? "선" : "텍스처";

    public void ToggleAlignmentView()
    {
        alignmentView = !alignmentView;
        if (alignmentView)
        {
            savedCullingMask = projectCam.cullingMask;
            projectCam.cullingMask = 1 << LayerMask.NameToLayer("UI");
        }
        else
        {
            projectCam.cullingMask = savedCullingMask;
        }
        Debug.Log($"[Patch] 정렬 보기: {(alignmentView ? "켜짐 (마커/패치만 투사)" : "꺼짐")}");
    }

    // 마커에 주변 모양 패치를 붙인다. 패치 중심 = 현재 프로젝터 카메라로 본 버텍스 위치.
    private void AttachPatch(Marker marker, Vector2 screen)
    {
        GameObject target = MainController.Instance != null ? MainController.Instance.targetMesh : null;
        if (marker == null || target == null || !showPatches || patchSize <= 0) return;

        patchSnapshot.creaseAngle = EffectiveCreaseAngle(target);
        patchSnapshot.EnsureCaptured(projectCam, target);
        marker.SetPatches(
            patchSnapshot.Crop(PatchSnapshot.Mode.Lines, screen, patchSize),
            patchSnapshot.Crop(PatchSnapshot.Mode.Texture, screen, patchSize),
            patchMode, patchSize);
    }

    // 선 모드 모서리 기준 각도: Inspector 값(>0) -> 라이브러리 항목 값(>0) -> 자동
    private float EffectiveCreaseAngle(GameObject target)
    {
        string source;
        float angle;
        MeshEntry entry = MainController.Instance != null ? MainController.Instance.currentEntry : null;
        float median = 0f;
        if (patchCreaseAngle > 0f) { angle = patchCreaseAngle; source = "Inspector 지정"; }
        else if (entry != null && entry.patchCreaseAngle > 0f) { angle = entry.patchCreaseAngle; source = "라이브러리 지정"; }
        else { angle = patchSnapshot.AutoCreaseAngle(target, out median); source = $"자동, 모서리 각도 중앙값 {median:F1}도"; }

        if (creaseAngleLoggedFor != target)
        {
            creaseAngleLoggedFor = target;
            Debug.Log($"[Patch] 선 패치 모서리 기준: {angle:F1}도 ({source})");
        }
        return angle;
    }

    // 표시 중인 추천점들을 대응점으로 선택한다. 기존 선택(마커 포함)은 비우고,
    // 추천 순서(가장 salient한 점이 먼저)대로 슬롯 0번부터 채운다.
    public void SelectRecommendedVertices()
    {
        MainController main = MainController.Instance;
        if (main == null) return;
        if (main.currentCalibrator == null)
        {
            Debug.LogWarning("[Recommend] 모델을 먼저 선택하세요.");
            return;
        }
        if (main.IsSaliencyPending)
        {
            Debug.LogWarning("[Recommend] saliency 계산 중입니다. 끝나면 추천점이 표시되니 그때 다시 누르세요.");
            return;
        }

        // R 하나로 시작할 수 있게, 꺼져 있으면 캘리브레이션 모드(1)와 추천점 표시(3)를 켠다.
        // (모델을 바꾸면 둘 다 꺼지는데, 리허설에서 이걸 다시 켜야 하는지 몰라 마커가 안 생겼음)
        if (!main.IsCalibrationActive) main.ToggleCalibration(true);
        if (!main.IsRecommendationActive) main.ToggleRecommendation(true);

        ProjectionMappingCalibrator calibrator = main.currentCalibrator;
        List<Vector3> recommended = calibrator.GetRecommendedPositions();
        if (recommended.Count == 0)
        {
            Debug.LogWarning("[Recommend] 추천점을 찾지 못했습니다. 모델이 프로젝터 화면 안에 보이는지 확인하세요.");
            return;
        }

        if (main.sphereGenerator == null)
        {
            Debug.LogError("[Recommend] CreateSphereAtVertex를 찾을 수 없습니다.");
            return;
        }

        // 추천점과 버텍스 구는 같은 메쉬 버텍스에서 나온 좌표라 거의 일치한다. 허용 오차는 메쉬 크기의 0.1%.
        float tolerance = 1e-3f;
        if (calibrator.meshFilter != null && calibrator.meshFilter.TryGetComponent(out Renderer meshRenderer))
            tolerance = Mathf.Max(tolerance, meshRenderer.bounds.size.magnitude * 1e-3f);

        ClearSelection();

        // 마커를 맞추는 동안은 텍스처 없이 모델 모양만 투사한다 (다 맞춘 뒤 X로 입힘)
        if (TextureSequenceAnimator.Instance != null) TextureSequenceAnimator.Instance.SetTextureVisible(false);

        int selected = 0;
        foreach (Vector3 position in recommended)
        {
            GameObject sphere = main.sphereGenerator.FindSphereAt(position, tolerance);
            if (sphere == null)
            {
                Debug.LogWarning($"[Recommend] {position} 위치의 버텍스 구를 찾지 못했습니다.");
                continue;
            }
            if (ArrayContains(clickedObjects, sphere)) continue;

            AddObject(sphere);
            selected++;
        }

        Debug.Log($"[Recommend] 추천점 {recommended.Count}개 중 {selected}개를 대응점으로 선택했습니다.");

        // 모델이 프로젝터 화면에서 너무 작으면 마커가 겹쳐서 맞출 수 없다.
        Rect spread = Rect.MinMaxRect(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
        for (int i = 0; i < clickedObjects.Length; i++)
        {
            if (clickedObjects[i] == null) continue;
            Vector2 p = verticesStruct[i].screenCoordinateGT;
            spread.xMin = Mathf.Min(spread.xMin, p.x); spread.yMin = Mathf.Min(spread.yMin, p.y);
            spread.xMax = Mathf.Max(spread.xMax, p.x); spread.yMax = Mathf.Max(spread.yMax, p.y);
        }
        if (selected > 0 && Mathf.Max(spread.width, spread.height) < MinMarkerSpreadPixels)
            Debug.LogWarning($"[Recommend] 추천점들이 프로젝터 화면에서 {spread.width:F1}x{spread.height:F1}px 안에 몰려 있습니다. 스케일 슬라이더로 모델을 키우세요.");
    }

    // 모든 선택을 해제하고 마커도 지운다.
    public void ClearSelection()
    {
        if (dltSolver != null) dltSolver.ClearStatus(); // 이전 마커들의 '보정 보류' 이유, 빨간 마커 표시 지우기
        for (int i = 0; i < clickedObjects.Length; i++)
        {
            if (ReferenceEquals(clickedObjects[i], null)) continue;
            if (clickedObjects[i] != null) SetSphereSelected(clickedObjects[i], false);
            ReleaseSlot(i);
        }
    }

    // 이미 선택된 구면 해제, 아니면 선택
    private void ToggleSphere(GameObject target)
    {
        if (ArrayContains(clickedObjects, target)) RemoveObject(target);
        else AddObject(target);
    }

    // UI(버튼, 슬라이더 등) 위면 뒤에 있는 버텍스를 고르지 않는다.
    private static bool IsPointerOverUI()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        return es != null && es.IsPointerOverGameObject();
    }

    // 커서 근처의 보이는 버텍스 구. 구는 화면에서 2px 정도라 직접 맞히기 어려워서,
    // 모델 표면을 맞히면 그 삼각형의 세 꼭짓점 중 커서에 가장 가까운 것을 고른다 (pickRadiusPixels 이내).
    // 표면에 가려진 뒤쪽 버텍스는 고르지 않는다.
    private GameObject PickVertexSphere(Vector2 mouse)
    {
        Camera cam = Camera.main;
        MainController main = MainController.Instance;
        if (cam == null || main == null || main.sphereGenerator == null) return null;

        if (!Physics.Raycast(cam.ScreenPointToRay(mouse), out RaycastHit hit))
            return PickNearestVisibleOnScreen(cam, main.sphereGenerator, mouse); // 모델 가장자리 바로 바깥을 누른 경우
        if (hit.collider.CompareTag("SphereMainCam")) return hit.collider.gameObject; // 구를 직접 맞힘

        var meshCollider = hit.collider as MeshCollider;
        if (meshCollider == null || meshCollider.sharedMesh == null || hit.triangleIndex < 0) return null;
        if (pickMesh != meshCollider.sharedMesh)
        {
            pickMesh = meshCollider.sharedMesh;
            pickTriangles = pickMesh.triangles;
            pickVertices = pickMesh.vertices;
        }

        float best = pickRadiusPixels;
        Vector3 bestWorld = default, bestLocal = default;
        bool found = false;
        for (int k = 0; k < 3; k++)
        {
            Vector3 local = pickVertices[pickTriangles[hit.triangleIndex * 3 + k]];
            Vector3 world = meshCollider.transform.TransformPoint(local);
            float d = Vector2.Distance(cam.WorldToScreenPoint(world), mouse);
            if (d < best) { best = d; bestWorld = world; bestLocal = local; found = true; }
        }
        return found ? main.sphereGenerator.FindSphereForVertex(bestLocal, bestWorld) : null;
    }

    // 화면에서 커서에 가장 가까운 보이는 구 (pickRadiusPixels 이내).
    // 가장자리 버텍스는 조금만 바깥을 눌러도 표면에 닿지 않아서, 이때는 화면 거리로 찾는다.
    private GameObject PickNearestVisibleOnScreen(Camera cam, CreateSphereAtVertex spheres, Vector2 mouse)
    {
        // 커서가 모델 근처가 아니면 바로 끝 (빈 곳 위에서 매 프레임 구 수만 개를 훑지 않게)
        GameObject model = MainController.Instance.targetMesh;
        if (model == null || !NearModelOnScreen(cam, model, mouse, pickRadiusPixels)) return null;

        // 화면 좌표는 행렬로 직접 계산 (WorldToScreenPoint를 수만 번 부르면 수 ms 걸림)
        Matrix4x4 viewProj = cam.projectionMatrix * cam.worldToCameraMatrix;
        Rect pixels = cam.pixelRect;
        var candidates = new List<(float distance, Transform sphere)>();
        foreach (Transform sphere in spheres.ActiveSpheres)
        {
            if (sphere == null) continue;
            Vector3 p = sphere.position;
            Vector4 clip = viewProj * new Vector4(p.x, p.y, p.z, 1f);
            if (clip.w <= 0f) continue;
            float sx = pixels.x + (clip.x / clip.w * 0.5f + 0.5f) * pixels.width;
            float sy = pixels.y + (clip.y / clip.w * 0.5f + 0.5f) * pixels.height;
            float d = Vector2.Distance(new Vector2(sx, sy), mouse);
            if (d <= pickRadiusPixels) candidates.Add((d, sphere));
        }
        candidates.Sort((a, b) => a.distance.CompareTo(b.distance));

        // 카메라에서 그 구로 쏜 광선이 구 자신에 먼저 닿으면 보이는 버텍스 (모델에 가려졌으면 표면에 먼저 닿음)
        foreach (var (_, sphere) in candidates)
        {
            Vector3 toSphere = sphere.position - cam.transform.position;
            if (Physics.Raycast(cam.transform.position, toSphere, out RaycastHit hit, toSphere.magnitude + 1f)
                && hit.collider.transform == sphere)
                return sphere.gameObject;
        }
        return null;
    }

    private GameObject boundsModel;
    private readonly List<Renderer> modelRenderers = new List<Renderer>();

    private bool NearModelOnScreen(Camera cam, GameObject model, Vector2 mouse, float margin)
    {
        // 모델 아래에 버텍스 구 수만 개가 붙어 있어서, 모델 자체 렌더러는 모델이 바뀔 때만 찾아 둔다
        if (boundsModel != model)
        {
            boundsModel = model;
            modelRenderers.Clear();
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
                if (r.gameObject.layer == model.layer) modelRenderers.Add(r);
        }

        bool any = false;
        Bounds b = default;
        foreach (Renderer r in modelRenderers)
        {
            if (r == null) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) return false;

        Rect rect = Rect.MinMaxRect(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
            Vector3 s = cam.WorldToScreenPoint(corner);
            rect.xMin = Mathf.Min(rect.xMin, s.x); rect.yMin = Mathf.Min(rect.yMin, s.y);
            rect.xMax = Mathf.Max(rect.xMax, s.x); rect.yMax = Mathf.Max(rect.yMax, s.y);
        }
        return mouse.x >= rect.xMin - margin && mouse.x <= rect.xMax + margin && mouse.y >= rect.yMin - margin && mouse.y <= rect.yMax + margin;
    }

    // 클릭하면 선택될 버텍스를 노란 점으로 미리 보여준다. 조작 화면에만 보이고(구와 같은 레이어), 모델에 가려지지 않게 그린다.
    private void UpdateHoverHighlight(GameObject candidate)
    {
        if (candidate == null)
        {
            if (hoverHighlight != null) hoverHighlight.SetActive(false);
            return;
        }

        if (hoverHighlight == null)
        {
            hoverHighlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hoverHighlight.name = "VertexHoverHighlight";
            Destroy(hoverHighlight.GetComponent<Collider>());
            var material = new Material(Shader.Find("Hidden/Internal-Colored"));
            material.SetColor("_Color", new Color(1f, 0.9f, 0f, 1f));
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            material.SetInt("_ZWrite", 0);
            material.renderQueue = 4000;
            hoverHighlight.GetComponent<Renderer>().sharedMaterial = material;
        }

        hoverHighlight.layer = candidate.layer;
        hoverHighlight.SetActive(true);
        hoverHighlight.transform.position = candidate.transform.position;

        // 화면에서 약 12px 크기
        Camera cam = Camera.main;
        float depth = Vector3.Dot(candidate.transform.position - cam.transform.position, cam.transform.forward);
        float worldPerPixel = 2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / cam.pixelHeight;
        hoverHighlight.transform.localScale = Vector3.one * (12f * worldPerPixel);
    }

    // 오브젝트 추가 함수
    private void AddObject(GameObject target)
    {
        int slot = Array.IndexOf(clickedObjects, null);

        if (slot == -1)
        {
            Debug.LogWarning("더 이상 선택할 수 없습니다 (배열 가득 참).");
            return;
        }

        Vector3 world = target.transform.position;

        // 마커 시작 위치 = 현재 프로젝터 카메라로 이 버텍스를 투영한 위치.
        // (예전에는 Main Camera 픽셀 좌표를 써서, 해상도가 다른 프로젝터 화면과 좌표계가 섞였음)
        Vector3 projected = projectCam.WorldToScreenPoint(world);
        Vector2 screen = new Vector2(projected.x, projected.y);
        if (projected.z <= 0f || screen.x < 0f || screen.y < 0f || screen.x > projectCam.pixelWidth || screen.y > projectCam.pixelHeight)
            Debug.LogWarning($"[Select] {target.name}이(가) 프로젝터 화면 밖에 투영됩니다: {projected}");

        clickedObjects[slot] = target;
        placed[slot] = false;
        verticesStruct[slot] = new VertexStruct
        {
            uniqIndex = slot,
            worldCoordinate = world,
            screenCoordinate = screen,
            screenCoordinateGT = screen
        };
        if (markerManager != null) markers[slot] = markerManager.CreateMarker(slot, screen, projectCam, this);
        AttachPatch(markers[slot], screen);
        SetSphereSelected(target, true);
        if (MarkersHidden) SetMarkersHidden(false); // 새 마커를 만들면(R, Ctrl+클릭) 다시 보이게

        arrayIndex++;
        Debug.Log($"[Select] 추가됨 ({arrayIndex}개, 마커 {slot + 1}번): {target.name}");
    }

    // 오브젝트 제거 함수
    private void RemoveObject(GameObject target)
    {
        for (int i = 0; i < clickedObjects.Length; i++)
        {
            if (clickedObjects[i] == target)
            {
                SetSphereSelected(target, false);
                ReleaseSlot(i);
                Debug.Log($"[Deselect] 해제됨 ({arrayIndex}개 남음): {target.name}");
                return;
            }
        }
    }

    // 슬롯을 비우고 그 슬롯의 마커도 지운다.
    private void ReleaseSlot(int slot)
    {
        if (markers[slot] != null) Destroy(markers[slot].gameObject);
        markers[slot] = null;
        clickedObjects[slot] = null;
        placed[slot] = false;
        if (ActiveSlot == slot) ActiveSlot = -1;
        if (dltSolver != null && dltSolver.SuspectSlot == slot) dltSolver.ClearSuspect();
        verticesStruct[slot] = new VertexStruct();
        arrayIndex--;
    }

    // 메쉬를 바꾸면 구들이 새로 만들어지면서 선택돼 있던 구가 파괴된다. 그런 슬롯은 마커와 함께 정리한다.
    private void ReleaseDestroyedSlots()
    {
        for (int i = 0; i < clickedObjects.Length; i++)
        {
            // 참조는 남아 있는데 Unity 오브젝트는 파괴된 상태
            if (!ReferenceEquals(clickedObjects[i], null) && clickedObjects[i] == null)
                ReleaseSlot(i);
        }
    }

    private static void SetSphereSelected(GameObject sphere, bool selected)
    {
        if (sphere.TryGetComponent(out VertexInteraction interaction))
            interaction.SetSelected(selected);
    }

    private bool ArrayContains(GameObject[] array, GameObject obj)
    {
        foreach (var item in array)
        {
            if (item == obj) return true;
        }
        return false;
    }
}

//using System.Collections;
//using System.Collections.Generic;
//using Unity.VisualScripting;
//using UnityEngine;
//using UnityEngine.UI;
//public class VertexClickTest : MonoBehaviour
//{
//    public GameObject[] clickedObjects; // Array to store clicked objects
//    public int arrayIndex;
//    public Camera projectCam;

//    public struct VertexStruct
//    {
//        public int uniqIndex;
//        public Vector3 worldCoordinate;
//        public Vector2 screenCoordinate;
//        public Vector2 screenCoordinateGT;
//        public VertexStruct(int vertexIndex, Vector3 worldCoord, Vector2 screenCoord, Vector2 screenCoordGT)
//        {
//            this.uniqIndex = vertexIndex;
//            this.worldCoordinate = worldCoord;
//            this.screenCoordinate = screenCoord;
//            this.screenCoordinateGT = screenCoordGT;
//            //원래는 screenCoordinate에 this가 붙어있지 않았는데 이게 원인이었을까?
//        }
//    }
//    public VertexStruct[] verticesStruct;



//    private void Start()
//    {

//        clickedObjects = new GameObject[10]; // Initializing arrays with size 10
//        verticesStruct = new VertexStruct[12];
//        arrayIndex = 0;
//    }

//    private void Update()
//    {
//        // Check if the left mouse button is clicked
//        if (Input.GetMouseButtonDown(0) && Display.activeEditorGameViewTarget == 0)
//        {
//            // Shoot a ray from the camera to the mouse position
//            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
//            RaycastHit hit;

//            // Check if the ray hits an object
//            if (Physics.Raycast(ray, out hit))
//            {
//                // Store clicked object
//                GameObject clickedObject = hit.collider.gameObject;

//                // Check if the clicked object is not already in the array
//                if (!ArrayContains(clickedObjects, clickedObject))
//                {
//                    // Find first empty slot
//                    int index = System.Array.IndexOf(clickedObjects, null);
//                    Debug.Log("index: " + index);

//                    if (index != -1 && clickedObject.tag == "SphereMainCam")
//                    {
//                        //임시로 "SphereIn2D" 태그에서 현재태그로 변경. -> VertexInteraction에서 array에 추가하는 코드로 변경해야 함
//                        //여기 확인 필요
//                        clickedObjects[index] = clickedObject;
//                        Debug.Log("Object already clicked vertex MainCam: " + clickedObject.name);
//                        verticesStruct[index].uniqIndex = index;
//                        verticesStruct[index].worldCoordinate = clickedObject.transform.position;
//                        //verticesStruct[index].screenCoordinate = new Vector2(projectCam.WorldToScreenPoint(clickedObject.transform.position).x, projectCam.pixelHeight - projectCam.WorldToScreenPoint(clickedObject.transform.position).y);
//                        arrayIndex++;
//                        Debug.Log("Working");
//                    }
//                    else
//                    {
//                        Debug.Log("Object already clicked vertex MainCam: " + clickedObject.name);
//                        Debug.LogWarning("Clicked objects array is full. Increase array size if needed.");
//                    }

//                }
//                else
//                {
//                    Debug.Log("Object already clicked: " + clickedObject.name);
//                }
//            }
//        }
//    }


//    private bool ArrayContains(GameObject[] array, GameObject obj)
//    {
//        foreach (GameObject item in array)
//        {
//            if (item == obj)
//                return true;
//        }
//        return false;
//    }
//    // private void OnMouseDown()
//    // {
//    //     renderer.material.color = renderer.material.color == originalColor ? Color.red : originalColor;
//    //     //Debug.Log(this.transform.position);
//    //     if (!copied){
//    //         GameObject copy = Instantiate(gameObject);
//    //         copy.transform.Translate(0f,0f,-10f);
//    //         copied = true;
//    //     }

//    // }
//    // Function to check if an array contains a specific object

//}