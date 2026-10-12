using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 3단 값(0·1·2) 하나를 고르는 버튼 묶음. 팀 전술 설정창 세부 표의 한 칸(09-30). 값의 뜻(짧게·직접·롱볼 등)은 모른다:
// 글자는 패널이 TacticLevelNames에서 가져와 SetLabels로 넣는다(10-12, 프리팹 글자와 결과 화면 배열이 갈리던 것을 한 곳으로).
// 코드가 값을 채울 때(SetLevel)는 Changed를 쏘지 않는다: 카드·슬라이더가 표를 다시 채울 때마다 "사용자가 바꿨다"로 저장되는 순환을 막는다
public class TacticLevelSelector : MonoBehaviour
{
    private static readonly Color SelectedColor = TacticsColors.Highlight;
    private static readonly Color NormalColor = Color.white;

    [SerializeField] private Button[] levelButtons;   // 0·1·2 순서
    [SerializeField] private Image[] levelImages;

    public int Level { get; private set; }
    public event Action<TacticLevelSelector> Changed;

    public void Initialize()
    {
        for (int i = 0; i < levelButtons.Length; i++)
        {
            int level = i;   // 클로저가 루프 변수 대신 이 버튼의 값을 잡게 한다
            levelButtons[i].onClick.AddListener(() => OnLevelClicked(level));
        }
    }

    // 버튼마다 글자. 프리팹에 라벨 참조를 따로 배선하지 않고 버튼 아래 TMP_Text 하나를 찾는다(배선 변경 없이 글자 출처만 코드로)
    public void SetLabels(string[] names)
    {
        if (names.Length != levelButtons.Length)
        {
            Debug.LogError($"[TacticLevelSelector] 이름 {names.Length}개 ≠ 버튼 {levelButtons.Length}개");
            return;
        }
        for (int i = 0; i < levelButtons.Length; i++)
        {
            TMP_Text label = levelButtons[i].GetComponentInChildren<TMP_Text>();
            if (label == null) { continue; }
            label.text = names[i];
        }
    }

    public void SetLevel(int level)
    {
        Level = level;
        for (int i = 0; i < levelImages.Length; i++)
        {
            levelImages[i].color = i == level ? SelectedColor : NormalColor;
        }
    }

    private void OnLevelClicked(int level)
    {
        if (level == Level) { return; }
        SetLevel(level);
        Changed?.Invoke(this);
    }
}
