using Game.Core.Tactics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 체력 부담 막대(09-30). 팀 전술 패널의 카드 설명 아래에 놓인다. FM26 전술 화면의 Intensity 막대처럼 8칸 중 몇 칸이 찼는지와 단계 글자를 보여 준다.
// 몇 칸인지·어느 단계인지는 순수 코어(StaminaLoad)가 정하고 여기는 받은 값을 칠하기만 한다
public class StaminaLoadGauge : MonoBehaviour
{
    private static readonly Color EmptyColor = new Color(0.22f, 0.25f, 0.30f);    // 안 찬 칸. 패널 바탕보다 조금 밝은 회색
    private static readonly Color LowColor = new Color(0.51f, 0.78f, 0.52f);      // 낮음(#81C784)
    private static readonly Color MediumColor = TacticsColors.Highlight;          // 보통. 전술 화면 공통 강조색
    private static readonly Color HighColor = new Color(1f, 0.54f, 0.40f);        // 높음(#FF8A65)

    [SerializeField] private Image[] segments;   // 왼쪽부터 8칸(StaminaLoad.Segments)
    [SerializeField] private TMP_Text levelText;

    public void Show(int filledSegments, StaminaLoadLevel level)
    {
        if (segments.Length != StaminaLoad.Segments)
        {
            Debug.LogError($"[StaminaLoadGauge] 칸 {segments.Length}개 ≠ {StaminaLoad.Segments}. 프리팹 배선을 확인한다");
        }

        Color fill = FillColor(level);
        for (int i = 0; i < segments.Length; i++)
        {
            segments[i].color = i < filledSegments ? fill : EmptyColor;
        }
        levelText.text = LevelLabel(level);
    }

    private static Color FillColor(StaminaLoadLevel level)
    {
        switch (level)
        {
            case StaminaLoadLevel.Low: return LowColor;
            case StaminaLoadLevel.Medium: return MediumColor;
            default: return HighColor;
        }
    }

    private static string LevelLabel(StaminaLoadLevel level)
    {
        switch (level)
        {
            case StaminaLoadLevel.Low: return "낮음";
            case StaminaLoadLevel.Medium: return "보통";
            default: return "높음";
        }
    }
}
