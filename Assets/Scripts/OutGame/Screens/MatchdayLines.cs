using System.Collections.Generic;
using Game.Core.League;
using Game.Core.Tactics;

// 홈과 매치데이가 같이 쓰는 다음 경기 문장(스펙 §2-1, 10-15). 두 화면이 같은 상대·같은 전술 이름을 보여야 해서 한 곳에 둔다.
// 값은 전부 시즌에서 읽기만 한다
public static class MatchdayLines
{
    // 시즌이 끝났을 때 홈·매치데이가 같은 말을 한다. 다음 시즌을 여는 버튼은 홈에만 있다
    public const string SeasonOverNotice = "시즌이 끝났다. 홈의 [다음 시즌]이 새 부의 상대와 다시 시작한다";

    public static string Matchup(MatchSetup setup)
    {
        string venue = setup.MyTeamIsHome ? "홈" : "원정";
        return $"상대 {setup.OpponentName} ({venue}) · 상대 전술: {setup.Tactics1.DisplayName}";
    }

    // 카드 이름만. 슬라이더·세부 표로 바꿨으면 "(조정)"을 붙여 전술판에서 확인하게 한다(세부 값은 여기 안 적는다, 스펙 §2-1 [가정]).
    // 이름을 어디서 읽는지는 SeasonState.MyCardName이 정한다. 프리셋 표는 호출하는 화면이 넘긴다
    public static string MyTactics(SeasonState state, IReadOnlyList<TeamTactics> presets)
    {
        string card = state.MyCardName(presets);
        string adjusted = state.MyCustomTactics != null ? " (조정)" : string.Empty;
        return $"내 전술: {card}{adjusted}";
    }
}
