using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Tactics;
using UnityEngine;

// 시즌의 수명과 영속(09-27 확정 스펙 ①③). PlayerAccountData와 같은 꼴: GameDataManager가 들고 씬을 넘어 살며, SaveLoadSystem이 Save()를 부른다.
// 판정은 전부 순수 코어(SeasonProgress·SeasonRunner) 몫. 여기는 "지금 시즌이 무엇인가", 파생물(상대 팀·일정·이번 경기) 캐시, 저장 타이밍만.
// 상대 팀은 세이브에 없고 (시즌 시드, 부)로 매번 같게 다시 만든다(09-26 확정 스펙 #1)
public class SeasonSystem : ISaveLoad
{
    public DataSourceType SaveDataSouceType => DataSourceType.Local;

    // 내 팀 이름은 아직 세이브에 없다(09-27 결정 ③-a). 이름 짓기 UI가 생길 때 세이브 필드로.
    // HUD·결과 화면이 같은 이름을 쓰도록 시즌 한 곳에 둔다(09-28 결과 화면)
    public const string MyTeamName = "내 팀";

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

    // 승점표 행(팀 id) → 표시 이름. 상대 id는 1부터라 인덱스로 짐작하지 않고 찾는다(09-28 결과 화면)
    public string TeamName(int teamId)
    {
        if (teamId == SeasonState.MyTeamId) { return MyTeamName; }
        for (int i = 0; i < Opponents.Count; i++)
        {
            if (Opponents[i].TeamId == teamId) { return Opponents[i].Name; }
        }
        throw new ArgumentOutOfRangeException(nameof(teamId), $"[Season] {Tier.Tier}부에 팀 id {teamId}가 없다");
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

    // 내 경기 결과 → 같은 라운드 나머지 경기 헤드리스 → 즉시 저장. 결과 화면이 뜬 뒤 앱이 죽어도 경기가 안 날아간다(09-27 ③)
    // 나머지 경기(부에 따라 2~5경기, 경기당 9,000틱 ≈ 0.5초)를 FixedUpdate 안에서 돌려 휘슬 뒤 화면이 최대 2.5초 멈췄다. 계산만 백그라운드 스레드로 뺀다(09-28 G1)
    // 결과 패널은 IsReporting을 보고 버튼을 잠근다. 저장 전에 씬이 다시 로드되면 이 라운드가 사라지기 때문이다
    public bool IsReporting { get; private set; }

    public async Awaitable ReportMyResultAsync(MatchResult myResult)
    {
        if (IsReporting)
        {
            Debug.LogError("[Season] 이전 라운드 결과를 아직 처리 중이다. 이번 보고는 버린다");
            return;
        }
        IsReporting = true;

        // Unity API는 스레드 안전하지 않아 재료는 메인 스레드에서 미리 잡는다(TeamTacticsRepository.All은 처음 부를 때 Resources.Load를 탄다)
        SeasonState state = State;
        IReadOnlyList<GeneratedTeam> opponents = Opponents;
        IReadOnlyList<Fixture> schedule = Schedule;
        IReadOnlyList<TeamTactics> presets = TeamTacticsRepository.All;
        try
        {
            // 시뮬은 순수 C#(Game.Core, 엔진 참조 없음)이고 입력을 읽기만 해서 스레드에서 돌려도 된다. 이 구간엔 Debug.Log도 부르지 않는다
            await Awaitable.BackgroundThreadAsync();
            List<MatchResult> results = SeasonRunner.ComputeRound(state, opponents, schedule, presets, myResult);
            await Awaitable.MainThreadAsync();

            // 넣기는 메인 스레드에서 한 번에 한다. 헤드리스 경기 중 예외가 나면 여기까지 못 와서 반쪽 라운드가 생기지 않는다(09-27 리뷰 D3)
            state.AddRound(results);
            currentMatch = null;
            SaveLoadSystem.Instance.Save();
            Debug.Log($"[Season] {Tier.Tier}부 {State.RoundsPlayed}/{Tier.Matches} 라운드 완료" + (IsOver ? " → 시즌 종료" : string.Empty));
        }
        catch (Exception e)
        {
            // 백그라운드에서 던졌을 수 있으니 메인 스레드로 먼저 돌아온다. 그래서 finally도 메인 스레드에서 돈다
            await Awaitable.MainThreadAsync();
            Debug.LogError($"[Season] {Tier.Tier}부 라운드 {state.RoundsPlayed} 결과 처리 실패, 이 라운드는 기록되지 않았다: {e}");
        }
        finally
        {
            IsReporting = false;
        }
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
