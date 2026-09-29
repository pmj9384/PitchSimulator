using Game.Core.Match;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 경기 HUD(09-27): 스코어·시계(전반/후반 mm:ss)·소유 팀·일시정지 버튼. 값은 MatchManager에서 매 프레임 읽되 바뀐 프레임에만 문자열을 다시 만든다
// (Unity e-book Dirty Flag, 모바일 가이드 "매 프레임 문자열 금지"). 이벤트 구독을 두지 않는 이유: 정수 3개라 구독-해제 표면이 더 비싸다
// 자막(09-28 자막): 경기 종료 > 하프타임 > 골 > 빈칸 순으로 한 칸을 나눠 쓴다. 골도 폴링으로 잡는다(점수 증가 = 골). 이 칸이 장부 「해설」(템플릿 해설)의 첫 층이다
public class MatchHudUIElement : UIElement
{
    private const string OwnerMark = "●";
    private const string FullTimeCaption = "경기 종료";
    private const string HalfTimeCaption = "하프타임";

    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text clockText;
    [SerializeField] private Button pauseButton;
    [SerializeField] private TMP_Text captionText;   // 경기 흐름 자막 한 칸(09-28 자막): 경기 종료·하프타임·골. 나중에 템플릿 해설(장부 「해설」)이 같은 칸에 얹힌다
    [SerializeField] private float goalCaptionSec = 2f;   // 골 자막 유지 시간. Time.time 기준이라 일시정지(timeScale 0) 동안엔 줄지 않는다

    private int shownSecond = -1;   // 총 초(0~5400). 초가 바뀐 프레임에만 시계를 다시 쓴다
    private int shownHomeGoals = -1;
    private int shownAwayGoals = -1;
    private int shownOwnerTeam = -2;   // -1 = 자유 공도 표시 상태라 초기값은 그 밖의 값
    private string shownCaption = string.Empty;   // 지금 captionText에 쓰인 자막. 원하는 자막이 달라진 프레임에만 다시 쓴다

    // 골 감지용 기준 점수(09-28 자막). 스코어 표시용 shown*과 따로 둔다: shown*은 -1에서 시작해 첫 프레임에 무조건 바뀌므로 골로 오인된다.
    // -1 = 아직 기준을 안 잡음. Show 뒤 첫 프레임에 현재 점수로 잡고 그다음부터 늘어난 쪽을 골로 본다
    private int seenHomeGoals = -1;
    private int seenAwayGoals = -1;
    private string goalCaption = string.Empty;   // 골 순간에 한 번 만들어 둔 "골! 팀 이름". 매 프레임 문자열을 만들지 않으려고 캐시한다
    private float goalCaptionUntil;              // 이 Time.time 전까지 골 자막을 띄운다

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

        MatchSimulation sim = match.Simulation;
        UpdateCaption(match, sim);

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
            scoreText.text = $"{homeMark} {SeasonSystem.MyTeamName}  {sim.HomeGoals} : {sim.AwayGoals}  {match.OpponentName} {awayMark}";
        }
    }

    // 원하는 자막을 고르고, 지금 쓰인 것과 다를 때만 TMP에 쓴다(Dirty Flag)
    private void UpdateCaption(MatchManager match, MatchSimulation sim)
    {
        TrackGoals(match, sim);

        string caption = DesiredCaption(match);
        if (caption == shownCaption) { return; }
        shownCaption = caption;
        captionText.text = caption;
    }

    // 점수가 늘어난 프레임 = 골. 팀 0(홈)은 늘 나, 팀 1은 이번 상대
    private void TrackGoals(MatchManager match, MatchSimulation sim)
    {
        if (seenHomeGoals < 0)
        {
            seenHomeGoals = sim.HomeGoals;
            seenAwayGoals = sim.AwayGoals;
            return;
        }

        if (sim.HomeGoals > seenHomeGoals)
        {
            ShowGoal(SeasonSystem.MyTeamName);
        }
        else if (sim.AwayGoals > seenAwayGoals)
        {
            ShowGoal(match.OpponentName);
        }

        // 줄어든 경우(새 경기로 시뮬이 바뀜)도 기준만 따라가고 자막은 띄우지 않는다
        seenHomeGoals = sim.HomeGoals;
        seenAwayGoals = sim.AwayGoals;
    }

    private void ShowGoal(string teamName)
    {
        goalCaption = $"골! {teamName}";
        goalCaptionUntil = Time.time + goalCaptionSec;
    }

    // 우선순위: 골 세리머니 > 경기 종료 > 하프타임 > 골 > 빈칸. 경기 종료는 결과 화면(GameUIManager.resultPanelDelaySec 뒤)이 덮을 때까지 유지한다.
    // 세리머니(09-29) 동안은 골 자막을 유지한다: 마지막 틱 골이면 세리머니가 끝난 뒤에 경기 종료가 뜬다
    private string DesiredCaption(MatchManager match)
    {
        if (match.InCelebration) { return goalCaption; }
        if (match.Ticks >= MatchTuning.MatchTicks) { return FullTimeCaption; }
        if (match.InHalfTimeHold) { return HalfTimeCaption; }
        if (Time.time < goalCaptionUntil) { return goalCaption; }
        return string.Empty;
    }

    private void ResetShown()
    {
        shownSecond = -1;
        shownHomeGoals = -1;
        shownAwayGoals = -1;
        shownOwnerTeam = -2;
        shownCaption = string.Empty;
        seenHomeGoals = -1;
        seenAwayGoals = -1;
        goalCaption = string.Empty;
        goalCaptionUntil = 0f;
        captionText.text = string.Empty;
    }
}
