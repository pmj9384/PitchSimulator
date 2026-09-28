using System.Text;
using Game.Core.League;
using Game.Core.Match;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 결과 화면(09-28): 승·무·패 한 패널. 이번 경기 스코어 + 이번 라운드까지 반영한 승점표 + 다음 경기/로비 버튼. 옛 GameOverPanel(패배 전용)을 대체한다.
// 승점표는 SeasonSystem이 나머지 경기를 백그라운드로 다 돌리고 저장까지 끝내야(IsReporting false) 확정된다. 그 전에 다음 경기로 가면 새 씬이 아직 넘어가지 않은 라운드(끝난 경기)를 다시 읽으므로 버튼을 잠근다(09-28 리뷰로 이유 수정).
// 참조가 둘(경기·시즌)인 이유: 스코어는 휘슬 순간 경기에서 바로 쓰고, 표는 보고가 끝난 뒤 시즌에서 쓴다. 시점이 달라 한쪽으로 합치면 스코어도 계산이 끝날 때까지 비게 된다
// 끝남을 알리는 이벤트를 두지 않고 HUD처럼 매 프레임 IsReporting만 본다(Dirty Flag): bool 하나라 구독-해제 표면이 더 비싸고, 스레드에서 돌아온 콜백이 파괴된 패널을 건드릴 일도 없다.
// 표는 한 번만 만든다. 만든 뒤엔 Update가 곧바로 빠진다
public class ResultPanelUIElement : UIElement
{
    private const string HighlightColor = "#FFD54F";   // 내 행 강조(노랑). 표 안 다른 행은 기본 색

    // 승점표 열 위치(TMP <pos> 태그, 텍스트 폭 기준 %). 한 TMP 텍스트에 표 전체를 쓰려고 열을 태그로 맞춘다. 행마다 오브젝트를 두면 팀 수(4~12)만큼 풀이 필요하다
    private const string ColRank = "<pos=0%>";
    private const string ColTeam = "<pos=10%>";
    private const string ColPlayed = "<pos=50%>";
    private const string ColWon = "<pos=60%>";
    private const string ColDrawn = "<pos=67%>";
    private const string ColLost = "<pos=74%>";
    private const string ColGoalDiff = "<pos=81%>";
    private const string ColPoints = "<pos=90%>";

    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private TMP_Text tableText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button nextButton;
    [SerializeField] private TMP_Text nextButtonLabel;

    private readonly StringBuilder tableBuilder = new(512);   // 표는 패널이 뜰 때 한 번 만든다. 재사용해 결과마다 버퍼를 새로 잡지 않는다
    private bool tableShown;

    public override void Initialize()
    {
        gameObject.SetActive(false);
        nextButton.onClick.AddListener(OnNext);
    }

    // 스코어는 경기에서 바로 쓴다. 승점표는 시즌 보고가 끝나야 확정이라 비워 두고 버튼도 잠근다(Update가 풀어 준다)
    public override void Show()
    {
        gameObject.SetActive(true);

        MatchManager match = gameManager.Match;
        MatchSimulation sim = match.Simulation;   // 팀 0이 항상 나(홈/원정과 무관)
        titleText.text = TitleOf(sim.HomeGoals, sim.AwayGoals);
        scoreText.text = $"{SeasonSystem.MyTeamName}  {sim.HomeGoals} : {sim.AwayGoals}  {match.OpponentName}";

        statusText.text = "다른 경기 결과 계산 중…";
        roundText.text = string.Empty;
        tableText.text = string.Empty;
        nextButton.interactable = false;
        tableShown = false;
    }

    public override void Hide() => gameObject.SetActive(false);

    private void Update()
    {
        if (tableShown) { return; }

        SeasonSystem season = GameDataManager.Instance.Season;
        if (season.IsReporting) { return; }

        // 버튼부터 연다: 아래 표 만들기가 던져도 화면이 잠긴 채 멈추지 않게(09-28 리뷰)
        tableShown = true;
        nextButton.interactable = true;
        nextButtonLabel.text = season.IsOver ? "로비로" : "다음 경기";
        statusText.text = season.LastReportFailed ? "결과를 저장하지 못했다" : string.Empty;   // 실패를 성공처럼 보이지 않게(09-28 리뷰)
        roundText.text = $"{season.Tier.Tier}부 {season.State.RoundsPlayed}/{season.Tier.Matches} 라운드";
        try
        {
            tableText.text = BuildTable(season);
            if (!season.LastReportFailed && season.IsOver) { statusText.text = OutcomeLine(season.Outcome(), season.Tier.Tier); }   // 시즌 마지막 판: 승강 판정(09-28)
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Result] 승점표를 만들지 못했다: {e}");
            tableText.text = "승점표를 불러오지 못했다";
        }
    }

    // 다음 경기 = 같은 씬 재로드(결과 보고가 먼저 라운드를 올린다). 시즌이 끝났으면 다음 시즌 입구는 로비(PrepareNextMatch) 하나라 로비로 보낸다(09-27 리뷰 R1)
    private void OnNext()
    {
        SeasonSystem season = GameDataManager.Instance.Season;
        if (season.IsReporting) { return; }   // 버튼이 잠겨 있어 안 오지만, 저장 전 재로드는 라운드를 잃으니 한 번 더 막는다

        if (season.IsOver)
        {
            gameManager.GoToTitle();
            return;
        }
        gameManager.RestartGame(skipReady: false);
    }

    // 시즌 끝 문구. 2·3위(승강전 대상)는 승강전(10-05)이 생기기 전까지 잔류로 보여 준다: 없는 기능을 문구로 약속하지 않는다
    private static string OutcomeLine(SeasonOutcome outcome, int tier)
    {
        if (outcome.Kind == SeasonOutcomeKind.Champion) { return $"시즌 종료 · {tier}부 우승!"; }
        if (outcome.Kind == SeasonOutcomeKind.Promoted) { return $"시즌 종료 · 최종 {outcome.FinalRank}위 · {outcome.NextTier}부 승격!"; }
        if (outcome.Kind == SeasonOutcomeKind.Relegated) { return $"시즌 종료 · 최종 {outcome.FinalRank}위 · {outcome.NextTier}부 강등"; }
        return $"시즌 종료 · 최종 {outcome.FinalRank}위 · 잔류";
    }

    // MatchManager.WinnerByGoals와 같은 기준(골 수). 무승부도 GameOver 국면이라 국면으로는 무/패를 못 가른다
    private static string TitleOf(int myGoals, int opponentGoals)
    {
        if (myGoals > opponentGoals) { return "승리"; }
        if (myGoals == opponentGoals) { return "무승부"; }
        return "패배";
    }

    private string BuildTable(SeasonSystem season)
    {
        LeagueTable table = season.Table();
        StringBuilder sb = tableBuilder.Clear();
        sb.Append(ColRank).Append("순위")
          .Append(ColTeam).Append("팀")
          .Append(ColPlayed).Append("경기")
          .Append(ColWon).Append("승")
          .Append(ColDrawn).Append("무")
          .Append(ColLost).Append("패")
          .Append(ColGoalDiff).Append("득실")
          .Append(ColPoints).Append("승점");

        for (int i = 0; i < table.Count; i++)
        {
            LeagueRow row = table[i];
            bool mine = row.TeamId == SeasonState.MyTeamId;
            sb.Append('\n');
            if (mine) { sb.Append("<color=").Append(HighlightColor).Append('>'); }

            sb.Append(ColRank).Append(i + 1)
              .Append(ColTeam).Append(season.TeamName(row.TeamId))
              .Append(ColPlayed).Append(row.Played)
              .Append(ColWon).Append(row.Won)
              .Append(ColDrawn).Append(row.Drawn)
              .Append(ColLost).Append(row.Lost)
              .Append(ColGoalDiff);
            if (row.GoalDifference > 0) { sb.Append('+'); }   // 득실은 부호까지(+3 / 0 / -2). 음수는 Append(int)가 붙인다
            sb.Append(row.GoalDifference)
              .Append(ColPoints).Append(row.Points);

            if (mine) { sb.Append("</color>"); }
        }
        return sb.ToString();
    }
}
