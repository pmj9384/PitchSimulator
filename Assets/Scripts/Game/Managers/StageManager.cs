using System.Collections.Generic;
using Game.Core.League;
using Game.Core.Match;
using UnityEngine;

// "이번 판이 어떤 판인가". 시즌의 이번 라운드 경기(MatchSetup)를 필드로 옮기고, 끝나면 결과를 시즌에 돌려준다(09-27 확정 스펙 ②③).
// 편성 CSV 번호 경로(09-16)는 뺐다: 인게임 경기는 전부 시즌에서 온다. StageComposition은 새 시즌의 내 기본 11명과 러너·테스트 재료로만 남는다.
// 결과 보고(5줄)를 여기 두는 건 InGameManager 3종 세트 하나를 더 만들 만큼의 책임이 아니라서(09-16 공 매니저와 같은 판단)
public class StageManager : InGameManager
{
    public string DisplayName { get; private set; }    // "N라운드"
    public string OpponentName { get; private set; }

    private MatchSetup current;
    private bool loaded;

    public override void Initialize()
    {
        GameManager.AddGameStateEnterAction(GameManager.GameState.GameReady, Load);   // 판이 차려질 때 스폰
        GameManager.Match.MatchEnded += OnMatchEnded;
    }

    // 시즌 → 선수 스폰. 씬 로드당 한 번. 두 번 돌면 선수가 두 배가 된다
    private void Load()
    {
        if (loaded) { return; }
        loaded = true;

        current = GameDataManager.Instance.Season.CurrentMatch;
        DisplayName = $"{current.Fixture.Round + 1}라운드";
        OpponentName = current.OpponentName;

        Spawn(current.Team0, 0);
        Spawn(current.Team1, 1);
    }

    private void Spawn(IReadOnlyList<LineupSlot> slots, int team)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            LineupSlot slot = slots[i];
            GameManager.Players.Spawn(slot.Stats, team, new Vector3(slot.AttackX, 0f, slot.AttackZ), new Vector3(slot.DefendX, 0f, slot.DefendZ));
        }
    }

    // 시뮬 골(팀 0 = 나) → 일정의 홈·원정 기준 결과 → 시즌이 나머지 경기를 돌리고 저장한다
    private void OnMatchEnded(int winnerTeam)
    {
        MatchSimulation sim = GameManager.Match.Simulation;
        SeasonSystem season = GameDataManager.Instance.Season;
        season.ReportMyResult(current.ResultFor(sim.HomeGoals, sim.AwayGoals));
        Debug.Log($"[Stage] {DisplayName} 결과 저장. {season.Tier.Tier}부 {season.State.RoundsPlayed}/{season.Tier.Matches} 라운드 완료" + (season.IsOver ? " → 시즌 종료" : string.Empty));
    }

    public override void Clear()
    {
        GameManager.RemoveGameStateEnterAction(GameManager.GameState.GameReady, Load);
        GameManager.Match.MatchEnded -= OnMatchEnded;
        loaded = false;
        current = null;
        DisplayName = null;
        OpponentName = null;
    }
}
