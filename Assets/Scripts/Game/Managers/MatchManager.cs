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

    public int Ticks { get; private set; }                       // 킥오프부터 센 고정 스텝 수. float 누적은 종료 틱이 9000/9001로 갈려 러너와 어긋난다(09-27 리뷰)
    public float Elapsed => Ticks * MatchTuning.FixedStep;     // 로그용 초
    public MatchSimulation Simulation { get; private set; }
    public string OpponentName { get; private set; }            // HUD용. 이번 경기 정보는 Match 한 곳에서(참조 1개 규칙)

    // 경기가 끝났음을 알린다(승리 팀 0/1, 무승부 -1). 결과 화면(3주차)과 검증 도구가 구독한다
    public event Action<int> MatchEnded;

    private static readonly BehaviorNode SharedTree = PlayerTreeBuilder.Build();   // 22명이 공유하는 트리 하나(무상태). 4국면(09-18)
    private System.Random rng;
    private int kickoffTeam;   // 일정상 홈이 킥오프(MatchSetup). 골 뒤엔 시뮬이 실점 팀에게 준다(09-23)

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
        MatchSetup setup = GameDataManager.Instance.Season.CurrentMatch;
        Ticks = 0;
        rng = new System.Random(setup.Seed);
        kickoffTeam = setup.KickoffTeam;
        OpponentName = setup.OpponentName;
        Simulation = new MatchSimulation(NextRoll, SharedTree)
        {
            ResetAfterEveryShot = false   // 4국면 트리(09-18): 세이브 뒤 GK가 배급한다. 리트머스 때만 true였다
        };
        Simulation.SetTactics(0, setup.Tactics0);   // 내 프리셋(세이브)·상대 프리셋(생성 팀)
        Simulation.SetTactics(1, setup.Tactics1);
        Simulation.ShotResolved += LogShot;
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

        Simulation.Tick(MatchTuning.FixedStep);   // Unity 설정(Fixed Timestep)이 아니라 코어 상수로 흐른다: 설정이 바뀌어도 같은 시드 = 같은 경기(러너와 동일). 호출 주기만 설정이 정한다
        SyncViews();

        Ticks++;
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
    // MatchEnded(→ 시즌 결과 보고·저장)를 상태 전환보다 먼저 쏜다: 결과 화면이 GameOver 훅에서 승점표를 읽을 때 이번 라운드가 들어가 있어야 한다(09-27 리뷰 Y2)
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
