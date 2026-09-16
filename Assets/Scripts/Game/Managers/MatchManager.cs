using System;
using UnityEngine;

// 경기 실행자이자 심판. 고정 스텝으로 경기 시간을 세고, 끝나면 국면을 바꾼다.
// 판단·계산은 순수 코어(MatchRules·TargetSelector, 2주차 트리) 몫. 여기는 "언제 부르나"만.
// 1차 이식(09-16): 틱 골격과 종료만. 선수 틱(sharedTree.Tick)과 공은 공·소유 설계 뒤 #region 심장에 들어간다.
public class MatchManager : InGameManager
{
    public const float MatchLengthSec = 180f;   // 스펙 §0: 실시간 3분 = 게임 내 90분
    public const int Draw = -1;                 // EndMatch의 무승부 표식

    public float Elapsed { get; private set; }

    // 경기가 끝났음을 알린다(승리 팀 0/1, 무승부 -1). 결과 화면(3주차)과 검증 도구가 구독한다
    public event Action<int> MatchEnded;

    public override void Initialize()
    {
        GameManager.AddGameStateEnterAction(GameManager.GameState.GameReady, ResetMatch);   // 판이 새로 차려질 때 시계 0
    }

    // 틱이 도는지는 플래그가 아니라 상태가 정한다. 일시정지(GameStop)·종료(GameOver/Clear)에서 저절로 멈춘다
    private bool IsRunning => GameManager.CurrentState == GameManager.GameState.GamePlay;

    private void ResetMatch()
    {
        Elapsed = 0f;
    }

    #region 심장: 고정 스텝 틱
    // FixedUpdate = 고정 스텝(기본 0.02s). 기기가 달라도 틱 수·순서가 같아야 결과가 같다
    private void FixedUpdate()
    {
        if (!IsRunning) { return; }

        Elapsed += Time.fixedDeltaTime;

        if (Elapsed >= MatchLengthSec)
        {
            EndMatch(Draw);   // 골이 생기면 스코어로 승패를 정한다. 지금은 시간만 끝난다
        }
    }
    #endregion

    // 승리 팀 0 → GameClear, 그 외(패배·무승부) → GameOver. 스테이지 클리어는 승리만(스펙 §7)
    public void EndMatch(int winnerTeam)
    {
        GameManager.SetGameState(winnerTeam == 0 ? GameManager.GameState.GameClear : GameManager.GameState.GameOver);
        Debug.Log($"[Match] 경기 종료. 승리 팀: {winnerTeam}");

        if (MatchEnded != null)
        {
            MatchEnded(winnerTeam);
        }
    }

    public override void Clear()
    {
        GameManager.RemoveGameStateEnterAction(GameManager.GameState.GameReady, ResetMatch);
        MatchEnded = null;   // 씬이 내려가면 구독자도 같이 사라진다. 죽은 구독자를 들고 있지 않게
    }
}
