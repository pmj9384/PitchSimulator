using System;
using Game.Core.Tactics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 3단 값(0·1·2) 하나를 고르는 칸. 팀 전술 표의 한 칸이다. 버튼 하나를 누르면 다음 단으로 돈다(0 → 1 → 2 → 0, 10-21 목업 4번 보드).
// 전엔 버튼 3개가 한 칸이었는데, 서드 3행 × 항목 4열 표에 3개씩 깔면 버튼이 약 32px라 터치 기준(Apple HIG 44pt·Material 48dp)에 못 미쳤다.
// 값의 뜻(짧게·직접·롱볼 등)은 모른다: 글자는 패널이 TacticLevelNames에서 SetLabels로 넣는다.
// 코드가 값을 채울 때(SetLevel)는 Changed를 쏘지 않는다: 카드·슬라이더가 표를 다시 채울 때마다 "사용자가 바꿨다"로 저장되는 순환을 막는다.
// 바탕색은 기준 카드와 다른 칸 표시(SetDiffersFromCard). 어느 입력(슬라이더·표)이 바꿨는지는 세이브에 없어 "다른 칸 전부"가 기준이다(10-21 결정)
public class TacticLevelSelector : MonoBehaviour
{
    private static readonly Color DiffersColor = TacticsColors.Highlight;
    private static readonly Color NormalColor = Color.white;

    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Image background;

    private string[] labels = Array.Empty<string>();   // 단마다 글자

    public int Level { get; private set; }
    public event Action<TacticLevelSelector> Changed;

    public void Initialize()
    {
        button.onClick.AddListener(OnClicked);
    }

    // 단마다 글자. 지금 단의 글자를 바로 비춘다
    public void SetLabels(string[] names)
    {
        if (names.Length != TeamTactics.Levels)
        {
            Debug.LogError($"[TacticLevelSelector] {name}: 이름 {names.Length}개 ≠ 단 {TeamTactics.Levels}개");
            return;
        }
        labels = names;
        ShowLabel();
    }

    public void SetLevel(int level)
    {
        Level = level;
        ShowLabel();
    }

    public void SetDiffersFromCard(bool differs)
    {
        background.color = differs ? DiffersColor : NormalColor;
    }

    private void OnClicked()
    {
        SetLevel((Level + 1) % TeamTactics.Levels);
        Changed?.Invoke(this);
    }

    private void ShowLabel()
    {
        if (labels.Length == 0) { return; }
        label.text = labels[Level];
    }
}
