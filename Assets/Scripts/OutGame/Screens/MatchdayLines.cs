using System.Collections.Generic;
using System.Text;
using Game.Core.League;
using Game.Core.Tactics;
using UnityEngine;

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

    // ── 일정표(스펙 §2-1 10-19). 날짜는 없다: 시뮬에 달력이 없어 라운드 번호만. 레퍼런스 FM Fixtures(상대·홈/원정·결과 한 줄씩)
    private static readonly string HighlightColor = "#" + ColorUtility.ToHtmlStringRGB(TacticsColors.Highlight);   // 다음 경기·내 순위 줄. 결과 화면 표와 같은 색

    // 매치데이 왼쪽 열: 정규 라운드 전부 + 승강전 행
    public static string FixturesBlock(SeasonSystem season)
    {
        List<FixtureRow> rows = FixtureList.Build(season.Schedule, season.State.Results, SeasonState.MyTeamId, season.Tier.Teams);
        var sb = new StringBuilder(rows.Count * 32);
        sb.Append("일정");
        for (int i = 0; i < rows.Count; i++)
        {
            bool isNext = !season.IsRegularSeasonOver && rows[i].Round == season.State.RoundsPlayed;
            sb.Append('\n').Append(FixtureLine(rows[i], season.TeamName(rows[i].OpponentId), isNext));
        }
        AppendPlayoffRows(sb, season);
        return sb.ToString();
    }

    public static string FixtureLine(FixtureRow row, string opponentName, bool isNext)
    {
        string venue = row.IsHome ? "vs" : "@";
        string head = $"R{row.Round + 1}  {venue} {opponentName}";
        if (row.Played) { return $"{head}   {row.MyGoals}-{row.TheirGoals}  {Outcome(row)}"; }
        if (isNext) { return $"<color={HighlightColor}>{head}   ▶ 다음 경기</color>"; }
        return head;
    }

    private static string Outcome(FixtureRow row)
    {
        if (row.Won) { return "승"; }
        if (row.Drawn) { return "무"; }
        if (row.Lost) { return "패"; }
        return string.Empty;
    }

    // 승강전 행: 대상이 아니면 없음, 정규 중엔 안내 한 줄, 정규 뒤엔 치른 경기와 다음 단계
    private static void AppendPlayoffRows(StringBuilder sb, SeasonSystem season)
    {
        if (!season.IsRegularSeasonOver)
        {
            sb.Append("\nPO  승강전은 정규 시즌 뒤 순위로 결정");
            return;
        }
        IReadOnlyList<PlayoffResult> results = season.State.PlayoffResults;
        for (int i = 0; i < results.Count; i++)
        {
            sb.Append('\n').Append(PlayoffLine(results[i], season));
        }
        string next = season.PlayoffNextLine();
        if (next.Length > 0) { sb.Append($"\n<color={HighlightColor}>PO  ▶ {next}</color>"); }
    }

    private static string PlayoffLine(PlayoffResult r, SeasonSystem season)
    {
        bool home = r.HomeTeamId == SeasonState.MyTeamId;
        int opponent = home ? r.AwayTeamId : r.HomeTeamId;
        int mine = home ? r.HomeGoals : r.AwayGoals;
        int theirs = home ? r.AwayGoals : r.HomeGoals;
        string venue = home ? "vs" : "@";
        return $"PO {StageLabel(r.Stage)}  {venue} {season.TeamName(opponent)}   {mine}-{theirs}{PenaltyText(r, home)}";
    }

    private static string StageLabel(PlayoffStage stage)
    {
        if (stage == PlayoffStage.Semifinal) { return "단판"; }
        if (stage == PlayoffStage.LegOne) { return "1차전"; }
        return "2차전";
    }

    private static string PenaltyText(PlayoffResult r, bool home)
    {
        if (r.HomePenalties + r.AwayPenalties == 0) { return string.Empty; }
        int mine = home ? r.HomePenalties : r.AwayPenalties;
        int theirs = home ? r.AwayPenalties : r.HomePenalties;
        return $" (승부차기 {mine}-{theirs})";
    }

    // 홈 카드 아래 한 줄: "R1 승 · R2 패 · R3 ▶ · R4 · R5"
    public static string StripLine(SeasonSystem season)
    {
        List<FixtureRow> rows = FixtureList.Build(season.Schedule, season.State.Results, SeasonState.MyTeamId, season.Tier.Teams);
        var sb = new StringBuilder(rows.Count * 8);
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) { sb.Append(" · "); }
            sb.Append('R').Append(rows[i].Round + 1).Append(' ');
            if (rows[i].Played) { sb.Append(Outcome(rows[i])); }
            else if (!season.IsRegularSeasonOver && rows[i].Round == season.State.RoundsPlayed) { sb.Append('▶'); }
        }
        return sb.ToString();
    }

    // 홈 띠 아래 한 줄 소식(10-21). 종류·재료는 코어 HomeNews가 시드로 정하고 여기는 문장만 만든다. 없으면 빈 줄
    public static string NewsLine(SeasonSystem season)
    {
        SeasonState state = season.State;
        NewsItem item = HomeNews.Pick(state.SeasonSeed, state.RoundsPlayed, state.Results, SeasonState.MyTeamId, season.Tier.Teams, season.BestScoutStars());
        if (item.Kind == NewsKind.OtherResult) { return $"소식 · {season.TeamName(item.HomeTeamId)} {item.HomeGoals}-{item.AwayGoals} {season.TeamName(item.AwayTeamId)}"; }
        if (item.Kind == NewsKind.RankChange) { return $"소식 · 내 순위 {item.Rank + 1}위 {RankMove(item)}"; }
        if (item.Kind == NewsKind.ScoutCandidate) { return $"소식 · 스카우트 후보에 ★{item.Stars} 선수가 있다"; }
        return string.Empty;
    }

    private static string RankMove(NewsItem item)
    {
        if (item.Rank < item.PreviousRank) { return $"(▲{item.PreviousRank - item.Rank})"; }
        if (item.Rank > item.PreviousRank) { return $"(▼{item.Rank - item.PreviousRank})"; }
        return "(유지)";
    }

    // 매치데이 오른쪽 아래 작은 순위표: "1  청운  10". 내 줄 강조
    public static string TableLines(SeasonSystem season)
    {
        LeagueTable table = season.Table();
        var sb = new StringBuilder(table.Count * 24);
        sb.Append("순위  팀  승점");
        for (int i = 0; i < table.Count; i++)
        {
            LeagueRow row = table[i];
            bool mine = row.TeamId == SeasonState.MyTeamId;
            sb.Append('\n');
            if (mine) { sb.Append("<color=").Append(HighlightColor).Append('>'); }
            sb.Append(i + 1).Append("  ").Append(season.TeamName(row.TeamId)).Append("  ").Append(row.Points);
            if (mine) { sb.Append("</color>"); }
        }
        return sb.ToString();
    }
}
