using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Tactics;
using UnityEngine;

// 시즌의 수명과 영속(09-27 확정 스펙 ①③). PlayerAccountData와 같은 꼴: GameDataManager가 들고 씬을 넘어 살며, SaveLoadSystem이 Save()를 부른다.
// 판정은 전부 순수 코어(SeasonProgress·SeasonRunner) 몫. 여기는 "지금 시즌이 무엇인가", 파생물(상대 팀·일정·이번 경기) 캐시, 저장 타이밍만.
// 경기 결과 보고는 비동기다(09-28 G1): 나머지 경기는 백그라운드에서 계산하고 반영·저장은 메인 스레드. 보고 중(IsReporting)엔 이번 라운드 전 상태라 다음 경기 재료를 내주지 않는다
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
    private GeneratedTeam foreignTeam;   // 승강전 1·2차전의 다른 부 상대(10-08). 처음 필요할 때 만들고 시즌이 바뀌면 비운다

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
    public bool IsRegularSeasonOver => SeasonProgress.IsRegularSeasonOver(State, Tier);
    public PlayoffStage PendingPlayoff => SeasonProgress.PendingStage(State, Tier);   // 승강전(10-08). None = 대상 아님, Done = 끝
    public PlayoffPlan PlayoffPlan => SeasonProgress.Plan(State, Tier);

    public LeagueTable Table()
    {
        return State.Table(Tier.Teams);
    }

    // 시즌이 끝났을 때 내 팀 판정(승격·잔류·강등·우승, 최종 순위). 끝나기 전엔 던진다(09-28 시즌 종료 표시)
    public SeasonOutcome Outcome()
    {
        return SeasonProgress.Outcome(State, Tier);
    }

    // 승점표 행(팀 id) → 표시 이름. 상대 id는 1부터라 인덱스로 짐작하지 않고 찾는다(09-28 결과 화면)
    public string TeamName(int teamId)
    {
        if (teamId == SeasonState.MyTeamId) { return MyTeamName; }
        if (foreignTeam != null && teamId == foreignTeam.TeamId) { return foreignTeam.Name; }   // 승강전 상대(다른 부)
        return SeasonRunner.Find(Opponents, teamId).Name;   // 없는 id면 Find가 던진다
    }

    // 이번 라운드 내 경기의 재료. 시즌이 끝났으면 먼저 PrepareNextMatch()
    public MatchSetup CurrentMatch
    {
        get
        {
            // 보고 중엔 State가 아직 이번 라운드 전이라 끝난 경기를 다시 내준다. 지금은 결과 화면 버튼 잠금이 막지만, 새 진입 경로(뒤로가기 등)가 생겨도 조용히 틀리지 않고 터지게 여기서 막는다(09-28 리뷰)
            if (IsReporting) { throw new InvalidOperationException("[Season] 이번 라운드 결과를 아직 처리 중이다. 끝난 뒤(IsReporting false) 다음 경기를 연다"); }
            if (IsOver) { throw new InvalidOperationException($"[Season] {Tier.Tier}부 시즌이 끝났다({State.RoundsPlayed}/{Tier.Matches}). PrepareNextMatch()로 다음 시즌을 연다"); }
            if (currentMatch == null)
            {
                currentMatch = IsRegularSeasonOver ? SetupPlayoff() : SeasonRunner.SetupMyMatch(State, Opponents, Schedule, TeamTacticsRepository.All, State.RoundsPlayed);
            }
            return currentMatch;
        }
    }

    // 내 경기 결과 → 같은 라운드 나머지 경기 헤드리스 → 즉시 저장. 승점표가 뜬 뒤 앱이 죽어도 이 라운드는 남는다(09-27 ③)
    // 나머지 경기(부에 따라 2~5경기, 경기당 9,000틱 ≈ 0.5초)를 FixedUpdate 안에서 돌려 휘슬 뒤 화면이 최대 2.5초 멈췄다. 계산만 백그라운드 스레드로 뺀다(09-28 G1)
    // 보고 중엔 CurrentMatch·PrepareNextMatch가 옛 라운드를 읽으므로 둘은 오류로 막고, 결과 화면은 끝날 때까지 버튼을 잠근다(09-28 리뷰)
    public bool IsReporting { get; private set; }
    public bool LastReportFailed { get; private set; }   // 직전 보고가 실패했나. 결과 화면이 성공처럼 보이지 않게 "저장 실패"를 띄운다(09-28 리뷰)

    public async Awaitable ReportMyResultAsync(MatchResult myResult)
    {
        if (IsReporting)
        {
            Debug.LogError("[Season] 이전 라운드 결과를 아직 처리 중이다. 이번 보고는 버린다");
            return;
        }
        IsReporting = true;
        LastReportFailed = false;

        if (IsRegularSeasonOver)
        {
            ReportPlayoffResult(myResult);   // 승강전(10-08): 헤드리스 경기가 없어 동기. 승부차기까지 여기서 정하고 저장한다
            IsReporting = false;
            return;
        }

        SeasonState state = State;
        bool recorded = false;
        try
        {
            // Unity API는 스레드 안전하지 않아 재료는 메인 스레드에서 미리 잡는다(TeamTacticsRepository.All은 처음 부를 때 Resources.Load를 탄다).
            // try 안에 둬야 여기서 던져도 finally가 IsReporting을 푼다(09-28 리뷰: 밖에 두면 결과 화면 버튼이 영영 잠겼다)
            int round = state.RoundsPlayed;
            int seasonSeed = state.SeasonSeed;
            IReadOnlyList<GeneratedTeam> opponents = Opponents;
            IReadOnlyList<Fixture> schedule = Schedule;
            IReadOnlyList<TeamTactics> presets = TeamTacticsRepository.All;

            // 시뮬은 순수 C#(Game.Core, 엔진 참조 없음)이고 값·읽기 전용 목록만 넘겨 스레드에서 돌려도 된다. 이 구간엔 Debug.Log도 부르지 않는다
            await Awaitable.BackgroundThreadAsync();
            List<MatchResult> results = SeasonRunner.ComputeRound(round, seasonSeed, opponents, schedule, presets, myResult);
            await Awaitable.MainThreadAsync();

            // 에디터에서 계산 중 Play를 끄면 여기로 돌아왔을 때 SaveLoadSystem이 이미 파괴돼 있다. Instance가 초기화 안 된 새 객체를 만들어 세이브를 null로 덮으니
            // 저장하지 않고 빠진다(09-28 리뷰). 실기기는 앱 종료 때 프로세스가 같이 끝나 여기로 안 온다
            if (Application.exitCancellationToken.IsCancellationRequested) { return; }

            // 넣기는 메인 스레드에서 한 번에 한다. 헤드리스 경기 중 예외가 나면 여기까지 못 와서 반쪽 라운드가 생기지 않는다(09-27 리뷰 D3)
            state.AddRound(results);
            recorded = true;
            currentMatch = null;
            SaveLoadSystem.Instance.Save();
            Debug.Log($"[Season] {Tier.Tier}부 {State.RoundsPlayed}/{Tier.Matches} 라운드 완료" + (IsOver ? " → 시즌 종료" : string.Empty));
        }
        catch (Exception e)
        {
            // 백그라운드에서 던졌을 수 있으니 메인 스레드로 먼저 돌아온다. 그래서 finally도 메인 스레드에서 돈다
            await Awaitable.MainThreadAsync();
            LastReportFailed = true;
            string what = recorded ? "저장 실패(이 라운드는 메모리엔 들어갔다)" : "계산 실패(이 라운드는 기록되지 않았다)";
            Debug.LogError($"[Season] {Tier.Tier}부 결과 {what}: {e}");
        }
        finally
        {
            IsReporting = false;
        }
    }

    // ── 승강전(10-08). 재료는 SeasonRunner, 흐름 판정은 SeasonProgress. 여기는 다른 부 팀 캐시와 저장만
    private MatchSetup SetupPlayoff()
    {
        PlayoffPlan plan = PlayoffPlan;
        PlayoffStage stage = PendingPlayoff;
        if (stage != PlayoffStage.Semifinal && foreignTeam == null)
        {
            TierRule other = TierRuleRepository.Get(plan.OtherTier);
            foreignTeam = SeasonRunner.ForeignTeam(State.SeasonSeed, other, plan.Role, PlayerTableRepository.All, FormationTemplateRepository.All, TeamNameRepository.Table, TeamTacticsRepository.All);
        }
        return SeasonRunner.SetupPlayoffMatch(State, Tier, plan, stage, Opponents, foreignTeam, TeamTacticsRepository.All);
    }

    private void ReportPlayoffResult(MatchResult myResult)
    {
        try
        {
            PlayoffStage stage = PendingPlayoff;
            if (stage == PlayoffStage.None || stage == PlayoffStage.Done) { throw new InvalidOperationException($"[Season] 치를 승강전이 없다({stage})"); }
            MatchSetup setup = currentMatch ?? SetupPlayoff();
            PlayoffResult? legOne = stage == PlayoffStage.LegTwo ? State.PlayoffResults[State.PlayoffResults.Count - 1] : (PlayoffResult?)null;
            State.AddPlayoffResult(SeasonRunner.ResolvePlayoff(setup, stage, myResult, legOne));
            currentMatch = null;
            SaveLoadSystem.Instance.Save();
            Debug.Log($"[Season] {Tier.Tier}부 승강전 {stage} 완료" + (IsOver ? " → 시즌 종료" : string.Empty));
        }
        catch (Exception e)
        {
            LastReportFailed = true;
            Debug.LogError($"[Season] 승강전 결과 처리 실패: {e}");
        }
    }

    // 결과 화면의 라운드 자리 문구. 정규 시즌이면 "n부 r/m 라운드", 승강전이면 단계와 합산
    public string RoundLine()
    {
        if (!IsRegularSeasonOver) { return $"{Tier.Tier}부 {State.RoundsPlayed}/{Tier.Matches} 라운드"; }
        IReadOnlyList<PlayoffResult> results = State.PlayoffResults;
        if (results.Count == 0) { return $"{Tier.Tier}부 정규 시즌 종료"; }
        PlayoffResult last = results[results.Count - 1];
        if (last.Stage == PlayoffStage.Semifinal) { return $"{Tier.Tier}부 승강 PO 단판 · {SemifinalLine(last)}"; }
        if (last.Stage == PlayoffStage.LegOne) { return "승강전 1차전"; }
        PlayoffResult legOne = results[results.Count - 2];
        int me = SeasonState.MyTeamId;
        int mine = PlayoffRules.AggregateGoals(legOne, last, me);
        int theirs = PlayoffRules.AggregateGoals(legOne, last, last.HomeTeamId == me ? last.AwayTeamId : last.HomeTeamId);
        string pens = PenaltyLine(last, me);
        return $"승강전 2차전 · 합산 {mine}:{theirs}{pens}";
    }

    // 단판은 무승부면 홈(2위)이 올라간다. 제목(무승부)과 어긋나 보이지 않게 사유를 붙인다(10-08 리뷰)
    private static string SemifinalLine(PlayoffResult semi)
    {
        bool won = PlayoffRules.SemifinalWinner(semi) == SeasonState.MyTeamId;
        if (semi.HomeGoals != semi.AwayGoals) { return won ? "승리" : "패배"; }
        return won ? "무승부, 상위 순위(2위)로 진출" : "무승부, 상위 순위(2위)가 진출";
    }

    private static string PenaltyLine(PlayoffResult legTwo, int me)
    {
        if (legTwo.HomePenalties + legTwo.AwayPenalties == 0) { return string.Empty; }
        bool myHome = legTwo.HomeTeamId == me;
        int mine = myHome ? legTwo.HomePenalties : legTwo.AwayPenalties;
        int theirs = myHome ? legTwo.AwayPenalties : legTwo.HomePenalties;
        return $" · 승부차기 {mine}:{theirs}";
    }

    // 팀 전술 설정창(09-30)의 입구 3개: 카드·슬라이더·세부 표. 규칙(없는 id 거부, 기준 카드 유지/교체, 범위 검증)은 SeasonState가 정하고 여기는 앞뒤만 맡는다.
    // 카드: 없는 id면 SeasonState가 던지고 아무것도 안 바뀐다
    public void SetMyPreset(string presetId)
    {
        ThrowIfReporting();
        State.SetMyPreset(presetId, TeamTacticsRepository.All);
        CommitMyTactics();
    }

    // 슬라이더: 기준 카드는 그대로
    public void SetMyTactics(TeamTactics tactics)
    {
        ThrowIfReporting();
        State.SetMyTactics(tactics, TeamTacticsRepository.All);
        CommitMyTactics();
    }

    // 세부 표: 어떤 카드와 값이 같아지면 그 카드를 고른 것으로
    public void SetMyTacticsFromTable(TeamTactics tactics)
    {
        ThrowIfReporting();
        State.SetMyTacticsFromTable(tactics, TeamTacticsRepository.All);
        CommitMyTactics();
    }

    // 개인 전술(09-30): 전술 화면에서 필드의 선수 칩을 눌러 역할과 개인 지시를 정한다. 규칙(같은 자리의 노출된 역할만, 지시 범위)은 SeasonState가 정한다.
    // 이번 경기 재료에는 내 라인업의 선수 값도 들어 있어서 팀 전술과 같이 비우고 저장한다
    public void SetPlayerRole(int playerId, string variantId)
    {
        ThrowIfReporting();
        State.SetPlayerRole(playerId, variantId, PlayerTableRepository.All);
        CommitMyTactics();
    }

    public void SetPlayerInstruction(int playerId, PlayerDial dial, int offset)
    {
        ThrowIfReporting();
        State.SetPlayerInstruction(playerId, dial, offset);
        CommitMyTactics();
    }

    public void ClearPlayerInstructions(int playerId)
    {
        ThrowIfReporting();
        State.ClearPlayerInstructions(playerId);
        CommitMyTactics();
    }

    // 배치(스펙 §8, 10-08): 전술 화면 필드에서 칩을 끌어 놓은 편성 자리. 규칙은 SeasonState가 정한다
    public void MoveLineupSlot(int playerId, bool defending, float x, float z)
    {
        ThrowIfReporting();
        State.MoveLineupSlot(playerId, defending, x, z);
        CommitMyTactics();
    }

    // CurrentMatch·PrepareNextMatch와 같은 방어선: 지금은 설정창이 GameReady(보고가 끝난 뒤)에만 떠서 겹칠 길이 없지만, 새 진입 경로가 생겨도 조용히 틀리지 않게(09-30 리뷰)
    private void ThrowIfReporting()
    {
        if (IsReporting) { throw new InvalidOperationException("[Season] 이번 라운드 결과를 아직 처리 중이다. 끝난 뒤에 전술을 바꾼다"); }
    }

    // 이번 경기 재료(currentMatch)의 내 전술이 옛 값이라 비운다. 다시 만들어도 시드·상대·킥오프 팀은 같다(SeasonFlowTests).
    // 바꾸는 즉시 저장한다: 킥오프 전에 앱을 꺼도 다음에 켜면 바꾼 전술로 열린다
    private void CommitMyTactics()
    {
        currentMatch = null;
        SaveLoadSystem.Instance.Save();
    }

    // 경기가 차려질 때(MatchManager.ResetMatch) 부른다. 시즌이 끝났으면 다음 부에서 새 시즌(승격 연출은 09-28 결과 화면)
    public void PrepareNextMatch()
    {
        if (IsReporting) { throw new InvalidOperationException("[Season] 이번 라운드 결과를 아직 처리 중이다. IsOver가 옛 값이라 다음 시즌 여부를 판단할 수 없다(09-28 리뷰)"); }
        if (!IsOver) { return; }
        StartNextSeason();
    }

    public void StartNextSeason()
    {
        int nextTier = SeasonProgress.NextTier(State, Tier);
        Debug.Log($"[Season] {Tier.Tier}부 시즌 종료 → {nextTier}부 새 시즌");
        TeamTactics custom = State.MyCustomTactics;   // 바꾼 전술도 다음 시즌으로 이어 간다(09-30). 카드만 넘기면 승격하자마자 세팅이 풀린다
        // 시즌이 끝난 뒤 로비 전술 화면에서 바꾼 값도 끝난 시즌의 State에 들어가 있다가 여기서 읽혀 새 시즌으로 넘어간다(09-30 리뷰: 로비 편집 경로가 생기며 처음 도달 가능해진 순서)
        IReadOnlyList<RosterPlayer> previousRoster = State.Roster;   // 역할·개인 지시도 이어 간다. 새 시즌 로스터는 기본 편성에서 다시 만들어져 그대로 두면 풀린다
        StartSeason(nextTier, unchecked(State.SeasonSeed + 1), State.MyPresetId);   // 다음 시즌 시드도 결정적(이전 시드 + 1)
        if (custom != null) { State.SetMyTactics(custom, TeamTacticsRepository.All); }
        State.AdoptPlayerTactics(previousRoster, PlayerTableRepository.All);
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
        foreignTeam = null;
    }

    private static int NewSeed()
    {
        return Environment.TickCount & 0x7fffffff;
    }
}
