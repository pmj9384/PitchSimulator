using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;
using UnityEngine;

// 시즌의 수명과 영속(09-27 확정 스펙 ①③). PlayerAccountData와 같은 꼴: GameDataManager가 들고 씬을 넘어 살며, SaveLoadSystem이 Save()를 부른다.
// 판정은 전부 순수 코어(SeasonProgress·SeasonRunner) 몫. 여기는 "지금 시즌이 무엇인가", 파생물(상대 팀·일정·이번 경기) 캐시, 저장 타이밍만.
// 상대 팀은 세이브에 없고 (시즌 시드, 부)로 매번 같게 다시 만든다(09-26 확정 스펙 #1)
public class SeasonSystem : ISaveLoad
{
    public DataSourceType SaveDataSouceType => DataSourceType.Local;

    public SeasonState State { get; private set; }
    public TierRule Tier { get; private set; }
    public IReadOnlyList<GeneratedTeam> Opponents { get; private set; }
    public IReadOnlyList<Fixture> Schedule { get; private set; }

    private MatchSetup currentMatch;   // 이번 라운드 내 경기. Match·Stage 매니저가 같은 객체를 읽는다. 결과 보고 뒤 비운다

    public SeasonSystem()
    {
        SaveLoadSystem.Instance.RegisterOnSaveAction(this);
    }

    public void Save()
    {
        SaveLoadSystem.Instance.CurrentSaveData.seasonSave = State == null ? null : State.ToSave();
    }

    // 세이브 없음 → 4부 새 시즌. 시드는 한 번 뽑히면 세이브에 남아 그 뒤로는 결정적이다
    public void Load()
    {
        StartSeason(LeagueRules.BottomTier, NewSeed(), SeasonState.DefaultPresetId);
    }

    public void Load(SeasonSave save)
    {
        if (SeasonProgress.NeedsNewSeason(save))
        {
            if (save != null) { Debug.Log($"[Season] 생성기 버전 {save.generatorVersion} ≠ {TeamGenerator.Version}. 상대 팀을 재현할 수 없어 새 시즌으로 시작한다"); }
            Load();
            return;
        }
        try
        {
            Adopt(SeasonState.FromSave(save, PlayerTableRepository.All));
        }
        catch (Exception e)
        {
            // SaveLoadSystem.Load와 같은 정책: 깨진 세이브는 크게 알리고 기본값으로. 예외를 흘리면 이 세션 내내 시즌이 null이고 종료 때 조용히 지워진다(09-27 리뷰 Y1)
            Debug.LogError($"[Season] 시즌 세이브 검증 실패, 새 시즌으로 시작한다: {e.Message}");
            Load();
        }
    }

    public bool IsOver => SeasonProgress.IsOver(State, Tier);

    public LeagueTable Table()
    {
        return State.Table(Tier.Teams);
    }

    // 이번 라운드 내 경기의 재료. 시즌이 끝났으면 먼저 PrepareNextMatch()
    public MatchSetup CurrentMatch
    {
        get
        {
            if (IsOver) { throw new InvalidOperationException($"[Season] {Tier.Tier}부 시즌이 끝났다({State.RoundsPlayed}/{Tier.Matches}). PrepareNextMatch()로 다음 시즌을 연다"); }
            if (currentMatch == null)
            {
                currentMatch = SeasonRunner.SetupMyMatch(State, Opponents, Schedule, TeamTacticsRepository.All, State.RoundsPlayed);
            }
            return currentMatch;
        }
    }

    // 내 경기 결과 → 같은 라운드 나머지 경기 헤드리스 → 즉시 저장. MatchEnded가 GameOver 전환보다 먼저라 결과 화면이 뜰 때 승점표가 완성돼 있고, 거기서 앱이 죽어도 경기가 안 날아간다(09-27 ③)
    public void ReportMyResult(MatchResult myResult)
    {
        SeasonRunner.PlayRound(State, Opponents, Schedule, TeamTacticsRepository.All, myResult);
        currentMatch = null;
        SaveLoadSystem.Instance.Save();
    }

    // 로비가 경기 전에 부른다. 시즌이 끝났으면 다음 부에서 새 시즌(승격 연출은 09-28 결과 화면)
    public void PrepareNextMatch()
    {
        if (!IsOver) { return; }
        StartNextSeason();
    }

    public void StartNextSeason()
    {
        int nextTier = SeasonProgress.NextTier(State, Tier);
        Debug.Log($"[Season] {Tier.Tier}부 시즌 종료 → {nextTier}부 새 시즌");
        StartSeason(nextTier, unchecked(State.SeasonSeed + 1), State.MyPresetId);   // 다음 시즌 시드도 결정적(이전 시드 + 1)
        SaveLoadSystem.Instance.Save();
    }

    private void StartSeason(int tier, int seasonSeed, string presetId)
    {
        Adopt(SeasonFactory.NewSeason(tier, seasonSeed, presetId));
    }

    private void Adopt(SeasonState state)
    {
        State = state;
        Tier = TierRuleRepository.Get(state.Tier);
        Opponents = TeamGenerator.Generate(state.SeasonSeed, Tier, PlayerTableRepository.All, FormationTemplateRepository.All, TeamNameRepository.Table);
        Schedule = SeasonSchedule.RoundRobin(Tier.Teams);
        currentMatch = null;
    }

    private static int NewSeed()
    {
        return Environment.TickCount & 0x7fffffff;
    }
}
