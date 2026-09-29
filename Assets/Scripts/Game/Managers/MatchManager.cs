using System;
using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.League;
using Game.Core.Match;
using UnityEngine;

// 경기 실행자이자 심판. 고정 스텝마다 순수 MatchSimulation을 한 틱 돌리고 그 결과를 화면(선수·공 뷰)에 비춘다.
// 판단·계산은 전부 순수 코어(MatchSimulation·MatchRules·BallRules) 몫. 여기는 "언제 부르나"와 국면 전환만.
// 09-16 결정: 공 매니저를 따로 두지 않는다(연산이 복사 한 번이고 매니저 3종 세트가 더 비싸다).
public class MatchManager : InGameManager
{
    public const int Draw = -1;                 // EndMatch의 무승부 표식

    [SerializeField] private BallView ballView;   // 씬의 공(구). 순수 BallState를 비춘다
    [SerializeField] private bool autoKickoff = true;   // 임시: 프리셋 선택 화면(플랜 10-01)이 오면 킥오프 버튼으로 바꾸고 지운다
    [SerializeField] private float halfTimeHoldSec = 2f;   // 하프타임 멈춤(연출). 시뮬 틱은 안 돌아 러너·시즌 결과와 무관. 0이면 멈춤 없음
    [SerializeField] private float celebrationSec = 3f;    // 골 세리머니(연출, 09-29 유저 Play "골 넣은 사람 세리머니 시간을 줘야"). 시뮬 틱은 안 돈다. 0이면 없음
    [SerializeField] private float kickoffSetSec = 1f;     // 킥오프 전 제자리 멈춤(연출, 09-29). 경기 시작·골 뒤. 하프타임은 halfTimeHoldSec이 맡는다

    public int Ticks { get; private set; }                       // 킥오프부터 센 고정 스텝 수. float 누적은 종료 틱이 9000/9001로 갈려 러너와 어긋난다(09-27 리뷰)
    public float Elapsed => Ticks * MatchTuning.FixedStep;     // 로그용 초
    public MatchSimulation Simulation { get; private set; }
    public string OpponentName { get; private set; }            // HUD용. 이번 경기 정보는 Match 한 곳에서(참조 1개 규칙)

    // 경기가 끝났음을 알린다(승리 팀 0/1, 무승부 -1). 결과 화면(3주차)과 검증 도구가 구독한다
    public event Action<int> MatchEnded;

    private static readonly BehaviorNode SharedTree = PlayerTreeBuilder.Build();   // 22명이 공유하는 트리 하나(무상태). 4국면(09-18)
    private System.Random rng;
    private int kickoffTeam;   // 일정상 홈이 킥오프(MatchSetup). 골 뒤엔 시뮬이 실점 팀에게 준다(09-23)
    private int holdTicksLeft;   // 멈춤(하프타임·킥오프 준비) 남은 고정 스텝 수
    private bool holdIsHalfTime;
    private int goalScorerId = BallState.NoOwner;   // 이번 틱 골 넣은 선수. ShotResolved에서 받는다
    private readonly GoalCelebration celebration = new GoalCelebration();   // 세리머니 안무. 언제 시작·끝낼지는 여기서 정한다

    public bool InHalfTimeHold => holdTicksLeft > 0 && holdIsHalfTime;   // HUD가 "하프타임" 자막을 띄운다
    public bool InCelebration => celebration.IsPlaying;                   // HUD가 골 자막을 세리머니 내내 유지한다

    public override void Initialize()
    {
        GameManager.AddGameStateEnterAction(GameManager.GameState.GameReady, ResetMatch);   // 판이 새로 차려질 때 시계 0·시뮬 새로
        GameManager.AddGameStateEnterAction(GameManager.GameState.GamePlay, StartMatch);    // 킥오프
    }

    // 틱이 도는지는 플래그가 아니라 상태가 정한다. 일시정지(GameStop)·종료(GameOver/Clear)에서 저절로 멈춘다
    private bool IsRunning => GameManager.CurrentState == GameManager.GameState.GamePlay;

    // 시드·전술·킥오프 팀은 시즌의 이번 경기(MatchSetup)에서(09-27). 같은 시즌·라운드·같은 세팅이면 같은 경기가 재현된다("막히면 재세팅")
    private void ResetMatch()
    {
        // 입구가 어디든(로비·에디터에서 InGameScene 직접 Play) 끝난 시즌이면 여기서 다음 시즌을 연다. 안 끝났으면 아무 일도 안 한다(09-29 발견 A).
        // GameReady 훅은 Match가 Stage보다 먼저라(GameManager 등록 순서) StageManager.Load도 새 시즌을 읽는다
        GameDataManager.Instance.Season.PrepareNextMatch();
        MatchSetup setup = GameDataManager.Instance.Season.CurrentMatch;
        Ticks = 0;
        holdTicksLeft = 0;
        holdIsHalfTime = false;
        goalScorerId = BallState.NoOwner;
        celebration.Stop();
        rng = new System.Random(setup.Seed);
        kickoffTeam = setup.KickoffTeam;
        OpponentName = setup.OpponentName;
        Simulation = new MatchSimulation(NextRoll, SharedTree)
        {
            ResetAfterEveryShot = false   // 4국면 트리(09-18): 세이브 뒤 GK가 배급한다. 리트머스 때만 true였다
        };
        Simulation.SetAddedTime(AddedTime.FromSeed(setup.Seed));   // 러너(MatchAssembler)와 같은 식
        Simulation.SetTactics(0, setup.Tactics0);   // 내 프리셋(세이브)·상대 프리셋(생성 팀)
        Simulation.SetTactics(1, setup.Tactics1);
        Simulation.ShotResolved += LogShot;
        Simulation.ShotResolved += OnShotResolved;
        Simulation.PossessionChanged += LogPossession;
    }

    private float NextRoll()
    {
        return (float)rng.NextDouble();
    }

    // StageManager가 스폰한 선수를 시뮬 명부에 올린다(GameReady 훅 순서: Match 리셋 → Stage 스폰)
    public void Register(PlayerState player)
    {
        Simulation.AddPlayer(player);
    }

    private void StartMatch()
    {
        Simulation.KickoffBy(kickoffTeam);
        SnapViews();   // 킥오프 자리로 순간이동(보간하면 전 자리에서 미끄러져 온다)
        Hold(kickoffSetSec, halfTime: false);
    }

    #region 심장: 고정 스텝 틱
    // FixedUpdate = 고정 스텝(기본 0.02s). 기기가 달라도 틱 수·순서가 같아야 결과가 같다
    private void FixedUpdate()
    {
        if (Simulation == null) { return; }   // GameReady 훅이 중간에 끊겨 시뮬이 없으면(예: 시즌 종료 상태 진입) 매 스텝 NRE 대신 조용히 멈춘다
        if (autoKickoff && GameManager.CurrentState == GameManager.GameState.GameReady && Simulation.Players.Count > 0)
        {
            GameManager.SetGameState(GameManager.GameState.GamePlay);   // GameReady 진입 훅 체인 밖(다음 고정 스텝)에서 전환
            return;
        }
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
            return;
        }
        if (Ticks >= MatchTuning.MatchTicks)
        {
            EndMatch(WinnerByGoals());   // 마지막 틱 골: 세리머니를 본 뒤에 끝낸다
            return;
        }

        bool halfTimeTick = Ticks + 1 == MatchClock.HalfTimeTick(Simulation.Added);
        goalScorerId = BallState.NoOwner;
        Simulation.Tick(MatchTuning.FixedStep);   // Unity 설정(Fixed Timestep)이 아니라 코어 상수로 흐른다: 설정이 바뀌어도 같은 시드 = 같은 경기(러너와 동일). 호출 주기만 설정이 정한다
        Ticks++;

        if (goalScorerId != BallState.NoOwner)
        {
            // 시뮬은 이 틱에 이미 킥오프 자리로 리셋했다. 화면은 골 순간에 머물고 세리머니 뒤에 킥오프 자리로 스냅한다
            BeginCelebration();
            return;
        }
        SyncViews();

        if (halfTimeTick)
        {
            // 이 틱 안에서 시뮬이 진영 교체 + 후반 킥오프를 끝냈다(09-27). 뷰를 새 자리에 바로 세우고(보간하면 22명이 반대편으로 미끄러진다) 잠시 멈춘다
            SnapViews();
            Hold(halfTimeHoldSec, halfTime: true);
        }
        if (Ticks >= MatchTuning.MatchTicks)   // 러너(MatchProbe.Run)와 같은 틱 수에서 끝난다
        {
            EndMatch(WinnerByGoals());
        }
    }
    #endregion

    private void SyncViews()
    {
        for (int team = 0; team < 2; team++)
        {
            IReadOnlyList<PlayerController> roster = GameManager.Players.Roster(team);
            for (int i = 0; i < roster.Count; i++)
            {
                roster[i].SyncView();
            }
        }

        if (ballView != null)
        {
            ballView.Apply(Simulation.Ball);
        }
    }

    private void Hold(float seconds, bool halfTime)
    {
        holdTicksLeft = Mathf.RoundToInt(seconds / MatchTuning.FixedStep);
        holdIsHalfTime = halfTime;
    }

    private void OnShotResolved(ShotReport report)
    {
        if (report.Outcome == ShotOutcome.Goal) { goalScorerId = report.ShooterId; }
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
        if (Ticks < MatchTuning.MatchTicks) { Hold(kickoffSetSec, halfTime: false); }
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
            ballView.Snap(Simulation.Ball);
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
        MatchEnded?.Invoke(winnerTeam);
        GameManager.SetGameState(winnerTeam == 0 ? GameManager.GameState.GameClear : GameManager.GameState.GameOver);
    }

    public override void Clear()
    {
        GameManager.RemoveGameStateEnterAction(GameManager.GameState.GameReady, ResetMatch);
        GameManager.RemoveGameStateEnterAction(GameManager.GameState.GamePlay, StartMatch);
        MatchEnded = null;   // 씬이 내려가면 구독자도 같이 사라진다. 죽은 구독자를 들고 있지 않게
    }
}
