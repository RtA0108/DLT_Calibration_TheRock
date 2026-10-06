using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

// 프로젝터 화면(CanvasUI)에 뜨는 대응점 마커.
// 드래그한 위치를 프로젝터 카메라의 스크린 픽셀 좌표로 VertexClickTest에 기록한다.
// 모양은 가운데가 빈 십자선이라, 맞출 실물 지점을 가리지 않는다. 색: 흰색 = 아직 안 맞춤, 초록 = 맞춤,
// 노랑 = 방향키로 움직일 마커(마지막으로 끈 것), 빨강 = 다른 마커들과 유독 안 맞는 마커.
public class Marker : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public TextMeshProUGUI markerText;

    public int IDX;            // VertexClickTest 슬롯 인덱스
    public Camera projectCam;  // 이 마커를 그리는 카메라 (CanvasUI의 worldCamera)

    // 십자선 크기: 팔 길이(px) 단계. [ / ] 키로 바꾸고, 바꾼 값은 다음 실행에도 유지된다.
    // 예전에는 팔 12px(전체 약 32px) 고정이라, 실물이 프로젝터 화면에서 작으면 마커가 실물을 가리고 서로 겹쳤음.
    private static readonly int[] ArmLengthSteps = { 3, 4, 6, 8, 12, 16 };
    private const string SizePrefKey = "MarkerArmLength";
    private const int DefaultArmLength = 6;
    private static int armLength = -1;

    public static int ArmLength
    {
        get
        {
            if (armLength < 0)
            {
                int saved = PlayerPrefs.GetInt(SizePrefKey, DefaultArmLength);
                armLength = System.Array.IndexOf(ArmLengthSteps, saved) >= 0 ? saved : DefaultArmLength;
            }
            return armLength;
        }
    }

    // delta만큼 한 단계씩 작게(-)/크게(+)
    public static void ChangeSize(int delta)
    {
        int index = Mathf.Clamp(System.Array.IndexOf(ArmLengthSteps, ArmLength) + delta, 0, ArmLengthSteps.Length - 1);
        armLength = ArmLengthSteps[index];
        PlayerPrefs.SetInt(SizePrefKey, armLength);
        Debug.Log($"[Marker] 십자선 크기: 팔 {armLength}px (전체 약 {2 * (ArmGapFor(armLength) + armLength)}px)");
    }

    private static int ArmGapFor(int arm) => Mathf.Max(2, Mathf.RoundToInt(arm / 3f)); // 가운데 빈 칸 (반지름)

    private static readonly Color UnplacedColor = Color.white;
    private static readonly Color PlacedColor = new Color(0.35f, 1f, 0.35f);
    private static readonly Color ActiveColor = new Color(1f, 0.9f, 0f);
    private static readonly Color SuspectColor = new Color(1f, 0.25f, 0.25f);

    private RectTransform rectTransform;
    private RectTransform canvasRect;
    // 마커 캔버스는 프로젝터 화면에 바로 그리는 Overlay라 카메라가 필요 없다(null).
    // 예전처럼 카메라 캔버스(Screen Space - Camera)면 보정된 투영 행렬에 기울어짐이 있을 때
    // 십자선이 같이 기울고 늘어났음 (리허설: 기울어짐 -2693인 결과에서 세로 팔이 3배로 비스듬해짐)
    private Camera uiCamera;
    private VertexClickTest owner;
    private Vector2 dragOffset;
    private bool dragging;
    private Image[] crosshairCore, crosshairOutline;
    private int builtArmLength;
    private Color currentColor;

    // 패치(선택): 마커 주변 모양. 중심이 대응점이고, 패치 어디를 잡아도 드래그된다.
    // 패치들은 마커들보다 아래 층(PatchLayer)에 모아 둔다. 패치끼리 겹쳐도 십자선과 번호가 가려지지 않게.
    private RawImage patchImage;
    private Texture2D linesPatch, texturePatch;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    // 드래그 중이 아니면 항상 저장된 프로젝터 좌표에 다시 놓는다.
    // (방향키 미세 조정도 저장된 좌표를 바꾸는 것이라 여기서 반영됨. 카메라 캔버스였을 때는
    //  캘리브레이션으로 카메라가 바뀌면 그대로 둔 마커가 움직여서 이렇게 했음)
    void LateUpdate()
    {
        if (owner == null || canvasRect == null) return;
        if (crosshairCore != null && builtArmLength != ArmLength) LayoutCrosshair();
        if (!dragging && ScreenToCanvas(owner.verticesStruct[IDX].screenCoordinate, out Vector2 local))
        {
            rectTransform.localPosition = new Vector3(local.x, local.y, 0f);
            SyncPatchPosition();
        }

        Color color = dragging ? ActiveColor
                    : owner.IsSuspect(IDX) ? SuspectColor
                    : owner.ActiveSlot == IDX ? ActiveColor
                    : owner.IsPlaced(IDX) ? PlacedColor : UnplacedColor;
        if (color != currentColor) SetColor(color);
    }

    void OnDestroy()
    {
        ClearPatches();
    }

    // ---------------------------------------------------------------- 모양
    private void BuildCrosshair()
    {
        // 원래 프리팹의 빨간 점은 투명하게 두고 잡는 영역으로만 쓴다.
        var hitArea = GetComponent<Image>();
        if (hitArea != null) hitArea.color = new Color(0f, 0f, 0f, 0f);

        crosshairCore = new Image[4];
        crosshairOutline = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            // 검은 테두리를 먼저 그려서 밝은 실물 위에서도 보이게
            crosshairOutline[i] = NewBar("Outline", new Color(0f, 0f, 0f, 0.85f));
            crosshairCore[i] = NewBar("Arm", UnplacedColor);
        }

        markerText.raycastTarget = false;
        markerText.rectTransform.sizeDelta = new Vector2(30f, 18f);
        markerText.outlineWidth = 0.25f;
        markerText.outlineColor = Color.black;
        markerText.transform.SetAsLastSibling();
        LayoutCrosshair();
        SetColor(UnplacedColor);
    }

    // 지금 십자선 크기(ArmLength)에 맞게 팔, 잡는 영역, 번호 위치를 다시 잡는다.
    private void LayoutCrosshair()
    {
        int arm = ArmLength, gap = ArmGapFor(arm);
        float thick = arm >= 6 ? 2f : 1f; // 아주 작을 때는 1px 선
        Vector2[] dirs = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };
        for (int i = 0; i < 4; i++)
        {
            Vector2 center = dirs[i] * (gap + arm / 2f);
            bool horizontal = dirs[i].y == 0f;
            SetBar(crosshairOutline[i], center, horizontal ? new Vector2(arm + 2f, thick + 2f) : new Vector2(thick + 2f, arm + 2f));
            SetBar(crosshairCore[i], center, horizontal ? new Vector2(arm, thick) : new Vector2(thick, arm));
        }

        // 잡는 영역은 십자선보다 조금 크게 (가까운 마커끼리 잡는 영역이 덜 겹치게 크기에 맞춤)
        float grab = Mathf.Max(18f, 2f * (gap + arm) + 6f);
        rectTransform.sizeDelta = new Vector2(grab, grab);

        markerText.fontSize = Mathf.Clamp(Mathf.RoundToInt(8f + arm * 0.6f), 10, 16);
        markerText.rectTransform.anchoredPosition = new Vector2(gap + arm * 0.75f + 4f, gap + arm * 0.6f + 4f); // 오른쪽 위
        builtArmLength = arm;
    }

    private static void SetBar(Image bar, Vector2 center, Vector2 size)
    {
        bar.rectTransform.anchoredPosition = center;
        bar.rectTransform.sizeDelta = size;
    }

    private Image NewBar(string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private void SetColor(Color color)
    {
        currentColor = color;
        if (crosshairCore != null) foreach (Image arm in crosshairCore) arm.color = color;
        markerText.color = color;
    }

    // ---------------------------------------------------------------- 패치 (선택 기능)
    public void SetPatches(Texture2D lines, Texture2D texture, PatchSnapshot.Mode mode, int size)
    {
        // 다시 호출되면(캘리브레이션 후 패치 갱신) 이전 텍스처는 정리
        if (linesPatch != null && linesPatch != lines) Destroy(linesPatch);
        if (texturePatch != null && texturePatch != texture) Destroy(texturePatch);
        linesPatch = lines;
        texturePatch = texture;

        if (patchImage == null)
        {
            var go = new GameObject($"Patch {IDX + 1}", typeof(RectTransform), typeof(RawImage), typeof(PatchDragForwarder));
            go.transform.SetParent(GetPatchLayer(), false);
            go.GetComponent<PatchDragForwarder>().marker = this;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            patchImage = go.GetComponent<RawImage>();
            SyncPatchPosition();
        }
        SetPatchMode(mode);
    }

    public void ClearPatches()
    {
        if (patchImage != null) Destroy(patchImage.gameObject);
        if (linesPatch != null) Destroy(linesPatch);
        if (texturePatch != null) Destroy(texturePatch);
        patchImage = null;
        linesPatch = texturePatch = null;
    }

    // Canvas의 맨 첫 자식(= 가장 먼저 그려짐)으로 패치 전용 층을 둔다.
    private Transform GetPatchLayer()
    {
        Transform layer = canvasRect.Find("PatchLayer");
        if (layer == null)
        {
            var go = new GameObject("PatchLayer", typeof(RectTransform));
            layer = go.transform;
            layer.SetParent(canvasRect, false);
            var rt = (RectTransform)layer;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
        }
        layer.SetAsFirstSibling();
        return layer;
    }

    private void SyncPatchPosition()
    {
        if (patchImage != null) patchImage.transform.position = rectTransform.position;
    }

    public void SetPatchMode(PatchSnapshot.Mode mode)
    {
        if (patchImage != null) patchImage.texture = mode == PatchSnapshot.Mode.Lines ? linesPatch : texturePatch;
    }

    // ---------------------------------------------------------------- 배치와 드래그
    public void SetMarker(int slot, Vector2 screenPosition, RectTransform canvasRectTransform, Camera cam, VertexClickTest clickTest)
    {
        IDX = slot;
        canvasRect = canvasRectTransform;
        projectCam = cam;
        owner = clickTest;
        Canvas canvas = canvasRect.GetComponentInParent<Canvas>().rootCanvas;
        uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : projectCam;

        markerText.text = (slot + 1).ToString();
        BuildCrosshair();

        // 스크린 좌표 -> Canvas 로컬 좌표 (Overlay면 카메라 없이, 카메라 캔버스면 카메라를 넘겨서).
        // (예전에는 마커가 아니라 Canvas 자체를 옮기고 있었음)
        if (ScreenToCanvas(screenPosition, out Vector2 local))
            rectTransform.localPosition = new Vector3(local.x, local.y, 0f);
    }

    // 누르기만 해도 방향키로 움직일 마커가 된다 (끌지 않고 골라서 방향키로만 맞출 수 있게)
    public void OnPointerDown(PointerEventData eventData)
    {
        if (owner != null) owner.OnMarkerPressed(IDX);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        dragging = true;
        owner.OnMarkerDragBegin(IDX);
        // 마커를 잡은 지점과 마커 중심의 차이를 기억해서, 드래그 시작 때 마커가 튀지 않게 한다.
        if (ScreenToCanvas(eventData.position, out Vector2 local))
            dragOffset = (Vector2)rectTransform.localPosition - local;
    }

    public void OnDrag(PointerEventData eventData)
    {
        // delta를 더하는 대신 포인터 위치를 직접 변환한다 (캔버스 1단위가 1픽셀이 아닐 수 있어서).
        if (!ScreenToCanvas(eventData.position, out Vector2 local)) return;

        Vector2 p = local + dragOffset;
        rectTransform.localPosition = new Vector3(p.x, p.y, 0f);
        SyncPatchPosition();

        // 마커가 실제로 그려지는 프로젝터 픽셀 좌표 (좌하단 원점)
        Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(uiCamera, rectTransform.position);
        owner.verticesStruct[IDX].screenCoordinate = screenPosition;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        dragging = false;
        var v = owner.verticesStruct[IDX];
        float distance = Vector2.Distance(v.screenCoordinateGT, v.screenCoordinate);
        Debug.Log($"[Marker {IDX + 1}] {v.screenCoordinate} (GT에서 {distance:F2}px 이동)");
        owner.OnMarkerDragEnd(IDX);
    }

    private bool ScreenToCanvas(Vector2 screenPosition, out Vector2 local)
    {
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, uiCamera, out local);
    }
}
