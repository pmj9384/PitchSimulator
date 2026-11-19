using Game.Core.Viewing;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 인게임 HUD 우측 상단에 톱니 버튼과 보기 패널(모드 세로 라디오 목록 + 배속 슬라이더)을 놓고 MatchHudUIElement·ViewingControlUIElement·GameUIManager에 잇는다(11-19).
// 다시 돌려도 같은 결과(이미 있으면 자리·글자 크기·연결만 다시 맞춘다). InGameScene이 안 열려 있으면 더해서 열고 저장한 뒤 닫는다.
// 자리: 톱니는 일시정지 버튼(HUD 오른쪽 위 84×60) 바로 왼쪽에 같은 크기로(FM24 경기 화면 우상단 톱니). 패널은 두 버튼 아래, 오른쪽 맞춤.
// 스코어·시계·자막은 가운데 정렬이라 오른쪽 끝과 겹치지 않는다.
// 모드 목록은 FM26 Preferences > Match Day의 하이라이트 라디오처럼 세로(레퍼런스/FM26-UI/측정.md). 키트에 라디오가 없어 여기서 만든다(원 = Unity 기본 Knob 스프라이트).
// 줄 높이는 터치 타깃 48dp(실험/2026-10-29-설정창-기본UI-표준-조사.md: 1080 기준 약 120~130)
public static class ViewingControlBuilder
{
    private const string ScenePath = "Assets/Scenes/InGameScene.unity";
    private const string KnobPath = "UI/Skin/Knob.psd";   // Unity 기본 리소스의 원
    private const string SliderRowPath = "Assets/Prefabs/UI/Kit/SliderRow.prefab";
    private const string PanelName = "ViewingControl";   // UIElementEnums 이름과 같게(GameUIManagerEditor가 이름으로 enum을 만든다)
    private const string OldModeName = "ModeControl";   // 11-19 첫 판의 가로 분절 버튼. 있으면 지운다
    private const string ModeName = "ModeList";
    private const string SpeedName = "SpeedRow";
    private const string GearName = "ViewingButton";
    private const string GearIconPath = "Assets/Resources/Sprites/UI/Icon_Settings.png";   // 템플릿 UI 스프라이트의 톱니
    private const float GearGap = 8f;      // 톱니와 일시정지 버튼 사이
    private const float GearIconInset = 8f;

    private static readonly Vector2 PanelPosition = new Vector2(-16f, -84f);   // SafeAreaPanel 오른쪽 위 기준. 톱니·일시정지 버튼 아래 끝(-72)에서 12 띄운다. 폭이 두 버튼(84+8+84)을 덮는다
    private const float PanelWidth = 380f;
    private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.55f);   // 일시정지 버튼 바탕과 같은 반투명 검정
    private const float RowHeight = 120f;      // 48dp. 모드 줄·배속 줄 공통
    private const float Inset = 8f;
    private const float LabelFontSize = 34f;
    private const float RingSize = 44f;        // 라디오 바깥 원
    private const float RingHole = 32f;        // 안쪽 구멍(바탕색)
    private const float DotSize = 20f;         // 고른 표시(강조색)
    private const float RingLeft = 24f;        // 줄 왼쪽에서 원까지
    private const float LabelLeft = 88f;       // 줄 왼쪽에서 글자까지
    private const int SliderSidePadding = 28;  // 키트 슬라이더 손잡이 폭 56의 절반

    [MenuItem("Tools/UI/경기 보기 패널 배치")]
    public static void Build()
    {
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
                Debug.LogError("[ViewingControlBuilder] InGameScene에 MatchHud나 GameUIManager가 없다");
                return;
            }

            Button gear = EnsureGear(hud);
            ViewingControlUIElement panel = EnsurePanel(hud.transform);
            Transform speedRow = EnsureSpeedRow(panel.transform);
            TMP_Text fontSource = speedRow.Find("ValueText").GetComponent<TMP_Text>();   // 한글 폰트(Kostar)를 그대로 쓴다
            Transform modeList = EnsureModeList(panel.transform, fontSource);
            Wire(panel, modeList, speedRow);
            WireGear(hud, gear);
            Register(uiManager, panel);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ViewingControlBuilder] 경기 보기 패널을 배치했다");
        }
        finally
        {
            if (openedHere)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
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

    // 일시정지 버튼을 복제하지 않고 같은 크기·바탕색으로 새로 만든다(일시정지의 글자 "II" 대신 톱니 이미지)
    private static Button EnsureGear(MatchHudUIElement hud)
    {
        var pause = (RectTransform)hud.transform.Find("PauseButton");
        Transform existing = hud.transform.Find(GearName);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(GearName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.layer = hud.gameObject.layer;
            go.transform.SetParent(hud.transform, false);
        }
        if (go.transform.GetSiblingIndex() > pause.GetSiblingIndex())
        {
            go.transform.SetSiblingIndex(pause.GetSiblingIndex());   // 일시정지 앞. 이미 앞이면 그대로 둬 다시 돌려도 순서가 같다
        }

        var rect = (RectTransform)go.transform;
        rect.anchorMin = pause.anchorMin;
        rect.anchorMax = pause.anchorMax;
        rect.pivot = pause.pivot;
        rect.sizeDelta = pause.sizeDelta;
        rect.anchoredPosition = pause.anchoredPosition - new Vector2(pause.sizeDelta.x + GearGap, 0f);
        var background = go.GetComponent<Image>();
        background.color = pause.GetComponent<Image>().color;
        go.GetComponent<Button>().targetGraphic = background;

        EnsureGearIcon(go.transform);
        return go.GetComponent<Button>();
    }

    private static void EnsureGearIcon(Transform gear)
    {
        Transform existing = gear.Find("Icon");
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = gear.gameObject.layer;
            go.transform.SetParent(gear, false);
        }
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(-2f * GearIconInset, -2f * GearIconInset);
        var icon = go.GetComponent<Image>();
        icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GearIconPath);
        icon.preserveAspect = true;
        icon.raycastTarget = false;   // 누름은 바탕(Button)이 받는다
    }

    private static void WireGear(MatchHudUIElement hud, Button gear)
    {
        var serialized = new SerializedObject(hud);
        serialized.FindProperty("viewingButton").objectReferenceValue = gear;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // HUD 바로 뒤 형제로 둔다: 결과·일시정지 창보다 먼저 그려져 그 창들이 덮는다
    private static ViewingControlUIElement EnsurePanel(Transform hud)
    {
        Transform parent = hud.parent;
        Transform existing = parent.Find(PanelName);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(PanelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ViewingControlUIElement));
            go.layer = hud.gameObject.layer;
            go.transform.SetParent(parent, false);
        }
        go.transform.SetSiblingIndex(hud.GetSiblingIndex() + 1);
        go.SetActive(false);   // 런타임엔 Initialize가 끈다. 씬에서도 꺼 둬 에디터 화면에서 HUD처럼 보이지 않게

        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = PanelPosition;
        int rows = ViewingOptions.ModeRows.Length + 1;   // 모드 줄 + 배속 줄
        rect.sizeDelta = new Vector2(PanelWidth, rows * RowHeight + 2f * Inset);
        go.GetComponent<Image>().color = PanelColor;
        return go.GetComponent<ViewingControlUIElement>();
    }

    // 세로 라디오 목록: 위에서부터 ViewingOptions.ModeRows 순서. 한 ToggleGroup 아래 줄 3개
    private static Transform EnsureModeList(Transform panel, TMP_Text fontSource)
    {
        Transform old = panel.Find(OldModeName);
        if (old != null) { Object.DestroyImmediate(old.gameObject); }

        GameObject list = EnsureChild(panel, ModeName, typeof(RectTransform), typeof(ToggleGroup));
        int rows = ViewingOptions.ModeRows.Length;
        var rect = (RectTransform)list.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -Inset);
        rect.sizeDelta = new Vector2(-2f * Inset, rows * RowHeight);
        var group = list.GetComponent<ToggleGroup>();
        group.allowSwitchOff = false;

        for (int row = 0; row < rows; row++)
        {
            EnsureRadioRow(list.transform, row, group, fontSource);
        }
        return list.transform;
    }

    private static void EnsureRadioRow(Transform list, int row, ToggleGroup group, TMP_Text fontSource)
    {
        GameObject go = EnsureChild(list, $"Row{row}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Toggle));
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -row * RowHeight);
        rect.sizeDelta = new Vector2(0f, RowHeight);
        var hit = go.GetComponent<Image>();
        hit.color = Color.clear;   // 줄 전체가 누름 영역(48dp). 보이는 것은 원과 글자뿐
        hit.raycastTarget = true;

        Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>(KnobPath);
        Image ring = EnsureCircle(go.transform, "Ring", RingSize, UIKitColors.SubText, knob);
        PlaceLeft((RectTransform)ring.transform, RingLeft);
        Image hole = EnsureCircle(ring.transform, "Hole", RingHole, UIKitColors.Surface, knob);
        Image dot = EnsureCircle(hole.transform, "Dot", DotSize, UIKitColors.Accent, knob);

        var toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = hit;
        toggle.graphic = dot;
        toggle.transition = Selectable.Transition.None;
        toggle.toggleTransition = Toggle.ToggleTransition.None;
        toggle.group = group;
        toggle.isOn = row == ViewingOptions.RowOf(ViewingOptions.DefaultMode);   // 런타임엔 Show가 지금 모드로 다시 칠한다

        GameObject labelGo = EnsureChild(go.transform, "Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.offsetMin = new Vector2(LabelLeft, 0f);
        labelRect.offsetMax = Vector2.zero;
        var label = labelGo.GetComponent<TMP_Text>();
        label.font = fontSource.font;
        label.fontSharedMaterial = fontSource.fontSharedMaterial;
        label.fontSize = LabelFontSize;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        label.text = string.Empty;   // 글자는 ViewingControlUIElement.Initialize가 넣는다
    }

    private static Image EnsureCircle(Transform parent, string name, float size, Color color, Sprite knob)
    {
        GameObject go = EnsureChild(parent, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(size, size);
        var image = go.GetComponent<Image>();
        image.sprite = knob;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void PlaceLeft(RectTransform rect, float left)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(left + rect.sizeDelta.x * 0.5f, 0f);
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

    // 키트 SliderRow의 모양(슬라이더 + 값 글자, 가로 배치)을 쓰되, SliderRow 스크립트는 0~1 연속값·음소거 전용이라 떼고 음소거 버튼도 지운다.
    // 정수 칸 범위는 ViewingControlUIElement가 Speeds 길이로 정한다
    private static Transform EnsureSpeedRow(Transform panel)
    {
        Transform existing = panel.Find(SpeedName);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SliderRowPath);
            go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, panel);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = SpeedName;
            Object.DestroyImmediate(go.GetComponent<SliderRow>());
            Transform mute = go.transform.Find("MuteButton");
            if (mute != null) { Object.DestroyImmediate(mute.gameObject); }
        }
        PlaceBottomRow((RectTransform)go.transform);
        // 손잡이(56)와 막대가 줄 왼쪽 끝에 붙어 패널 밖으로 삐져 보였다(캡처). 손잡이 반폭만큼 안쪽에서 시작한다
        go.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(SliderSidePadding, 0, 0, 0);
        SetFontSize(go);
        return go.transform;
    }

    // 배속 줄은 패널 아래에 붙인다(모드 목록 아래). 가로는 패널 폭에서 양쪽 여백만 뺀다. 높이는 모드 줄과 같은 48dp라 슬라이더 누름 영역도 그만큼이다
    private static void PlaceBottomRow(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, Inset);
        rect.sizeDelta = new Vector2(-2f * Inset, RowHeight);
    }

    private static void SetFontSize(GameObject row)
    {
        TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            texts[i].fontSize = LabelFontSize;
            EditorUtility.SetDirty(texts[i]);
        }
    }

    private static void Wire(ViewingControlUIElement panel, Transform modeList, Transform speedRow)
    {
        var serialized = new SerializedObject(panel);
        int rows = ViewingOptions.ModeRows.Length;
        SerializedProperty toggles = serialized.FindProperty("modeToggles");
        SerializedProperty labels = serialized.FindProperty("modeLabels");
        toggles.arraySize = rows;
        labels.arraySize = rows;
        for (int row = 0; row < rows; row++)
        {
            Transform rowTransform = modeList.Find($"Row{row}");
            toggles.GetArrayElementAtIndex(row).objectReferenceValue = rowTransform.GetComponent<Toggle>();
            labels.GetArrayElementAtIndex(row).objectReferenceValue = rowTransform.Find("Label").GetComponent<TMP_Text>();
        }
        serialized.FindProperty("speedSlider").objectReferenceValue = speedRow.GetComponentInChildren<Slider>(true);
        serialized.FindProperty("speedText").objectReferenceValue = speedRow.Find("ValueText").GetComponent<TMP_Text>();
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
}
