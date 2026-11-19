using Game.Core.Viewing;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 경기 화면 HUD 상단 바·보기 패널·일시정지 창을 배치·스타일링하고 MatchHudUIElement·ViewingControlUIElement·GameUIManager에 잇는다(11-19).
// 값은 전부 UiTokens(색·반경·글자 3단계·간격·누름 영역). 다시 돌려도 같은 결과: 이름으로 찾아 고쳐 쓰고, 없을 때만 만든다.
// InGameScene이 안 열려 있으면 더해서 열고 저장한 뒤 닫는다. 씬은 손으로 고치지 않고 이 메뉴로만 바꾼다.
//
// 상단 바(FM26 Touch 경기 화면, 레퍼런스/FM26-UI/측정.md): 가운데 둥근 칩 하나에 시계 + 점수 칸(이 칸만 밝은 바탕).
// 오른쪽에 톱니·일시정지 칩(일시정지만 강조색). 버튼은 누름 영역 TouchMin(48dp) 안에 보이는 칩을 가운데 둔다. 칩 사이 보이는 간격 = 24(8dp).
// 보기 패널: 톱니 아래 오른쪽 맞춤 팝오버. 제목 "보기 설정" 아래 2열: 왼쪽 세로 라디오 3줄(FM26 Preferences 순서, 줄마다 48dp),
// 오른쪽 "배속" + 슬라이더(얇은 트랙·둥근 손잡이·눈금 ×1~×4)를 라디오 높이 가운데에. 1열로 쌓으면 화면 높이의 약 50%를 가려 2열로 바꿨다(11-19).
// 일시정지 창(PausePanel): 딤 + 가운데 카드(제목 + 버튼 3개, 버튼 48dp·간격 24, 첫 버튼만 강조색). 문구·동작은 그대로(10-29 조사 모달 규칙).
// 기존 참조(scoreText·clockText·pauseButton·captionText·viewingButton, 일시정지 창 버튼 3개)는 오브젝트를 옮기기만 하고 그대로 둔다
public static class MatchHudBuilder
{
    private const string ScenePath = "Assets/Scenes/InGameScene.unity";
    private const string FontPath = "Assets/Font/Kostar SDF 2.asset";
    private const string GearIconPath = "Assets/Resources/Sprites/UI/Icon_Settings.png";   // 템플릿 UI 스프라이트의 톱니
    private const string PanelName = "ViewingControl";   // UIElementEnums 이름과 같게(GameUIManagerEditor가 이름으로 enum을 만든다)
    private const string GearName = "ViewingButton";
    private const string PauseName = "PauseButton";
    private const string ChipName = "Chip";
    private const string PanelTitle = "보기 설정";

    // 지난 판(11-19 첫 판·둘째 판)에 있던 패널 자식. 있으면 지운다
    private static readonly string[] LegacyPanelChildren = { "ModeControl", "ModeList", "SpeedRow" };

    private const float TopInset = 8f;          // SafeAreaPanel 위에서 상단 바까지(기존 HUD 값)
    private const float SideInset = 16f;        // 화면 오른쪽 끝에서 보이는 칩·패널까지
    private const float ButtonChipWidth = 88f;  // 보이는 버튼 칩 폭. 누름 영역 112 두 개를 붙이면 보이는 간격이 112 − 88 = 24(8dp)
    private const float ClockWidth = 224f;      // 시계 칸 고정 폭("후반 45+3:12"). 숫자가 바뀔 때 칩 폭이 흔들리지 않게
    private const float ScoreBoxInset = 4f;     // 칩 안에서 점수 칸까지(FM26처럼 칸이 칩 안쪽에 붙는다)

    private const float ColumnWidth = 296f;     // 2열 중 한 열. 왼쪽 = 라디오, 오른쪽 = 배속. 패널 폭 = 2열
    private const float ColumnPadding = 24f;    // 오른쪽 열 좌우 여백. 양 끝 눈금(×1·×4) 글자가 가장자리에 붙지 않게
    private const float TitleHeight = 56f;
    private const float SpeedLabelHeight = 56f;
    private const string SpeedLabel = "배속";
    private const float RingSize = 40f;
    private const float DotSize = 20f;
    private const float DividerHeight = 2f;
    private const float TrackHeight = 8f;
    private const float HandleSize = 40f;
    private const float TrackCenterFromTop = 36f;   // 배속 줄(112) 위쪽에 트랙, 아래쪽에 눈금
    private const float TickCenterFromBottom = 28f;
    private const float TickWidth = 80f;

    private const float PauseCardWidth = 640f;
    private const float PauseGap = 24f;          // 버튼 사이 8dp(10-29 조사: 간격 24px)
    private static readonly string[] PauseButtons = { "ResumeButton", "SettingsButton", "HomeButton" };   // 위에서부터. 첫 버튼(계속하기)만 강조색

    [MenuItem("Tools/UI/경기 HUD 배치")]
    public static void Build()
    {
        UiSpriteGenerator.Generate();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        }
        try
        {
            MatchHudUIElement hud = FindInScene<MatchHudUIElement>(scene);
            GameUIManager uiManager = FindInScene<GameUIManager>(scene);
            if (hud == null || uiManager == null)
            {
                Debug.LogError("[MatchHudBuilder] InGameScene에 MatchHud나 GameUIManager가 없다");
                return;
            }

            var style = new Style();
            BuildTopBar(hud, style);
            ViewingControlUIElement panel = BuildPanel(hud.transform, style);
            Register(uiManager, panel);
            BuildPausePanel(scene, style);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[MatchHudBuilder] 경기 HUD와 보기 패널을 배치했다");
        }
        finally
        {
            if (openedHere)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    // 빌더가 쓰는 에셋 묶음(스프라이트·폰트). 매번 같은 경로에서 읽는다
    private sealed class Style
    {
        public readonly Sprite Rounded = AssetDatabase.LoadAssetAtPath<Sprite>(UiSpriteGenerator.RoundedPath);
        public readonly Sprite Ring = AssetDatabase.LoadAssetAtPath<Sprite>(UiSpriteGenerator.RingPath);
        public readonly Sprite Circle = AssetDatabase.LoadAssetAtPath<Sprite>(UiSpriteGenerator.CirclePath);
        public readonly Sprite Gear = AssetDatabase.LoadAssetAtPath<Sprite>(GearIconPath);
        public readonly TMP_FontAsset Font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            T found = roots[i].GetComponentInChildren<T>(true);
            if (found != null) { return found; }
        }
        return null;
    }

    #region 상단 바
    private static void BuildTopBar(MatchHudUIElement hud, Style style)
    {
        var bar = (RectTransform)hud.transform;
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = Vector2.one;
        bar.pivot = new Vector2(0.5f, 1f);
        bar.anchoredPosition = new Vector2(0f, -TopInset);
        bar.sizeDelta = new Vector2(0f, UiTokens.TouchMin);

        var serialized = new SerializedObject(hud);
        var clock = (TMP_Text)serialized.FindProperty("clockText").objectReferenceValue;
        var score = (TMP_Text)serialized.FindProperty("scoreText").objectReferenceValue;
        var caption = (TMP_Text)serialized.FindProperty("captionText").objectReferenceValue;
        var pause = (Button)serialized.FindProperty("pauseButton").objectReferenceValue;

        BuildScoreChip(hud.transform, clock, score, style);
        Transform pauseLabel = FindDeep(pause.transform, "Label");
        StyleButton(pause, UiTokens.Accent, pauseLabel, 0, style);
        Button gear = EnsureGear(hud.transform);
        Transform gearIcon = FindDeep(gear.transform, "Icon");
        StyleButton(gear, UiTokens.Chip, gearIcon, 1, style);
        StyleGearIcon(gearIcon, style);
        StyleCaption(caption, style);

        serialized.FindProperty("viewingButton").objectReferenceValue = gear;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // 시계 + 점수 칸을 칩 하나로 묶는다. 칩 폭은 글자에 맞춰 늘고 준다(ContentSizeFitter)
    private static void BuildScoreChip(Transform hud, TMP_Text clock, TMP_Text score, Style style)
    {
        GameObject chip = EnsureChild(hud, "ScoreChip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        chip.transform.SetSiblingIndex(0);
        var rect = (RectTransform)chip.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, UiTokens.ChipHeight);
        SetSliced(chip.GetComponent<Image>(), UiTokens.Chip, style);
        chip.GetComponent<Image>().raycastTarget = false;

        var layout = chip.GetComponent<HorizontalLayoutGroup>();
        int inset = Mathf.RoundToInt(ScoreBoxInset);
        layout.padding = new RectOffset(Mathf.RoundToInt(UiTokens.Padding), inset, inset, inset);
        layout.spacing = UiTokens.Padding;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        var fitter = chip.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        clock.transform.SetParent(chip.transform, false);
        clock.transform.SetSiblingIndex(0);
        StyleText(clock, UiTokens.BodySize, UiTokens.Text, TextAlignmentOptions.Center, style);
        LayoutElement clockLayout = EnsureComponent<LayoutElement>(clock.gameObject);
        clockLayout.minWidth = ClockWidth;
        clockLayout.preferredWidth = ClockWidth;

        GameObject box = EnsureChild(chip.transform, "ScoreBox", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HorizontalLayoutGroup));
        box.transform.SetSiblingIndex(1);
        SetSliced(box.GetComponent<Image>(), UiTokens.ScoreBox, style);
        box.GetComponent<Image>().raycastTarget = false;
        var boxLayout = box.GetComponent<HorizontalLayoutGroup>();
        int pad = Mathf.RoundToInt(UiTokens.Padding);
        boxLayout.padding = new RectOffset(pad, pad, 0, 0);
        boxLayout.childAlignment = TextAnchor.MiddleCenter;
        boxLayout.childControlWidth = true;
        boxLayout.childControlHeight = true;
        boxLayout.childForceExpandWidth = false;
        boxLayout.childForceExpandHeight = true;

        score.transform.SetParent(box.transform, false);
        StyleText(score, UiTokens.BodySize, UiTokens.ScoreText, TextAlignmentOptions.Center, style);

        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    // 톱니는 지난 판 빌더가 만든 것을 고쳐 쓰고, 없으면 만든다
    private static Button EnsureGear(Transform hud)
    {
        GameObject go = EnsureChild(hud, GearName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        Transform icon = FindDeep(go.transform, "Icon");
        if (icon == null)
        {
            EnsureChild(go.transform, "Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        }
        return go.GetComponent<Button>();
    }

    // 버튼 하나: 루트 = 투명한 누름 영역(TouchMin), 자식 Chip = 보이는 칩(둥근 사각), 그 안 가운데 아이콘·글자.
    // slotFromRight 0 = 일시정지(맨 오른쪽), 1 = 톱니(그 왼쪽). 보이는 칩 오른쪽 끝이 SideInset에 맞는다
    private static void StyleButton(Button button, Color chipColor, Transform content, int slotFromRight, Style style)
    {
        var rect = (RectTransform)button.transform;
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(UiTokens.TouchMin, UiTokens.TouchMin);
        float hitOverhang = (UiTokens.TouchMin - ButtonChipWidth) * 0.5f;
        rect.anchoredPosition = new Vector2(-SideInset + hitOverhang - slotFromRight * UiTokens.TouchMin, 0f);
        var hit = button.GetComponent<Image>();
        hit.sprite = null;
        hit.color = Color.clear;   // 누름만 받는다. 보이는 것은 Chip
        hit.raycastTarget = true;
        EditorUtility.SetDirty(hit);

        GameObject chip = EnsureChild(button.transform, ChipName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var chipRect = (RectTransform)chip.transform;
        chipRect.anchorMin = new Vector2(0.5f, 0.5f);
        chipRect.anchorMax = new Vector2(0.5f, 0.5f);
        chipRect.pivot = new Vector2(0.5f, 0.5f);
        chipRect.anchoredPosition = Vector2.zero;
        chipRect.sizeDelta = new Vector2(ButtonChipWidth, UiTokens.ChipHeight);
        var chipImage = chip.GetComponent<Image>();
        SetSliced(chipImage, chipColor, style);
        chipImage.raycastTarget = false;
        button.targetGraphic = chipImage;
        EditorUtility.SetDirty(button);

        content.SetParent(chip.transform, false);
        var text = content.GetComponent<TMP_Text>();
        if (text == null) { return; }
        var contentRect = (RectTransform)content;
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.pivot = new Vector2(0.5f, 0.5f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;
        StyleText(text, UiTokens.BodySize, UiTokens.Text, TextAlignmentOptions.Center, style);
        text.raycastTarget = false;
    }

    private static void StyleGearIcon(Transform icon, Style style)
    {
        var rect = (RectTransform)icon;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(UiTokens.IconSize, UiTokens.IconSize);
        var image = icon.GetComponent<Image>();
        image.sprite = style.Gear;
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
        EditorUtility.SetDirty(image);
    }

    // 자막은 상단 바 바로 아래 가운데. 색은 지금 값(노랑)을 둔다
    private static void StyleCaption(TMP_Text caption, Style style)
    {
        var rect = (RectTransform)caption.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -(UiTokens.TouchMin + UiTokens.Space));
        rect.sizeDelta = new Vector2(0f, UiTokens.TouchMin * 0.5f + UiTokens.Space);
        StyleText(caption, UiTokens.TitleSize, caption.color, TextAlignmentOptions.Center, style);
    }
    #endregion

    #region 보기 패널
    private static ViewingControlUIElement BuildPanel(Transform hud, Style style)
    {
        GameObject go = EnsureChild(hud.parent, PanelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ViewingControlUIElement));
        go.transform.SetSiblingIndex(hud.GetSiblingIndex() + 1);   // HUD 바로 뒤: 결과·일시정지 창이 덮는다
        go.SetActive(false);   // 런타임엔 Initialize가 끄고 톱니가 켠다

        for (int i = 0; i < LegacyPanelChildren.Length; i++)
        {
            Transform legacy = go.transform.Find(LegacyPanelChildren[i]);
            if (legacy != null) { Object.DestroyImmediate(legacy.gameObject); }
        }

        int rows = ViewingOptions.ModeRows.Length;
        float modesTop = UiTokens.Space + TitleHeight;
        float modesHeight = rows * UiTokens.TouchMin;
        float speedGroup = SpeedLabelHeight + UiTokens.TouchMin;            // 라벨 + 슬라이더 줄(눈금 포함)
        float speedLabelTop = modesTop + (modesHeight - speedGroup) * 0.5f;   // 라디오 높이 가운데에 세운다
        float height = modesTop + modesHeight + UiTokens.Space;

        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-SideInset, -(TopInset + UiTokens.TouchMin + UiTokens.Space));   // 톱니·일시정지 누름 영역 바로 아래
        rect.sizeDelta = new Vector2(ColumnWidth * 2f, height);
        SetSliced(go.GetComponent<Image>(), UiTokens.Panel, style);

        BuildTitle(go.transform, style);
        Transform modes = BuildModes(go.transform, modesTop, style);
        BuildColumnDivider(go.transform, modesTop, modesHeight);
        BuildSpeedLabel(go.transform, speedLabelTop, style);
        Slider slider = BuildSpeed(go.transform, speedLabelTop + SpeedLabelHeight, style);
        Wire(go.GetComponent<ViewingControlUIElement>(), modes, slider);
        return go.GetComponent<ViewingControlUIElement>();
    }

    private static void BuildTitle(Transform panel, Style style)
    {
        GameObject go = EnsureChild(panel, "Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -UiTokens.Space);
        rect.sizeDelta = new Vector2(-2f * UiTokens.Padding, TitleHeight);
        var text = go.GetComponent<TMP_Text>();
        StyleText(text, UiTokens.TitleSize, UiTokens.Text, TextAlignmentOptions.MidlineLeft, style);
        text.text = PanelTitle;
        text.raycastTarget = false;
    }

    // 두 열 사이 세로 구분선
    private static void BuildColumnDivider(Transform panel, float top, float height)
    {
        GameObject go = EnsureChild(panel, "ColumnDivider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        PlaceColumnBand((RectTransform)go.transform, top, height, ColumnWidth - DividerHeight * 0.5f, DividerHeight);
        var image = go.GetComponent<Image>();
        image.sprite = null;
        image.color = UiTokens.Divider;
        image.raycastTarget = false;
        EditorUtility.SetDirty(image);
    }

    private static void BuildSpeedLabel(Transform panel, float top, Style style)
    {
        GameObject go = EnsureChild(panel, "SpeedLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        PlaceColumnBand((RectTransform)go.transform, top, SpeedLabelHeight, ColumnWidth + ColumnPadding, ColumnWidth - 2f * ColumnPadding);
        var text = go.GetComponent<TMP_Text>();
        StyleText(text, UiTokens.BodySize, UiTokens.Text, TextAlignmentOptions.MidlineLeft, style);
        text.text = SpeedLabel;
        text.raycastTarget = false;
    }

    // 세로 라디오: 위에서부터 ViewingOptions.ModeRows 순서. 한 ToggleGroup 아래 줄 3개(줄마다 48dp)
    private static Transform BuildModes(Transform panel, float top, Style style)
    {
        GameObject list = EnsureChild(panel, "Modes", typeof(RectTransform), typeof(ToggleGroup));
        int rows = ViewingOptions.ModeRows.Length;
        PlaceColumnBand((RectTransform)list.transform, top, rows * UiTokens.TouchMin, 0f, ColumnWidth);
        var group = list.GetComponent<ToggleGroup>();
        group.allowSwitchOff = false;
        for (int row = 0; row < rows; row++)
        {
            BuildRadioRow(list.transform, row, rows, group, style);
        }
        return list.transform;
    }

    private static void BuildRadioRow(Transform list, int row, int rows, ToggleGroup group, Style style)
    {
        GameObject go = EnsureChild(list, $"Row{row}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Toggle));
        PlaceTopBand((RectTransform)go.transform, row * UiTokens.TouchMin, UiTokens.TouchMin, 0f);
        var hit = go.GetComponent<Image>();
        hit.sprite = null;
        hit.color = Color.clear;   // 줄 전체가 누름 영역
        hit.raycastTarget = true;
        EditorUtility.SetDirty(hit);

        GameObject ring = EnsureChild(go.transform, "Ring", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var ringRect = (RectTransform)ring.transform;
        ringRect.anchorMin = new Vector2(0f, 0.5f);
        ringRect.anchorMax = new Vector2(0f, 0.5f);
        ringRect.pivot = new Vector2(0.5f, 0.5f);
        ringRect.sizeDelta = new Vector2(RingSize, RingSize);
        ringRect.anchoredPosition = new Vector2(UiTokens.Padding + RingSize * 0.5f, 0f);
        SetSimple(ring.GetComponent<Image>(), style.Ring, UiTokens.Line);   // 런타임엔 고른 줄만 강조색

        GameObject dot = EnsureChild(ring.transform, "Dot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var dotRect = (RectTransform)dot.transform;
        dotRect.anchorMin = new Vector2(0.5f, 0.5f);
        dotRect.anchorMax = new Vector2(0.5f, 0.5f);
        dotRect.pivot = new Vector2(0.5f, 0.5f);
        dotRect.anchoredPosition = Vector2.zero;
        dotRect.sizeDelta = new Vector2(DotSize, DotSize);
        SetSimple(dot.GetComponent<Image>(), style.Circle, UiTokens.Accent);

        GameObject labelGo = EnsureChild(go.transform, "Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.offsetMin = new Vector2(UiTokens.Padding * 2f + RingSize, 0f);
        labelRect.offsetMax = new Vector2(-UiTokens.Padding, 0f);
        var label = labelGo.GetComponent<TMP_Text>();
        StyleText(label, UiTokens.BodySize, UiTokens.SubText, TextAlignmentOptions.MidlineLeft, style);
        label.text = string.Empty;   // 글자는 ViewingControlUIElement.Initialize가 넣는다
        label.raycastTarget = false;

        BuildDivider(go.transform, row < rows - 1);

        var toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = hit;
        toggle.graphic = dot.GetComponent<Image>();
        toggle.transition = Selectable.Transition.None;
        toggle.toggleTransition = Toggle.ToggleTransition.None;
        toggle.group = group;
        toggle.isOn = row == ViewingOptions.RowOf(ViewingOptions.DefaultMode);   // 런타임엔 Show가 지금 모드로 다시 칠한다
        EditorUtility.SetDirty(toggle);
    }

    // 줄 아래 얇은 구분선. 마지막 줄에는 없다
    private static void BuildDivider(Transform row, bool visible)
    {
        GameObject go = EnsureChild(row, "Divider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.SetActive(visible);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(-2f * UiTokens.Padding, DividerHeight);
        var image = go.GetComponent<Image>();
        image.sprite = null;
        image.color = UiTokens.Divider;
        image.raycastTarget = false;
        EditorUtility.SetDirty(image);
    }

    // 배속: 줄 하나(48dp) 위쪽에 얇은 트랙·둥근 손잡이, 아래쪽에 눈금. 줄 전체가 누름 영역이다
    private static Slider BuildSpeed(Transform panel, float top, Style style)
    {
        GameObject go = EnsureChild(panel, "Speed", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Slider));
        PlaceColumnBand((RectTransform)go.transform, top, UiTokens.TouchMin, ColumnWidth + ColumnPadding, ColumnWidth - 2f * ColumnPadding);
        var hit = go.GetComponent<Image>();
        hit.sprite = null;
        hit.color = Color.clear;
        hit.raycastTarget = true;
        EditorUtility.SetDirty(hit);

        float half = HandleSize * 0.5f;   // 트랙 양 끝 = 손잡이 중심이 닿는 곳
        GameObject track = EnsureChild(go.transform, "Track", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        PlaceTrackBand((RectTransform)track.transform, half, TrackHeight);
        SetSliced(track.GetComponent<Image>(), UiTokens.Line, style);
        track.GetComponent<Image>().raycastTarget = false;

        GameObject fillArea = EnsureChild(go.transform, "Fill Area", typeof(RectTransform));
        PlaceTrackBand((RectTransform)fillArea.transform, half, TrackHeight);
        GameObject fill = EnsureChild(fillArea.transform, "Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var fillRect = (RectTransform)fill.transform;
        fillRect.sizeDelta = Vector2.zero;
        fillRect.anchoredPosition = Vector2.zero;
        SetSliced(fill.GetComponent<Image>(), UiTokens.Accent, style);
        fill.GetComponent<Image>().raycastTarget = false;

        GameObject handleArea = EnsureChild(go.transform, "Handle Slide Area", typeof(RectTransform));
        PlaceTrackBand((RectTransform)handleArea.transform, half, HandleSize);
        GameObject handle = EnsureChild(handleArea.transform, "Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var handleRect = (RectTransform)handle.transform;
        handleRect.sizeDelta = new Vector2(HandleSize, 0f);
        handleRect.anchoredPosition = Vector2.zero;
        SetSimple(handle.GetComponent<Image>(), style.Circle, UiTokens.Accent);
        handle.GetComponent<Image>().raycastTarget = false;

        BuildTicks(go.transform, half, style);

        var slider = go.GetComponent<Slider>();
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.transition = Selectable.Transition.None;
        slider.direction = Slider.Direction.LeftToRight;
        slider.wholeNumbers = true;
        slider.minValue = 0f;
        slider.maxValue = ViewingOptions.Speeds.Length - 1;
        slider.SetValueWithoutNotify(0f);
        EditorUtility.SetDirty(slider);
        return slider;
    }

    // 눈금 ×1~×4: 손잡이 중심이 서는 자리(트랙 양 끝 사이 등분) 아래에 하나씩
    private static void BuildTicks(Transform slider, float half, Style style)
    {
        GameObject ticks = EnsureChild(slider, "Ticks", typeof(RectTransform));
        var rect = (RectTransform)ticks.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, TickCenterFromBottom);
        rect.sizeDelta = new Vector2(-2f * half, UiTokens.CaptionSize + UiTokens.Space);

        int count = ViewingOptions.Speeds.Length;
        for (int i = 0; i < count; i++)
        {
            GameObject go = EnsureChild(ticks.transform, $"Tick{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var tickRect = (RectTransform)go.transform;
            float x = count > 1 ? (float)i / (count - 1) : 0.5f;
            tickRect.anchorMin = new Vector2(x, 0f);
            tickRect.anchorMax = new Vector2(x, 1f);
            tickRect.pivot = new Vector2(0.5f, 0.5f);
            tickRect.anchoredPosition = Vector2.zero;
            tickRect.sizeDelta = new Vector2(TickWidth, 0f);
            var text = go.GetComponent<TMP_Text>();
            StyleText(text, UiTokens.CaptionSize, UiTokens.SubText, TextAlignmentOptions.Center, style);
            text.text = $"×{ViewingOptions.Speeds[i]}";
            text.raycastTarget = false;
        }
    }

    private static void Wire(ViewingControlUIElement panel, Transform modes, Slider slider)
    {
        var serialized = new SerializedObject(panel);
        int rows = ViewingOptions.ModeRows.Length;
        SerializedProperty toggles = serialized.FindProperty("modeToggles");
        SerializedProperty labels = serialized.FindProperty("modeLabels");
        SerializedProperty rings = serialized.FindProperty("modeRings");
        toggles.arraySize = rows;
        labels.arraySize = rows;
        rings.arraySize = rows;
        for (int row = 0; row < rows; row++)
        {
            Transform rowTransform = modes.Find($"Row{row}");
            toggles.GetArrayElementAtIndex(row).objectReferenceValue = rowTransform.GetComponent<Toggle>();
            labels.GetArrayElementAtIndex(row).objectReferenceValue = rowTransform.Find("Label").GetComponent<TMP_Text>();
            rings.GetArrayElementAtIndex(row).objectReferenceValue = rowTransform.Find("Ring").GetComponent<Image>();
        }
        serialized.FindProperty("speedSlider").objectReferenceValue = slider;
        Transform ticks = slider.transform.Find("Ticks");
        SerializedProperty tickTexts = serialized.FindProperty("speedTicks");
        tickTexts.arraySize = ticks.childCount;
        for (int i = 0; i < ticks.childCount; i++)
        {
            tickTexts.GetArrayElementAtIndex(i).objectReferenceValue = ticks.Find($"Tick{i}").GetComponent<TMP_Text>();
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // uiElements 칸 번호 = UIElementEnums 값. 모자라면 늘려서 그 칸에 넣는다
    private static void Register(GameUIManager uiManager, ViewingControlUIElement panel)
    {
        int index = (int)UIElementEnums.ViewingControl;
        var serialized = new SerializedObject(uiManager);
        SerializedProperty list = serialized.FindProperty("uiElements");
        if (list.arraySize <= index)
        {
            list.arraySize = index + 1;
        }
        list.GetArrayElementAtIndex(index).objectReferenceValue = panel;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
    #endregion

    #region 일시정지 창
    private static void BuildPausePanel(Scene scene, Style style)
    {
        PausePanelUIElement pause = FindInScene<PausePanelUIElement>(scene);
        if (pause == null)
        {
            Debug.LogError("[MatchHudBuilder] InGameScene에 PausePanel이 없다");
            return;
        }
        // 루트는 SafeArea 크기라 가장자리를 못 덮었다. 루트는 투명한 누름 막기만 하고, 딤은 자식 Dim이 화면 전체로 넓혀 그린다(FullScreenBackdrop)
        var root = pause.GetComponent<Image>();
        root.sprite = null;
        root.color = Color.clear;
        root.raycastTarget = true;
        EditorUtility.SetDirty(root);
        GameObject dimGo = EnsureChild(pause.transform, "Dim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(FullScreenBackdrop));
        dimGo.transform.SetSiblingIndex(0);   // 카드 뒤
        var dimRect = (RectTransform)dimGo.transform;
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.offsetMin = Vector2.zero;
        dimRect.offsetMax = Vector2.zero;   // 에디터에선 SafeArea 크기. 런타임에 FullScreenBackdrop이 화면 전체로 넓힌다
        var dim = dimGo.GetComponent<Image>();
        dim.sprite = null;
        dim.color = UiTokens.Dim;
        dim.raycastTarget = true;
        EditorUtility.SetDirty(dim);

        Transform card = pause.transform.Find("Card");
        int count = PauseButtons.Length;
        float titleTop = UiTokens.Padding + UiTokens.Space;
        float buttonsTop = titleTop + UiTokens.TouchMin;
        float height = buttonsTop + count * UiTokens.TouchMin + (count - 1) * PauseGap + PauseGap;
        var cardRect = (RectTransform)card;
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = Vector2.zero;
        cardRect.sizeDelta = new Vector2(PauseCardWidth, height);
        SetSliced(card.GetComponent<Image>(), UiTokens.Panel, style);

        var title = card.Find("TitleText").GetComponent<TMP_Text>();
        PlaceTopBand((RectTransform)title.transform, titleTop, UiTokens.TouchMin, PauseGap);
        StyleText(title, UiTokens.TitleSize, UiTokens.Text, TextAlignmentOptions.Center, style);

        for (int i = 0; i < count; i++)
        {
            Transform button = card.Find(PauseButtons[i]);
            float top = buttonsTop + i * (UiTokens.TouchMin + PauseGap);
            PlaceTopBand((RectTransform)button, top, UiTokens.TouchMin, PauseGap);
            SetSliced(button.GetComponent<Image>(), i == 0 ? UiTokens.Accent : UiTokens.Surface, style);
            StyleText(button.GetComponentInChildren<TMP_Text>(true), UiTokens.BodySize, UiTokens.Text, TextAlignmentOptions.Center, style);
        }
    }
    #endregion

    #region 공통
    // 부모 위쪽에서 top만큼 내려온 가로 띠(높이 height, 좌우 여백 side)
    private static void PlaceTopBand(RectTransform rect, float top, float height, float side)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -top);
        rect.sizeDelta = new Vector2(-2f * side, height);
    }

    // 부모 왼쪽 위에서 (left, top)만큼 떨어진 열 조각(폭 width, 높이 height)
    private static void PlaceColumnBand(RectTransform rect, float top, float height, float left, float width)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    // 배속 줄 위쪽의 트랙 띠. 좌우를 손잡이 반폭만큼 들여 트랙 끝 = 손잡이 중심
    private static void PlaceTrackBand(RectTransform rect, float sideInset, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, -TrackCenterFromTop);
        rect.sizeDelta = new Vector2(-2f * sideInset, height);
    }

    private static void SetSliced(Image image, Color color, Style style)
    {
        image.sprite = style.Rounded;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f;
        image.color = color;
        EditorUtility.SetDirty(image);   // 이미 있던 Image는 SetDirty 없이 바꾸면 씬 저장에 안 남는 경우가 있었다(일시정지 창)
    }

    private static void SetSimple(Image image, Sprite sprite, Color color)
    {
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = color;
        EditorUtility.SetDirty(image);
    }

    private static void StyleText(TMP_Text text, float size, Color color, TextAlignmentOptions alignment, Style style)
    {
        text.font = style.Font;
        text.fontSharedMaterial = style.Font.material;
        text.fontSize = size;
        text.enableAutoSizing = false;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        EditorUtility.SetDirty(text);
    }

    // 이름으로 찾고 없으면 만든다. 다시 돌려도 같은 오브젝트를 고쳐 쓴다
    private static GameObject EnsureChild(Transform parent, string name, params System.Type[] components)
    {
        Transform existing = parent.Find(name);
        if (existing != null) { return existing.gameObject; }
        var go = new GameObject(name, components);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return go;
    }

    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        T found = go.GetComponent<T>();
        if (found != null) { return found; }
        return go.AddComponent<T>();
    }

    // 손자 이하까지 이름으로 찾는다(지난 판에서 자리가 옮겨졌을 수 있다)
    private static Transform FindDeep(Transform root, string name)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != root && all[i].name == name) { return all[i]; }
        }
        return null;
    }
    #endregion
}
