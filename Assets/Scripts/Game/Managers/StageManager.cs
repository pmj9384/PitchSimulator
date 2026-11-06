using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using UnityEngine;

// "이번 판이 어떤 판인가". 시즌의 이번 라운드 경기(MatchSetup)를 필드로 옮기고, 끝나면 결과를 시즌에 돌려준다.
// 편성 CSV 번호 경로는 뺐다: 인게임 경기는 전부 시즌에서 온다. StageComposition은 새 시즌의 내 기본 11명과 러너·테스트 재료로만 남는다.
// 결과 보고(5줄)를 여기 두는 건 InGameManager 3종 세트 하나를 더 만들 만큼의 책임이 아니라서(09-16 공 매니저와 같은 판단)
public class StageManager : InGameManager
{
    public string DisplayName { get; private set; }    // "N라운드"(로그용. 상대 이름은 MatchManager.OpponentName)

#if UNITY_EDITOR
    [SerializeField] private int stadiumTierOverride;   // 에디터 전용. 1~4면 그 부의 경기장을 띄운다(0 = 사용 안 함). 읽는 곳도 에디터 전용이라 선언도 감싼다(빌드에서 CS0169 경고 = 오류)
#endif

    private MatchSetup current;
    private bool isLoaded;
    private GameObject stadium;

    public override void Initialize()
    {
        GameManager.AddGameStateEnterAction(GameManager.GameState.GameReady, Load);   // 판이 차려질 때 스폰
        GameManager.Match.MatchEnded += OnMatchEnded;
    }

    // 시즌 → 선수 스폰. 씬 로드당 한 번. 두 번 돌면 선수가 두 배가 된다
    private void Load()
    {
        if (isLoaded) { return; }
        isLoaded = true;

        current = GameDataManager.Instance.Season.CurrentMatch;
        DisplayName = $"{current.Fixture.Round + 1}라운드";

        SpawnStadium();
        Spawn(current.Team0, 0, current.Kit0);
        Spawn(current.Team1, 1, current.Kit1);
    }

    // 이번 판 경기장(홈 팀이 속한 부)을 판당 1회 만든다. 반복 스폰이 아니라 ObjectPool 대상이 아니고, Clear에서 지운다
    private void SpawnStadium()
    {
        int tier = current.StadiumTier;
#if UNITY_EDITOR
        if (stadiumTierOverride > 0)
        {
            tier = Mathf.Clamp(stadiumTierOverride, 1, 4);
            Debug.Log($"[Stage] 경기장 부 강제 지정: {tier}부");
        }
#endif
        string id = TierRuleRepository.Get(tier).Stadium;
        GameObject prefab = Resources.Load<GameObject>("Stadiums/" + id);
        if (prefab == null)
        {
            Debug.LogError($"[Stage] 경기장 프리팹 Stadiums/{id} 없음. Tools/Pitch/경기장 프리팹 만들기를 실행한다");
            return;
        }
        stadium = Instantiate(prefab);
        InitCrowd(stadium, id, tier);
    }

    // 관중 채움 시드 = (시즌 시드, 경기장 부, 홈 팀 id)라 같은 시즌·같은 홈 팀이면 재시작해도 빈자리가 같다. 홈 색 = 일정상 홈 팀의 유니폼
    private void InitCrowd(GameObject instance, string stadiumId, int tier)
    {
        var crowd = instance.GetComponent<CrowdRenderer>();
        if (crowd == null)
        {
            Debug.LogError($"[Stage] 경기장 프리팹 {stadiumId}에 CrowdRenderer가 없다. Tools/Pitch/경기장 프리팹 만들기를 실행한다");
            return;
        }

        int seasonSeed = GameDataManager.Instance.Season.State.SeasonSeed;
        uint fillSeed = StadiumRules.FillSeed(seasonSeed, tier, current.Fixture.HomeTeamId);
        int homeKit = current.MyTeamIsHome ? current.Kit0 : current.Kit1;
        crowd.Init(StadiumSpecRepository.Get(stadiumId), fillSeed, PlayerPalette.Kit(homeKit));
    }

    private void Spawn(IReadOnlyList<LineupSlot> slots, int team, int kit)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            LineupSlot slot = slots[i];
            GameManager.Players.Spawn(slot.Stats, team, new Vector3(slot.AttackX, 0f, slot.AttackZ), new Vector3(slot.DefendX, 0f, slot.DefendZ), slot.Portrait, kit);
        }
    }

    // 시뮬 골(팀 0 = 나) → 일정의 홈·원정 기준 결과 → 시즌이 나머지 경기를 돌리고 저장한다.
    // 기다리지 않고 넘긴다: 나머지 경기는 백그라운드에서 돌고 오류·완료 로그는 SeasonSystem이 맡는다. 끝났는지는 결과 패널이 IsReporting으로 본다(09-28 G1)
    private void OnMatchEnded(int winnerTeam)
    {
        MatchManager match = GameManager.Match;
        MatchSimulation sim = match.Simulation;
        _ = GameDataManager.Instance.Season.ReportMyResultAsync(current.ResultFor(sim.HomeGoals, sim.AwayGoals), SummarizeReadout(match, sim));
    }

    // 로비 홈의 지난 경기 한 줄 재료(팀 0 = 나). 결과와 같은 저장에 묶는다.
    // 요약이 던져도 결과 보고는 가야 하므로 여기서 받아 null로 넘긴다: 표시값 하나 때문에 라운드가 빠지면 안 된다(10-15 리뷰)
    private static TacticsReadoutSummary SummarizeReadout(MatchManager match, MatchSimulation sim)
    {
        try
        {
            return TacticsReadoutSummary.From(match.MyReadout, sim.TacticsOf(0));
        }
        catch (Exception e)
        {
            Debug.LogError($"[Stage] 전술 실측 요약 실패, 홈의 지난 경기 줄은 비운다: {e.Message}");
            return null;
        }
    }

    public override void Clear()
    {
        GameManager.RemoveGameStateEnterAction(GameManager.GameState.GameReady, Load);
        GameManager.Match.MatchEnded -= OnMatchEnded;
        isLoaded = false;
        if (stadium != null) { Destroy(stadium); }
        stadium = null;
        current = null;
        DisplayName = null;
    }
}
