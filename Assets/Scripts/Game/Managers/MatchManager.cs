using System;
using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Match;
using UnityEngine;

// 경기 실행자이자 심판. 고정 스텝마다 순수 MatchSimulation을 한 틱 돌리고 그 결과를 화면(선수·공 뷰)에 비춘다.
// 판단·계산은 전부 순수 코어(MatchSimulation·MatchRules·BallRules) 몫. 여기는 "언제 부르나"와 국면 전환만.
// 09-16 결정: 공 매니저를 따로 두지 않는다(연산이 복사 한 번이고 매니저 3종 세트가 더 비싸다).
public class MatchManager : InGameManager
{
    public const float MatchLengthSec = 180f;   // 스펙 §0: 실시간 3분 = 게임 내 90분
    public const int Draw = -1;                 // EndMatch의 무승부 표식

    [SerializeField] private BallView ballView;   // 씬의 공(구). 순수 BallState를 비춘다
    [SerializeField] private bool autoKickoff = true;   // 1주차 임시: 선수 세팅 UI(2주차 배치 UI)가 없어 스폰이 끝나면 바로 킥오프한다. UI가 붙으면 지운다

    public float Elapsed { get; private set; }
    public MatchSimulation Simulation { get; private set; }

    // 경기가 끝났음을 알린다(승리 팀 0/1, 무승부 -1). 결과 화면(3주차)과 검증 도구가 구독한다
    public event Action<int> MatchEnded;

    private static readonly BehaviorNode SharedTree = PlayerTreeBuilder.BuildLitmus();   // 22명이 공유하는 트리 하나(무상태)
    private System.Random rng;

    public override void Initialize()
    {
        GameManager.AddGameStateEnterAction(GameManager.GameState.GameReady, ResetMatch);   // 판이 새로 차려질 때 시계 0·시뮬 새로
        GameManager.AddGameStateEnterAction(GameManager.GameState.GamePlay, StartMatch);    // 킥오프
    }

    // 틱이 도는지는 플래그가 아니라 상태가 정한다. 일시정지(GameStop)·종료(GameOver/Clear)에서 저절로 멈춘다
    private bool IsRunning => GameManager.CurrentState == GameManager.GameState.GamePlay;

    // 시드 = 스테이지 번호(09-16 결정). 같은 스테이지·같은 세팅이면 같은 경기가 재현된다
    private void ResetMatch()
    {
        Elapsed = 0f;
        rng = new System.Random(GameManager.Stage.StageNumber);
        Simulation = new MatchSimulation(NextRoll, SharedTree)
        {
            ResetAfterEveryShot = true   // 1주차 리트머스: 슛마다 킥오프로 되돌린다. 2주차 GK 배급이 생기면 끈다
        };
        Simulation.ShotResolved += LogShot;
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
        Simulation.Kickoff();
        SyncViews();
    }

    #region 심장: 고정 스텝 틱
    // FixedUpdate = 고정 스텝(기본 0.02s). 기기가 달라도 틱 수·순서가 같아야 결과가 같다
    private void FixedUpdate()
    {
        if (autoKickoff && GameManager.CurrentState == GameManager.GameState.GameReady && Simulation.Players.Count > 0)
        {
            GameManager.SetGameState(GameManager.GameState.GamePlay);   // GameReady 진입 훅 체인 밖(다음 고정 스텝)에서 전환
            return;
        }
        if (!IsRunning) { return; }

        Simulation.Tick(Time.fixedDeltaTime);
        SyncViews();

        Elapsed += Time.fixedDeltaTime;
        if (Elapsed >= MatchLengthSec)
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

    private int WinnerByGoals()
    {
        if (Simulation.HomeGoals > Simulation.AwayGoals) { return 0; }
        if (Simulation.AwayGoals > Simulation.HomeGoals) { return 1; }
        return Draw;
    }

    private void LogShot(ShotReport report)
    {
        Debug.Log($"[Match] 슛 #{report.ShooterId} p={report.Probability:0.000} → {report.Outcome}  ({Simulation.HomeGoals}:{Simulation.AwayGoals})");
    }

    // 승리 팀 0 → GameClear, 그 외(패배·무승부) → GameOver. 스테이지 클리어는 승리만(스펙 §7)
    public void EndMatch(int winnerTeam)
    {
        GameManager.SetGameState(winnerTeam == 0 ? GameManager.GameState.GameClear : GameManager.GameState.GameOver);
        Debug.Log($"[Match] 경기 종료. 승리 팀: {winnerTeam}  ({Simulation.HomeGoals}:{Simulation.AwayGoals})");

        if (MatchEnded != null)
        {
            MatchEnded(winnerTeam);
        }
    }

    public override void Clear()
    {
        GameManager.RemoveGameStateEnterAction(GameManager.GameState.GameReady, ResetMatch);
        GameManager.RemoveGameStateEnterAction(GameManager.GameState.GamePlay, StartMatch);
        MatchEnded = null;   // 씬이 내려가면 구독자도 같이 사라진다. 죽은 구독자를 들고 있지 않게
    }
}
