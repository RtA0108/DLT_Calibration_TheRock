using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// 조작 화면(Display 1)의 보조 UI. 실행할 때 Canvas 아래에 만든다.
//  - 오른쪽 패널: 추천점 개수 [-] n [+]
//  - 왼쪽 아래: 키로 켜고 끄는 기능들의 현재 상태 + 버튼 (버튼과 키는 똑같이 동작)
//  - 도움말 창 (H 키 또는 버튼)
// 한글 표시를 위해 Windows 기본 글꼴(맑은 고딕)을 실행 시 불러온다. 없으면 Arial로 표시된다.
public class CalibrationHUD : MonoBehaviour
{
    [Header("References (비워 두면 자동으로 찾음)")]
    public RectTransform controlPanel;   // 오른쪽 기존 패널
    public VertexClickTest clickTest;
    public DLT_solve dltSolver;

    [Header("Layout")]
    public float countRowY = -190f;      // 오른쪽 패널 안에서 개수 줄의 세로 위치
    public float statusPanelWidth = 260f;
    public int fontSize = 12;

    private const float RowHeight = 20f, RowGap = 3f;
    private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.392f); // 기존 오른쪽 패널과 같은 색
    private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.15f);

    private Font font;
    private Text countText;
    private GameObject helpPanel, statusPanel;
    private readonly List<(Text label, Func<string> text)> liveLabels = new List<(Text, Func<string>)>();
    private Text[] vertexLabels; // 조작 화면에서 선택된 버텍스 옆 번호 (프로젝터 마커 번호와 같음)

    private const string HelpText =
        "<b>사용 순서</b>\n" +
        "1. 오른쪽 목록에서 모델 선택 (프로젝터 화면에 맞게 크기가 자동 조정됨)\n" +
        "2. <b>- / =</b> 추천점 개수 조절 (6~20, 기본 12) → <b>R</b> 추천점을 대응점으로 선택\n" +
        "     (조작 화면에서 <b>Ctrl+클릭</b>으로 버텍스를 직접 고르거나 해제할 수도 있음. 노란 점이 고를 점)\n" +
        "3. 프로젝터 화면에서 각 십자선을 실물의 같은 위치로 드래그 (이때는 텍스처 없이 모델 모양만 투사됨)\n" +
        "     조작 화면의 번호가 그 마커의 버텍스. 지금 움직이는 마커는 노란색\n" +
        "     <b>방향키</b> 마지막으로 누르거나 끈 마커를 1px씩 (Shift: 10px)\n" +
        "     <b>V</b> 정렬 보기: 모델 없이 마커만 투사   <b>[ / ]</b> 십자선 크기 줄이기/키우기\n" +
        "4. 마커를 6개 이상 맞추면, 놓을 때마다 맞춘 마커들로 자동 보정됨 (초록 = 맞춘 마커)\n" +
        "     빨강 = 다른 마커들과 유독 안 맞는 마커 (8개 이상 맞췄을 때) → 그 마커를 다시 확인\n" +
        "     계산이 깨진 결과(좌우 뒤집힘 등)는 적용하지 않고 '보정 보류'로 알림 → 마커 짝 확인\n" +
        "     <b>L</b> 자동 보정 켜기/끄기, <b>F</b> 맞춘 마커로 지금 계산, <b>Backspace</b> 보정 초기화\n" +
        "5. 다 맞추면 <b>X</b>로 텍스처 입히기 (다시 누르면 끔. 모델을 바꾸거나 R을 누르면 다시 꺼짐)\n" +
        "\n" +
        "<b>기타</b>\n" +
        "<b>1</b> 캘리브레이션 모드   <b>2</b> 히트맵   <b>3</b> 추천점 표시   (R을 누르면 1, 3은 자동으로 켜짐)\n" +
        "<b>T</b> 패치(마커 주변 모양) 끔/선/텍스처   <b>P</b> 4D 텍스처 재생/정지\n" +
        "<b>F5</b> saliency 다시 계산   <b>H</b> 이 도움말 열기/닫기\n" +
        "\n" +
        "보정 후 프로젝터 위치·줌·키스톤이나 모델 크기·회전을 바꾸면 다시 보정해야 합니다.\n" +
        "(창을 클릭하면 닫힙니다)";

    void Start()
    {
        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, fontSize);
        if (controlPanel == null) controlPanel = transform.Find("ControlPanel") as RectTransform;
        if (clickTest == null) clickTest = FindObjectOfType<VertexClickTest>();
        if (dltSolver == null) dltSolver = FindObjectOfType<DLT_solve>();

        if (controlPanel != null) BuildCountRow();
        BuildStatusPanel();
        BuildHelpPanel();
    }

    void Update()
    {
        if (!HotkeyGuard.Blocked && Input.GetKeyDown(KeyCode.H)) ToggleHelp();

        if (countText != null && MainController.Instance != null)
            SetIfChanged(countText, MainController.Instance.recommendedVertexCount.ToString());
        foreach (var (label, text) in liveLabels) SetIfChanged(label, text());
    }

    // 카메라가 움직인 뒤에 위치를 잡도록 LateUpdate에서
    void LateUpdate()
    {
        UpdateVertexLabels();
    }

    // ---------------------------------------------------------------- 조작 화면: 선택된 버텍스 번호
    // "3번 마커가 실물의 어디인지"를 조작 화면에서 바로 알 수 있게 한다. 색은 프로젝터 마커와 같음.
    private void UpdateVertexLabels()
    {
        if (clickTest == null || clickTest.clickedObjects == null) return;
        Camera cam = Camera.main;
        if (vertexLabels == null)
        {
            vertexLabels = new Text[clickTest.clickedObjects.Length];
            for (int i = 0; i < vertexLabels.Length; i++)
            {
                Text t = NewText(transform, (i + 1).ToString(), TextAnchor.LowerLeft, Vector2.zero, new Vector2(40f, 24f));
                t.rectTransform.pivot = new Vector2(0f, 0f);
                t.fontStyle = FontStyle.Bold;
                t.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
                t.transform.SetAsFirstSibling(); // 패널들보다 아래에 그림
                t.gameObject.SetActive(false);
                vertexLabels[i] = t;
            }
        }

        var canvasRect = (RectTransform)transform;
        for (int i = 0; i < vertexLabels.Length; i++)
        {
            GameObject sphere = clickTest.clickedObjects[i];
            Vector3 screen = (sphere != null && cam != null) ? cam.WorldToScreenPoint(sphere.transform.position) : Vector3.back;
            bool show = screen.z > 0f;
            if (vertexLabels[i].gameObject.activeSelf != show) vertexLabels[i].gameObject.SetActive(show);
            if (!show) continue;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
            vertexLabels[i].rectTransform.anchoredPosition = local + new Vector2(4f, 4f);
            bool active = clickTest.ActiveSlot == i;
            vertexLabels[i].fontSize = active ? fontSize + 8 : fontSize + 3;
            vertexLabels[i].color = clickTest.IsSuspect(i) ? new Color(1f, 0.25f, 0.25f)
                                  : active ? new Color(1f, 0.9f, 0f)
                                  : clickTest.IsPlaced(i) ? new Color(0.35f, 1f, 0.35f) : Color.white;
        }
    }

    public void ToggleHelp()
    {
        helpPanel.SetActive(!helpPanel.activeSelf);
        statusPanel.SetActive(!helpPanel.activeSelf); // 도움말과 겹치지 않게
    }

    // 같은 글자를 매 프레임 다시 넣으면 UI 레이아웃을 매번 다시 계산하므로, 바뀔 때만 넣는다.
    private static void SetIfChanged(Text label, string value)
    {
        if (label.text != value) label.text = value;
    }

    // ---------------------------------------------------------------- 오른쪽: 추천점 개수
    private void BuildCountRow()
    {
        NewText(controlPanel, "추천점 개수", TextAnchor.MiddleLeft, new Vector2(-30f, countRowY), new Vector2(84f, RowHeight));
        NewButton(controlPanel, "-", new Vector2(20f, countRowY), new Vector2(18f, 18f), () => ChangeCount(-1));
        countText = NewText(controlPanel, "6", TextAnchor.MiddleCenter, new Vector2(42f, countRowY), new Vector2(24f, RowHeight));
        NewButton(controlPanel, "+", new Vector2(64f, countRowY), new Vector2(18f, 18f), () => ChangeCount(+1));
    }

    private static void ChangeCount(int delta)
    {
        MainController main = MainController.Instance;
        if (main != null) main.SetRecommendedVertexCount(main.recommendedVertexCount + delta);
    }

    // ---------------------------------------------------------------- 왼쪽 아래: 기능 상태 + 버튼
    private void BuildStatusPanel()
    {
        var rows = new List<(Func<string> text, UnityAction action)>
        {
            (() => "추천점을 대응점으로 선택 (R)", () => clickTest.SelectRecommendedVertices()),
            (() => $"정렬 보기 (V): {(clickTest.AlignmentView ? "켜짐" : "꺼짐")}", () => clickTest.ToggleAlignmentView()),
            (() => $"패치 (T): {clickTest.PatchLabel}", () => clickTest.TogglePatchMode()),
            (() => $"자동 보정 (L): {(clickTest.liveSolve ? "켜짐" : "꺼짐")}", () => clickTest.ToggleLiveSolve()),
            (() => "보정 계산 (F)", () => dltSolver.PerformDLT()),
            (() => "보정 초기화 (Backspace)", () => dltSolver.ResetCalibration()),
            (() => Animator4D == null || !Animator4D.HasModel ? "텍스처 (X): 모델 없음"
                                                              : $"텍스처 (X): {(Animator4D.TextureVisible ? "입힘" : "끔 (모양만)")}",
             () => { if (Animator4D != null) Animator4D.ToggleTextureVisible(); }),
            (() => Animator4D == null || !Animator4D.HasSequence ? "4D 텍스처: 없음"
                                                                : $"4D 텍스처 (P): {(Animator4D.playing ? "재생 중" : "정지")}",
             () => { if (Animator4D != null) Animator4D.TogglePlaying(); }),
            (() => "도움말 (H)", ToggleHelp),
        };

        // 마지막 상태 줄은 두 줄까지 (보정 보류 이유 + 할 일)
        float height = 8f + RowHeight + (rows.Count + 2) * (RowHeight + RowGap) + 6f;
        RectTransform panel = NewRect("CalibrationStatusPanel", transform, Vector2.zero, Vector2.zero, new Vector2(8f, 8f), new Vector2(statusPanelWidth, height));
        panel.gameObject.AddComponent<Image>().color = PanelColor;
        statusPanel = panel.gameObject;

        float y = -8f - RowHeight / 2f;
        Text title = NewText(panel, "<b>캘리브레이션</b>", TextAnchor.MiddleCenter, Vector2.zero, new Vector2(statusPanelWidth - 12f, RowHeight), topAnchored: true, y: y);
        title.fontSize = fontSize + 2;

        foreach (var (text, action) in rows)
        {
            y -= RowHeight + RowGap;
            Button button = NewButton(panel, text(), Vector2.zero, new Vector2(statusPanelWidth - 12f, RowHeight), action, topAnchored: true, y: y);
            Text label = button.GetComponentInChildren<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.offsetMin = new Vector2(6f, 0f);
            liveLabels.Add((label, text));
        }

        y -= RowHeight + RowGap + (RowHeight + RowGap) / 2f;
        Text status = NewText(panel, "", TextAnchor.MiddleLeft, Vector2.zero, new Vector2(statusPanelWidth - 18f, RowHeight * 2f + RowGap), topAnchored: true, y: y);
        // 지금 할 일 안내 (리허설에서 모델을 바꾼 뒤 무엇을 다시 켜야 하는지 몰라 막혔음)
        liveLabels.Add((status, () =>
        {
            MainController main = MainController.Instance;
            if (main == null || main.currentCalibrator == null) return "<b>다음:</b> 오른쪽 목록에서 모델 선택";
            if (main.IsSaliencyPending)
                return $"saliency: {main.CurrentSaliencyJob.Stage}... {main.CurrentSaliencyJob.ElapsedSeconds:F0}초";
            int selected = clickTest.SelectedCount(), placed = clickTest.PlacedCount();
            if (selected == 0) return "<b>다음:</b> R (추천점으로 마커 만들기)";
            if (selected < 6) return $"<b>다음:</b> 버텍스를 6개 이상 선택 (R 또는 Ctrl+클릭, 지금 {selected}개)";
            if (placed < 6) return $"<b>다음:</b> 프로젝터에서 마커 맞추기 ({placed}/6)";
            // 마지막 계산이 실제 프로젝터로 보기 어려워 적용되지 않았으면 이유와 할 일
            if (dltSolver != null && dltSolver.LastRejectReason != null)
                return $"<b>보정 보류:</b> {dltSolver.LastRejectReason}\n번호를 다른 곳에 맞춘 마커가 있는지 확인 ({placed}/{selected})";
            if (dltSolver != null && dltSolver.SuspectSlot >= 0)
                return $"<b>확인:</b> {dltSolver.SuspectSlot + 1}번 마커(빨간색)가 다른 마커와 안 맞음\n맞춘 마커 {placed}/{selected}개";
            if (placed >= selected && Animator4D != null && Animator4D.HasModel && !Animator4D.TextureVisible)
                return $"<b>다음:</b> 다 맞췄으면 <b>X</b>로 텍스처 입히기 ({placed}/{selected})";
            if (!clickTest.liveSolve) return $"맞춘 마커 {placed}/{selected}개 · <b>F</b>로 보정";
            return $"자동 보정 중 · 맞춘 마커 {placed}/{selected}개";
        }));
    }

    private static TextureSequenceAnimator Animator4D => TextureSequenceAnimator.Instance;

    // ---------------------------------------------------------------- 도움말 창
    private void BuildHelpPanel()
    {
        RectTransform panel = NewRect("HelpPanel", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 460f));
        panel.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.96f);
        panel.gameObject.AddComponent<Button>().onClick.AddListener(ToggleHelp); // 아무 곳이나 클릭하면 닫힘

        Text text = NewText(panel, HelpText, TextAnchor.UpperLeft, Vector2.zero, Vector2.zero);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(16f, 12f);
        text.rectTransform.offsetMax = new Vector2(-16f, -12f);
        text.fontSize = fontSize + 1;
        text.lineSpacing = 1.15f;

        helpPanel = panel.gameObject;
        helpPanel.SetActive(false);
    }

    // ---------------------------------------------------------------- UI 생성 도우미
    private RectTransform NewRect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    // topAnchored = 부모 위쪽 가운데 기준으로 y만큼 내려서 배치 (세로로 줄을 쌓을 때)
    private Text NewText(Transform parent, string content, TextAnchor align, Vector2 pos, Vector2 size, bool topAnchored = false, float y = 0f)
    {
        RectTransform rt = topAnchored
            ? NewRect("Text", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, y), size)
            : NewRect("Text", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        var text = rt.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = align;
        text.supportRichText = true;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.text = content;
        text.raycastTarget = false;
        return text;
    }

    private Button NewButton(Transform parent, string label, Vector2 pos, Vector2 size, UnityAction onClick, bool topAnchored = false, float y = 0f)
    {
        RectTransform rt = topAnchored
            ? NewRect("Button", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, y), size)
            : NewRect("Button", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        rt.gameObject.AddComponent<Image>().color = ButtonColor;
        var button = rt.gameObject.AddComponent<Button>();
        button.onClick.AddListener(onClick);

        Text text = NewText(rt, label, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.sizeDelta = Vector2.zero;
        return button;
    }
}
