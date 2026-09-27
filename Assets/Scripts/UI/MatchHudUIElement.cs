using Game.Core.Match;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 경기 HUD(09-27): 스코어·시계(전반/후반 mm:ss)·소유 팀·일시정지 버튼. 값은 MatchManager에서 매 프레임 읽되 바뀐 프레임에만 문자열을 다시 만든다
// (Unity e-book Dirty Flag, 모바일 가이드 "매 프레임 문자열 금지"). 이벤트 구독을 두지 않는 이유: 정수 3개라 구독-해제 표면이 더 비싸다
public class MatchHudUIElement : UIElement
{
    private const string MyTeamName = "내 팀";   // 내 팀 이름은 아직 세이브에 없다(09-27 결정 ③-a). 이름 짓기 UI가 생길 때 세이브 필드로
    private const string OwnerMark = "●";

    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text clockText;
    [SerializeField] private Button pauseButton;
    [SerializeField] private TMP_Text captionText;   // 하프타임 자막. 09-28 골·FT 자막도 이 자리

    private int shownSecond = -1;   // 총 초(0~5400). 초가 바뀐 프레임에만 시계를 다시 쓴다
    private int shownHomeGoals = -1;
    private int shownAwayGoals = -1;
    private int shownOwnerTeam = -2;   // -1 = 자유 공도 표시 상태라 초기값은 그 밖의 값
    private bool shownHold;

    public override void Initialize()
    {
        gameObject.SetActive(false);
        pauseButton.onClick.AddListener(OnPause);
    }

    // 일시정지는 경기 중에만. 세팅 국면·결과 국면에서 GameStop으로 가면 복귀할 상태가 애매하다
    private void OnPause()
    {
        if (gameManager.CurrentState != GameManager.GameState.GamePlay) { return; }
        gameManager.SetGameState(GameManager.GameState.GameStop);
    }

    public override void Show()
    {
        gameObject.SetActive(true);
        ResetShown();
    }

    public override void Hide() => gameObject.SetActive(false);

    private void Update()
    {
        MatchManager match = gameManager.Match;
        if (match == null || match.Simulation == null) { return; }

        bool hold = match.InHalfTimeHold;
        if (hold != shownHold)
        {
            shownHold = hold;
            captionText.text = hold ? "하프타임" : string.Empty;
        }

        MatchSimulation sim = match.Simulation;
        int totalSecond = MatchClock.TotalSecondsOf(match.Ticks, sim.Added);
        if (totalSecond != shownSecond)
        {
            shownSecond = totalSecond;
            ClockReading c = MatchClock.Describe(match.Ticks, sim.Added);
            string half = c.SecondHalf ? "후반" : "전반";
            clockText.text = c.InAddedTime
                ? $"{half} {c.Minute}+{c.AddedMinute}:{c.AddedSecond:00}"      // 중계식 "45+1:30", "90+3:12"
                : $"{half} {c.Minute:00}:{c.Second:00}";
        }

        int ownerTeam = sim.OwnerTeam();
        if (sim.HomeGoals != shownHomeGoals || sim.AwayGoals != shownAwayGoals || ownerTeam != shownOwnerTeam)
        {
            shownHomeGoals = sim.HomeGoals;
            shownAwayGoals = sim.AwayGoals;
            shownOwnerTeam = ownerTeam;
            string homeMark = ownerTeam == 0 ? OwnerMark : string.Empty;
            string awayMark = ownerTeam == 1 ? OwnerMark : string.Empty;
            scoreText.text = $"{homeMark} {MyTeamName}  {sim.HomeGoals} : {sim.AwayGoals}  {match.OpponentName} {awayMark}";
        }
    }

    private void ResetShown()
    {
        shownSecond = -1;
        shownHomeGoals = -1;
        shownAwayGoals = -1;
        shownOwnerTeam = -2;
        shownHold = false;
        captionText.text = string.Empty;
    }
}
