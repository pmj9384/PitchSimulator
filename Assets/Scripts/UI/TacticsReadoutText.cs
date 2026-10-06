using System.Text;
using Game.Core.Match;
using Game.Core.Tactics;
using UnityEngine;

// 결과 화면의 "설정 대 실측" 표 글자(10-06, 스펙 §6). 내 설정 한 줄 아래에 이번 경기 실측 한 줄을 놓아 설정이 경기에 어떻게 나왔는지 읽게 한다.
// 열은 팀 전술 세부 표와 같은 서드 3열(우리 진영·중앙·상대 진영). 승점표처럼 TMP <pos> 태그로 열을 맞춰 한 텍스트에 쓴다.
// 무엇을 셌는지는 순수 코어(TacticsProbe)가 정하고 여기는 글자로 옮기기만 한다
public static class TacticsReadoutText
{
    private const string ColLabel = "<pos=0%>";
    private static readonly string[] ColThird = { "<pos=40%>", "<pos=60%>", "<pos=80%>" };   // Third 순서
    private static readonly string[] ThirdNames = { "우리 진영", "중앙", "상대 진영" };

    // 값 0·1·2의 이름. 팀 전술 세부 표의 버튼 글자와 같은 말이다(TeamTactics 주석)
    private static readonly string[] PassStyleNames = { "짧게", "직접", "롱볼" };
    private static readonly string[] PressStartNames = { "안 감", "표준", "적극" };
    private static readonly string[] CounterNames = { "안 함", "상황 봐서", "적극" };

    private static readonly string SettingColorOpen = $"<color=#{ColorUtility.ToHtmlStringRGB(TacticsColors.Highlight)}>";
    private const string ColorClose = "</color>";

    public static string Build(TeamTactics setting, TacticsReadout measured, StringBuilder sb)
    {
        sb.Clear();
        sb.Append(SettingColorOpen).Append("노란 줄은 내 설정").Append(ColorClose).Append(", 그 아래는 이번 경기");

        sb.Append('\n').Append(ColLabel);
        for (int i = 0; i < ThirdNames.Length; i++)
        {
            sb.Append(ColThird[i]).Append(ThirdNames[i]);
        }

        AppendSettingRow(sb, "패스 방식", setting.PassStyle, PassStyleNames);
        sb.Append('\n').Append(ColLabel).Append("평균 패스 길이");
        for (int i = 0; i < ColThird.Length; i++)
        {
            sb.Append(ColThird[i]);
            AppendPassLength(sb, measured, (Third)i);
        }

        AppendSettingRow(sb, "압박 시작", setting.PressStart, PressStartNames);
        sb.Append('\n').Append(ColLabel).Append("공을 뺏은 횟수");
        for (int i = 0; i < ColThird.Length; i++)
        {
            sb.Append(ColThird[i]).Append(measured.RegainCount((Third)i)).Append("회");
        }

        sb.Append('\n').Append(SettingColorOpen).Append(ColLabel).Append("역습 성향").Append(ColThird[0]).Append(CounterNames[setting.Counter]).Append(ColorClose);
        sb.Append('\n').Append(ColLabel).Append("역습").Append(ColThird[0])
          .Append(measured.Counters).Append("회, 역습 슛 ").Append(measured.CounterShots).Append("개 (전체 슛 ").Append(measured.Shots).Append("개)");
        return sb.ToString();
    }

    private static void AppendSettingRow(StringBuilder sb, string label, int[] levels, string[] names)
    {
        sb.Append('\n').Append(SettingColorOpen).Append(ColLabel).Append(label);
        for (int i = 0; i < ColThird.Length; i++)
        {
            sb.Append(ColThird[i]).Append(names[levels[i]]);
        }
        sb.Append(ColorClose);
    }

    // "17m · 18회". 그 서드에서 받은 패스가 없으면 길이를 낼 수 없어 "-"
    private static void AppendPassLength(StringBuilder sb, TacticsReadout measured, Third third)
    {
        int count = measured.PassCount(third);
        if (count == 0)
        {
            sb.Append('-');
            return;
        }
        sb.Append(Mathf.RoundToInt(measured.MeanPassLength(third))).Append("m · ").Append(count).Append("회");
    }
}
