using System;
using System.Collections.Generic;
using System.Threading;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Viewing;
using UnityEngine;

// 경기 진행을 맡는 매니저. 고정 스텝마다 순수 MatchSimulation을 한 틱 돌리고 그 결과를 화면(선수·공 뷰)에 비춘다.
// 판단·계산은 전부 순수 코어(MatchSimulation·MatchRules·BallRules) 몫. 여기는 "언제 부르나"와 국면 전환만.
// 09-16 결정: 공 매니저를 따로 두지 않는다(연산이 복사 한 번이고 매니저 3종 세트가 더 비싸다).
public class MatchManager : InGameManager
{
    public const int Draw = -1;                 // EndMatch의 무승부 표식

    [SerializeField] private BallView ballView;   // 씬의 공(구). 순수 BallState를 비춘다
    [SerializeField] private float halfTimeHoldSec = 2f;   // 하프타임 멈춤(연출). 시뮬 틱은 안 돌아 러너·시즌 결과와 무관. 0이면 멈춤 없음
    [SerializeField] private float celebrationSec = 3f;    // 골 세리머니(연출, 09-29 유저 Play "골 넣은 사람 세리머니 시간을 줘야"). 시뮬 틱은 안 돈다. 0이면 없음
    [SerializeField] private float kickoffSetSec = 1f;     // 킥오프 전 제자리 멈춤(연출, 09-29). 경기 시작·골 뒤. 하프타임은 halfTimeHoldSec이 맡는다
    [SerializeField] private float goalKickHoldSec = 1f;   // 빗나간 슛 뒤 골킥 전 멈춤(연출, 10-23). 공이 골라인 근처에 멈춰 보이다 GK 발로 스냅한다
    [SerializeField] private int skipTicksPerStep = 1000;  // 하이라이트 사이를 건너뛸 때 고정 스텝 한 번에 돌리는 틱 수(S4). 체감만 바꾸고 결과는 안 바꾼다 [가정: 실제 90분 기준 20초분]

    public int Ticks { get; private set; }                       // 킥오프부터 센 고정 스텝 수. float 누적은 종료 틱이 TotalTicks와 그 다음 틱으로 갈려 러너와 어긋난다
    public float Elapsed => Ticks * MatchTuning.FixedStep;     // 로그용 초
    public MatchSimulation Simulation { get; private set; }
    public string OpponentName { get; private set; }            // HUD용. 이번 경기 정보는 Match 한 곳에서(참조 1개 규칙)
    public TacticsReadout MyReadout => myProbe.Readout;         // 결과 화면용. 내 팀이 이번 경기에 실제로 한 것

    // 경기가 끝났음을 알린다(승리 팀 0/1, 무승부 -1). 결과 화면(3주차)과 검증 도구가 구독한다
    public event Action<int> MatchEnded;

    private int holdTicksLeft;   // 멈춤(하프타임·킥오프 준비) 남은 고정 스텝 수
    private bool holdIsHalfTime;
    private int goalScorerId = BallState.NoOwner;   // 이번 틱 골 넣은 선수. ShotResolved에서 받는다
    private bool shotMissedThisTick;   // 이번 틱 빗나간 슛. 시뮬은 같은 틱에 골킥(GK 발)으로 옮기므로 화면은 잠깐 멈췄다 스냅한다
    private bool snapAfterHold;        // 멈춤이 끝나면 뷰를 시뮬 자리로 스냅(멈춤 동안 시뮬 자리가 바뀐 경우)
    private TacticsProbe myProbe;   // 내 팀의 전술 실측. 결과 화면이 설정과 나란히 보여 준다
    private readonly GoalCelebration celebration = new GoalCelebration();   // 세리머니 안무. 언제 시작·끝낼지는 여기서 정한다
    private bool celebrationBeforeHalfTime;   // 이번 세리머니가 하프타임 틱의 골인가. 끝나면 킥오프 준비 대신 하프타임 멈춤
    private ViewingMode viewingMode;          // 지금 보기 모드(S4). 경기 시작 때 설정에서 읽고, 경기 중에는 SetViewingMode가 바꾼다
    private int speed;                        // 지금 배속(×1~×4, 모든 모드). 경기 시작 때 설정에서 읽고 SetSpeed가 바꾼다
    private HighlightSet highlights;          // 1차 계산이 만든 두 모드의 구간. null이면 아직 계산 중이거나 실패
    private ReplayPlan replayPlan;            // 지금 모드의 구간 커서. null이면 전부 보여 준다(전체 경기, 1차 계산 전·실패)
    private bool skipping;                    // 구간 밖을 화면 없이 돌리는 중

    public bool InHalfTimeHold => holdTicksLeft > 0 && holdIsHalfTime;   // HUD가 "하프타임" 자막을 띄운다
    public bool InCelebration => celebration.IsPlaying;                   // HUD가 골 자막을 세리머니 내내 유지한다
    public bool IsSkipping => skipping;                                   // HUD가 "다음 장면" 자막을 띄운다
    public ViewingMode ViewingMode => viewingMode;                        // 보기 패널이 지금 칸을 그린다
    public int Speed => speed;

    // 건너뛰기가 닿을 틱(다음 하이라이트 시작, 없으면 경기 끝). 건너뛰는 중이 아니면 지금 틱
    public int SkipTargetTick
    {
        get
        {
            if (!skipping || replayPlan == null) { return Ticks; }
            int next = replayPlan.NextShownTick(Ticks);
            if (next == ReplayPlan.NoMoreShown) { return Simulation.TotalTicks; }
            return next;
        }
    }

    public override void Initialize()
    {
        GameManager.AddGameStateEnterAction(GameManager.GameState.GameReady, ResetMatch);   // 판이 새로 차려질 때 시계 0·시뮬 새로
        GameManager.AddGameStateEnterAction(GameManager.GameState.GamePlay, StartMatch);    // 킥오프
        GameManager.AddGameStateExitAction(GameManager.GameState.GameStop, ApplySpeedAfterPause);   // 일시정지 중 바꾼 배속을 복귀 때 적용
    }

    // 틱이 도는지는 플래그가 아니라 상태가 정한다. 일시정지(GameStop)·종료(GameOver/Clear)에서 저절로 멈춘다
    private bool IsRunning => GameManager.CurrentState == GameManager.GameState.GamePlay;

    // 시뮬은 시즌의 이번 경기(MatchSetup)를 MyMatchAssembler에 넘겨 만든다(선수 22명·전술·시드·추가시간·킥오프까지). 치트 헤드리스·테스트와 같은 함수라
    // 같은 시즌·라운드·같은 세팅이면 같은 경기가 재현된다("막히면 재세팅", 11-06 S4-1). 선수 뷰는 이 시뮬의 Players를 비춘다(StageManager가 GameReady 다음 훅에서 스폰)
    private void ResetMatch()
    {
        // 입구가 어디든(로비·에디터에서 InGameScene 직접 Play) 끝난 시즌이면 여기서 다음 시즌을 연다. 안 끝났으면 아무 일도 안 한다(09-29 발견 A).
        // GameReady 훅은 Match가 Stage보다 먼저라(GameManager 등록 순서) StageManager.Load도 새 시즌을 읽는다
        GameDataManager.Instance.Season.PrepareNextMatch();
        MatchSetup setup = GameDataManager.Instance.Season.CurrentMatch;
        Ticks = 0;
        holdTicksLeft = 0;
        holdIsHalfTime = false;
        snapAfterHold = false;
        goalScorerId = BallState.NoOwner;
        celebrationBeforeHalfTime = false;
        celebration.Stop();
        OpponentName = setup.OpponentName;
        Simulation = MyMatchAssembler.Create(setup, ThreatTableRepository.Grid, DevMatchLength.Current);
        myProbe = new TacticsProbe(Simulation, 0);   // 내 팀 = 시뮬 팀 0
        Simulation.ShotResolved += LogShot;
        Simulation.ShotResolved += OnShotResolved;
        Simulation.PossessionChanged += LogPossession;
        Simulation.BallKicked += OnBallKicked;

        highlights = null;
        replayPlan = null;
        skipping = false;
        viewingMode = ViewingModeSetting.Mode;
        speed = ViewingModeSetting.Speed;
        _ = PrepareHighlightsAsync(setup, Simulation.Length);
    }

    // 하이라이트 1차 계산(S4, 설계 문서 2절 A안). 같은 조립으로 시뮬을 하나 더 만들어 뒤 스레드에서 끝까지 돌리고 두 모드의 구간을 만든다.
    // 전체 경기 모드여도 한다: 경기 중에 주요 장면·하이라이트로 바꿀 수 있다(11-19 유저 Play 피드백).
    // 그동안 GameReady를 붙잡아 둔다(SkipTitle 진입을 미룸). 재생할 시뮬은 GamePlay 전엔 Tick을 안 돌아 두 시뮬이 동시에 돌지 않는다.
    // 실패하면 구간 없이(전체 경기처럼) 재생한다. 결과는 같은 경기라 모드와 무관하다
    private async Awaitable PrepareHighlightsAsync(MatchSetup setup, MatchLength length)
    {
        // await 뒤엔 이 오브젝트가 이미 파괴됐을 수 있어 토큰을 먼저 받아 둔다(Unity 문서 MonoBehaviour.destroyCancellationToken: 파괴 전에 캐시해야 한다)
        CancellationToken token = destroyCancellationToken;
        GameManager.HoldReady();
        MatchSimulation firstPass = MyMatchAssembler.Create(setup, ThreatTableRepository.Grid, length);
        HighlightSet planned = null;
        try
        {
            // 시뮬은 순수 C#이라 스레드에서 돌려도 된다(SeasonSystem.ReportMyResultAsync와 같은 근거). 이 구간엔 Debug.Log도 부르지 않는다
            await Awaitable.BackgroundThreadAsync();
            planned = HighlightPlanner.Plan(firstPass);
            await Awaitable.MainThreadAsync();
        }
        catch (Exception e)
        {
            await Awaitable.MainThreadAsync();
            Debug.LogError($"[Match] 하이라이트 계산 실패. 전체 경기로 재생한다: {e}");
        }

        if (token.IsCancellationRequested) { return; }   // 계산 중 씬을 떠났다(Play 종료·재시작)
        if (planned != null)
        {
            highlights = planned;
            Debug.Log($"[Match] 하이라이트 계산: 주요 장면 구간 {planned.Key.Count}개, 하이라이트 구간 {planned.Extended.Count}개");
            ApplyViewingMode();   // 계산 중에 모드를 바꿨어도 지금 모드로 고른다
        }
        GameManager.ReleaseReady();
    }

    // 보기 모드 바꾸기(경기 중 보기 패널, 11-19). 1차 계산이 만들어 둔 그 모드의 구간으로 커서만 바꾼다: 시뮬은 그대로라 결과가 같다
    public void SetViewingMode(ViewingMode mode)
    {
        ViewingMode sanitized = ViewingOptions.SanitizeMode((int)mode);
        ViewingModeSetting.SetMode(sanitized);
        if (sanitized == viewingMode) { return; }
        viewingMode = sanitized;
        ApplyViewingMode();
    }

    // 배속 바꾸기(×1~×4, 모든 모드). 경기 중이면 바로 timeScale에 넣는다. 일시정지 중이면 복귀 훅(ApplySpeedAfterPause)이, 킥오프 전이면 StartMatch가 넣는다
    public void SetSpeed(int value)
    {
        speed = ViewingOptions.SanitizeSpeed(value);
        ViewingModeSetting.SetSpeed(speed);
        if (!IsRunning) { return; }
        Time.timeScale = speed;
    }

    // GameStop 퇴장 훅. GameManager가 멈추기 전 timeScale을 먼저 되돌린 뒤라(훅 등록 순서) 그 위에 지금 배속을 덮는다. 경기로 돌아갈 때만
    private void ApplySpeedAfterPause()
    {
        if (!IsRunning) { return; }
        Time.timeScale = speed;
    }

    // 지금 모드의 구간으로 커서를 새로 만든다. 커서는 0부터 지금 틱까지 저절로 따라온다(ReplayPlan.Advance).
    // 건너뛰는 중이었으면 화면을 지금 시뮬 자리로 세운다: 새 모드가 이 틱을 보여 주면 다음 스텝부터 그대로 이어지고, 아니면 다음 스텝이 다시 건너뛰기를 시작한다
    private void ApplyViewingMode()
    {
        replayPlan = null;
        if (highlights != null)
        {
            IReadOnlyList<TickRange> ranges = highlights.RangesFor(viewingMode);
            if (ranges != null) { replayPlan = new ReplayPlan(ranges); }
        }
        if (!skipping) { return; }
        skipping = false;
        SnapViews();
    }

    // 찬 선수의 킥 애니. 시뮬은 안 기다린다(연출)
    private void OnBallKicked(KickReport report)
    {
        if (skipping) { return; }   // 건너뛰는 동안 뷰는 멈춰 있다
        PlayerController player = GameManager.Players.Find(report.KickerId);
        if (player == null) { return; }
        var direction = new Vector3(report.DirX, 0f, report.DirZ);
        if (report.IsThrowIn)
        {
            player.PlayThrow(direction);
            if (ballView != null)
            {
                ballView.StartThrowRelease();
            }
            return;
        }
        player.PlayKick(direction, report.IsShot);
    }

    // 전술·개인 지시·킥오프는 ResetMatch의 조립에서 이미 들어갔다(11-06 S4-1: 킥오프 순간에 다시 넣던 SetTactics·개인 지시 재대입·KickoffBy를 지웠다).
    // 10-13부터 편집 화면은 로비에만 있어 GameReady와 GamePlay 사이에 재료가 바뀔 길이 없다(09-30 확인). GamePlay 진입 훅은 경기당 한 번(일시정지 복귀는 훅을 다시 안 쏜다)
    // 배속(×1~×4, 모든 모드)은 timeScale로 준다. FixedUpdate 빈도가 실시간 기준으로 늘 뿐 틱 수·순서는 그대로라 결과가 같다(Unity 문서 Time.timeScale)
    private void StartMatch()
    {
        SnapViews();   // 킥오프 자리로 순간이동(보간하면 전 자리에서 미끄러져 온다)
        Hold(kickoffSetSec, halfTime: false);
        Time.timeScale = speed;
    }

    #region 심장: 고정 스텝 틱
    // FixedUpdate = 고정 스텝(기본 0.02s). 기기가 달라도 틱 수·순서가 같아야 결과가 같다
    private void FixedUpdate()
    {
        if (Simulation == null) { return; }   // GameReady 훅이 중간에 끊겨 시뮬이 없으면(예: 시즌 종료 상태 진입) 매 스텝 NRE 대신 조용히 멈춘다
        if (!IsRunning) { return; }

        // 연출 멈춤(세리머니 → 킥오프 준비·하프타임): 시뮬을 안 돌린다. 일시정지(GameStop)면 위에서 이미 멈춘다
        if (celebration.IsPlaying)
        {
            celebration.Step(MatchTuning.FixedStep);
            if (!celebration.IsPlaying) { EndCelebration(); }
            return;
        }
        if (holdTicksLeft > 0)
        {
            holdTicksLeft--;
            if (holdTicksLeft == 0 && snapAfterHold) { EndHoldWithSnap(); }
            return;
        }
        if (Ticks >= Simulation.TotalTicks)
        {
            EndMatch(WinnerByGoals());   // 마지막 틱 골: 세리머니를 본 뒤에 끝낸다
            return;
        }
        if (SkipToNextHighlight()) { return; }

        goalScorerId = BallState.NoOwner;
        shotMissedThisTick = false;
        bool halfTimeTick = StepSimulation();

        if (goalScorerId != BallState.NoOwner)
        {
            // 시뮬은 이 틱에 이미 킥오프 자리로 리셋했다. 화면은 골 순간에 머물고 세리머니 뒤에 킥오프 자리로 스냅한다.
            // 하프타임 틱의 골(휘슬 순간 날아가던 슛, 09-30)이면 세리머니 뒤에 하프타임 멈춤을 이어 준다: 그냥 두면 킥오프 준비 1초로 끝나 "하프타임" 자막이 안 뜬다
            celebrationBeforeHalfTime = halfTimeTick;
            BeginCelebration();
            return;
        }
        if (shotMissedThisTick && goalKickHoldSec > 0f && !halfTimeTick && Ticks < Simulation.TotalTicks)
        {
            // 빗나간 슛: 시뮬은 이 틱에 공을 GK 발(골킥)로 옮겼다. 화면은 직전 자리(골라인 근처)에 잠깐 멈췄다가 스냅한다(10-23 유저 "공이 GK에게 순간이동")
            HoldAllViews();
            snapAfterHold = true;
            Hold(goalKickHoldSec, halfTime: false);
            return;
        }
        SyncViews();

        if (halfTimeTick)
        {
            // 이 틱 안에서 시뮬이 진영 교체 + 후반 킥오프를 끝냈다. 뷰를 새 자리에 바로 세우고(보간하면 22명이 반대편으로 미끄러진다) 잠시 멈춘다
            SnapViews();
            Hold(halfTimeHoldSec, halfTime: true);
        }
        if (Ticks >= Simulation.TotalTicks)   // 러너(MatchProbe.Run)와 같은 틱 수에서 끝난다
        {
            EndMatch(WinnerByGoals());
        }
    }
    #endregion

    // 하이라이트 구간 밖이면 이번 스텝에 예산만큼 화면 없이 돌린다(S4). 시뮬 Tick의 횟수·순서는 보이는 틱과 같아 결과가 같다(HighlightReplayTests).
    // 하프타임 틱을 지나면 그 자리에서 멈추고 하프타임 자막을 띄운다(설계 7절 결정 5). 다음 구간에 닿으면 뷰를 새 자리로 스냅한다
    private bool SkipToNextHighlight()
    {
        if (replayPlan == null) { return false; }
        // 배속이 N이면 FixedUpdate가 실시간 1초에 N배 불린다. 예산을 배속으로 나눠 실시간 1초당 건너뛰는 계산량(부하)을 배속과 무관하게 같게 둔다.
        // 결과는 예산과 무관하다(HighlightReplayTests가 예산 1·1,000·100,000을 대조)
        int budget = Mathf.Max(1, skipTicksPerStep / speed);
        int skip = replayPlan.TicksToSkip(Ticks, Simulation.TotalTicks, budget);
        if (skip == 0) { return false; }

        if (!skipping)
        {
            skipping = true;
            HoldAllViews();
        }
        for (int i = 0; i < skip; i++)
        {
            bool halfTimeTick = StepSimulation();
            if (halfTimeTick)
            {
                SnapViews();
                Hold(halfTimeHoldSec, halfTime: true);
                break;
            }
        }

        if (Ticks >= Simulation.TotalTicks || replayPlan.IsShown(Ticks))
        {
            skipping = false;
            SnapViews();
        }
        return true;
    }

    // 시뮬 한 틱. 보이는 틱(FixedUpdate)과 건너뛰는 틱(SkipToNextHighlight)이 같은 순서로 부른다: 두 모드의 결과가 같은 전제다(HighlightReplayTests).
    // 이번 틱이 하프타임 틱이었는지 돌려준다. 전제: Ticks와 시뮬 내부 tickCount가 늘 같다(Simulation.Tick을 부르는 곳은 여기 한 곳, 09-30 리뷰)
    private bool StepSimulation()
    {
        bool halfTimeTick = Ticks + 1 == Simulation.HalfTimeTick;
        Simulation.Tick(MatchTuning.FixedStep);   // Unity 설정(Fixed Timestep)이 아니라 코어 상수로 흐른다: 설정이 바뀌어도 같은 시드 = 같은 경기(러너와 동일). 호출 주기만 설정이 정한다
        myProbe.Sample();   // 건너뛰는 틱에도 잰다: 결과 화면 실측이 전 경기 값이어야 전체 경기 모드와 같다
        Ticks++;
        return halfTimeTick;
    }

    private void SyncViews()
    {
        for (int team = 0; team < 2; team++)
        {
            IReadOnlyList<PlayerController> roster = GameManager.Players.Roster(team);
            for (int i = 0; i < roster.Count; i++)
            {
                roster[i].SyncView();
                if (roster[i].State.IncomingShot) { roster[i].FaceBall(new Vector3(Simulation.Ball.X, 0f, Simulation.Ball.Z)); }   // 옆으로 뛰면서도 공을 본다
            }
        }

        if (ballView != null)
        {
            ballView.Apply(Simulation.Ball, CarrierForward(), ThrowInHolder());
        }
    }

    // 스로인 공을 들고 기다리는 선수. 공을 발 앞이 아니라 그 선수 손에 그린다. 없거나 손 뼈가 없는 몸이면 null(발 앞에 그대로 그린다)
    private PlayerController ThrowInHolder()
    {
        if (Simulation.Ball.Phase != BallPhase.Owned) { return null; }
        PlayerController owner = FindView(Simulation.Ball.OwnerId);
        if (owner == null) { return null; }
        if (!owner.State.IsThrowInTaker) { return null; }
        if (!owner.HasHands) { return null; }
        return owner;
    }

    private void Hold(float seconds, bool halfTime)
    {
        holdTicksLeft = Mathf.RoundToInt(seconds / MatchTuning.FixedStep);
        holdIsHalfTime = halfTime;
    }

    private void EndHoldWithSnap()
    {
        snapAfterHold = false;
        SnapViews();
    }

    private void OnShotResolved(ShotReport report)
    {
        if (report.Outcome == ShotOutcome.Goal) { goalScorerId = report.ShooterId; }
        if (report.Outcome == ShotOutcome.Missed) { shotMissedThisTick = true; }
        PlayKeeperSave(report);
    }

    // 세이브 애니(연출). 잡으면 캐치, 쳐내면 펀칭. 슈터의 상대 팀 GK. 다이빙은 컨트롤러가 반응 시작 때 스스로 튼다(그동안 캐치·펀칭은 버려진다)
    private void PlayKeeperSave(ShotReport report)
    {
        if (skipping) { return; }
        if (report.Outcome != ShotOutcome.Caught && report.Outcome != ShotOutcome.Parried) { return; }
        PlayerController shooter = FindView(report.ShooterId);
        if (shooter == null) { return; }
        PlayerController keeper = KeeperView(1 - shooter.Team);
        if (keeper == null) { return; }
        if (report.Outcome == ShotOutcome.Caught)
        {
            keeper.PlayCatch();
            return;
        }
        keeper.PlayParry();
    }

    private PlayerController KeeperView(int team)
    {
        IReadOnlyList<PlayerController> roster = GameManager.Players.Roster(team);
        for (int i = 0; i < roster.Count; i++)
        {
            if (roster[i].State.IsGoalkeeper) { return roster[i]; }
        }
        return null;
    }

    #region 골 세리머니(연출, 09-29)
    // 골 틱에 화면을 멈추고 안무(GoalCelebration)를 시작한다. 끝나면 킥오프 자리로 스냅하고 킥오프 준비 멈춤
    private void BeginCelebration()
    {
        PlayerController scorer = FindView(goalScorerId);
        int ticks = Mathf.RoundToInt(celebrationSec / MatchTuning.FixedStep);
        if (scorer == null || ticks <= 0)
        {
            EndCelebration();
            return;
        }

        HoldAllViews();
        celebration.Begin(scorer, GameManager.Players.Roster(scorer.Team), ticks);
    }

    private void EndCelebration()
    {
        SnapViews();
        if (Ticks >= Simulation.TotalTicks) { return; }
        if (celebrationBeforeHalfTime)
        {
            Hold(halfTimeHoldSec, halfTime: true);
            return;
        }
        Hold(kickoffSetSec, halfTime: false);
    }

    private void HoldAllViews()
    {
        for (int team = 0; team < 2; team++)
        {
            IReadOnlyList<PlayerController> roster = GameManager.Players.Roster(team);
            for (int i = 0; i < roster.Count; i++)
            {
                roster[i].HoldView();
            }
        }

        if (ballView != null)
        {
            ballView.Hold();
        }
    }

    // 소유 중인 공을 발 앞에 그리려고 소유자 뷰가 보는 방향을 준다. 자유 공·비행 중·뷰 없음이면 0
    private Vector3 CarrierForward()
    {
        if (Simulation.Ball.Phase != BallPhase.Owned) { return Vector3.zero; }
        PlayerController owner = FindView(Simulation.Ball.OwnerId);
        if (owner == null) { return Vector3.zero; }
        Vector3 forward = owner.transform.forward;
        forward.y = 0f;
        return forward.normalized;
    }

    private PlayerController FindView(int playerId)
    {
        for (int team = 0; team < 2; team++)
        {
            IReadOnlyList<PlayerController> roster = GameManager.Players.Roster(team);
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].PlayerId == playerId) { return roster[i]; }
            }
        }
        return null;
    }
    #endregion

    // 시뮬 값을 뷰에 바로 세운다(보간 없음). 배치·킥오프용
    private void SnapViews()
    {
        for (int team = 0; team < 2; team++)
        {
            IReadOnlyList<PlayerController> roster = GameManager.Players.Roster(team);
            for (int i = 0; i < roster.Count; i++)
            {
                roster[i].SnapView();
            }
        }

        if (ballView != null)
        {
            ballView.Snap(Simulation.Ball, CarrierForward(), ThrowInHolder());
        }
    }

    private int WinnerByGoals()
    {
        if (Simulation.HomeGoals > Simulation.AwayGoals) { return 0; }
        if (Simulation.AwayGoals > Simulation.HomeGoals) { return 1; }
        return Draw;
    }

    // 경합·패스·가로채기가 실제로 나는지 보는 로그. 밸런스 잡을 때 끈다
    private void LogPossession(PossessionReport r)
    {
        string prev = r.PreviousOwnerId == BallState.NoOwner ? "자유공" : $"#{r.PreviousOwnerId}";
        Debug.Log($"[Match] {r.Kind} {prev} → #{r.NewOwnerId}(팀{r.NewOwnerTeam}) at ({r.X:0.0},{r.Z:0.0})  t={Elapsed:0.0}s");
    }

    private void LogShot(ShotReport report)
    {
        Debug.Log($"[Match] 슛 #{report.ShooterId} p={report.Probability:0.000} → {report.Outcome}  ({Simulation.HomeGoals}:{Simulation.AwayGoals})");
    }

    // 승리 팀 0 → GameClear, 그 외(패배·무승부) → GameOver. 스테이지 클리어는 승리만(스펙 §7).
    // MatchEnded(→ 시즌 결과 보고 시작)를 상태 전환보다 먼저 쏜다: 보고가 IsReporting을 먼저 세워야 결과 화면이 처음부터 버튼을 잠근다(09-28 G1. 동기 보고였던 09-27 Y2엔 "승점표가 이미 완성돼 있어야"가 이유였다)
    public void EndMatch(int winnerTeam)
    {
        Debug.Log($"[Match] 경기 종료. 승리 팀: {winnerTeam}  ({Simulation.HomeGoals}:{Simulation.AwayGoals})");
        Time.timeScale = 1f;   // 배속은 경기 동안만. 결과 화면은 제 속도로
        MatchEnded?.Invoke(winnerTeam);
        GameManager.SetGameState(winnerTeam == 0 ? GameManager.GameState.GameClear : GameManager.GameState.GameOver);
    }

    public override void Clear()
    {
        GameManager.RemoveGameStateEnterAction(GameManager.GameState.GameReady, ResetMatch);
        GameManager.RemoveGameStateEnterAction(GameManager.GameState.GamePlay, StartMatch);
        GameManager.RemoveGameStateExitAction(GameManager.GameState.GameStop, ApplySpeedAfterPause);
        MatchEnded = null;   // 씬이 내려가면 구독자도 같이 사라진다. 죽은 구독자를 들고 있지 않게
    }
}
