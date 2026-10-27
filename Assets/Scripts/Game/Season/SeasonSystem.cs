using System;
using System.Collections.Generic;
using Game.Core.AutoMatch;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Tactics;
using UnityEngine;

// 시즌의 수명과 영속. PlayerAccountData와 같은 꼴: GameDataManager가 들고 씬을 넘어 살며, SaveLoadSystem이 Save를 부른다.
// 판정은 전부 순수 코어(SeasonProgress·SeasonRunner) 몫. 여기는 "지금 시즌이 무엇인가", 파생물(상대 팀·일정·이번 경기) 캐시, 저장 타이밍만.
// 경기 결과 보고는 비동기다(09-28 G1): 나머지 경기는 백그라운드에서 계산하고 반영·저장은 메인 스레드. 보고 중(IsReporting)엔 이번 라운드 전 상태라 다음 경기 재료를 내주지 않는다
// 상대 팀은 세이브에 없고 (시즌 시드, 부)로 매번 같게 다시 만든다.
// 다음 경기·시즌 끝 한 줄 문구(NextMatchLine 등)도 여기서 낸다: 홈·매치데이·결과 화면이 같은 글자를 써야 해서 한 곳에 둔다
public class SeasonSystem : ISaveLoad
{
    public DataSourceType SaveDataSouceType => DataSourceType.Local;

    // 내 팀 이름은 아직 세이브에 없다. 이름 짓기 UI가 생길 때 세이브 필드로.
    // HUD·결과 화면이 같은 이름을 쓰도록 시즌 한 곳에 둔다
    public const string MyTeamName = "내 팀";

    public SeasonState State { get; private set; }
    public TacticLibrary Library { get; private set; } = new TacticLibrary();   // 저장 전술(10-25). LoadLibrary가 세이브로 바꿔 끼운다
    public TierRule Tier { get; private set; }
    public IReadOnlyList<GeneratedTeam> Opponents { get; private set; }
    public GeneratedTeam ScoutUpperTeam { get; private set; }   // 스카우트 후보 풀의 바로 위 부 팀(스펙 §4-5 10-19). 1부는 null. 상대 팀과 같이 시드로 다시 만든다
    public IReadOnlyList<Fixture> Schedule { get; private set; }

    private MatchSetup currentMatch;   // 이번 라운드 내 경기. Match·Stage 매니저가 같은 객체를 읽는다. 결과 보고 뒤 비운다
    private GeneratedTeam foreignTeam;   // 승강전 1·2차전의 다른 부 상대. 처음 필요할 때 만들고 시즌이 바뀌면 비운다

    public SeasonSystem()
    {
        SaveLoadSystem.Instance.RegisterOnSaveAction(this);
    }

    public void Save()
    {
        SaveLoadSystem.Instance.CurrentSaveData.seasonSave = State == null ? null : State.ToSave();
        SaveLoadSystem.Instance.CurrentSaveData.tacticLibrary = Library.ToSave();
    }

    // 저장 전술 목록(10-25). 시즌과 별개: 시즌이 끝나거나 새로 시작해도 남는다. 깨진 세이브는 시즌과 같은 정책(알리고 빈 목록)
    public void LoadLibrary(TacticLibrarySave save)
    {
        try
        {
            Library = TacticLibrary.FromSave(save);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Season] 저장 전술 세이브 검증 실패, 빈 목록으로 시작한다: {e.Message}");
            Library = new TacticLibrary();
        }
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
            SeasonState state = SeasonState.FromSave(save, PlayerTableRepository.All);
            if (string.IsNullOrEmpty(save.attackFormation))
            {
                // 10-25 전 세이브: 자유 배치로 옮긴 좌표를 버리고 4-4-2 템플릿 슬롯 값으로 1회 바꾼다(10-27 ④, 스펙 §8). 슬롯 번호는 FromSave가 라인업 순서로 흡수했다
                state.SetFormations(state.AttackFormationId, state.DefenseFormationId, FormationTemplateRepository.All);
            }
            Adopt(state);
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
    public PlayoffStage PendingPlayoff => SeasonProgress.PendingStage(State, Tier);   // 승강전. None = 대상 아님, Done = 끝
    private PlayoffPlan PlayoffPlan => SeasonProgress.Plan(State, Tier);   // 호출처가 SetupPlayoff뿐

    public LeagueTable Table()
    {
        return State.Table(Tier.Teams);
    }

    // 시즌이 끝났을 때 내 팀 판정(승격·잔류·강등·우승, 최종 순위). 끝나기 전엔 던진다
    public SeasonOutcome Outcome()
    {
        return SeasonProgress.Outcome(State, Tier);
    }

    // 승점표 행(팀 id) → 표시 이름. 상대 id는 1부터라 인덱스로 짐작하지 않고 찾는다
    public string TeamName(int teamId)
    {
        if (teamId == SeasonState.MyTeamId) { return MyTeamName; }
        if (teamId >= PlayoffRules.ForeignTeamIdBase && IsRegularSeasonOver) { return EnsureForeignTeam().Name; }   // 승강전 상대(다른 부). 재시작 뒤 캐시가 비어 있어도 시드로 같은 팀을 다시 만든다(10-19 리뷰 🟡)
        return SeasonRunner.Find(Opponents, teamId).Name;   // 없는 id면 Find가 던진다
    }

    // 이번 라운드 내 경기의 재료. 시즌이 끝났으면 먼저 StartNextSeason(홈 [다음 시즌]) 또는 PrepareNextMatch(인게임 안전장치)
    public MatchSetup CurrentMatch
    {
        get
        {
            // 보고 중엔 State가 아직 이번 라운드 전이라 끝난 경기를 다시 내준다. 지금은 결과 화면 버튼 잠금이 막지만, 새 진입 경로(뒤로가기 등)가 생겨도 조용히 틀리지 않고 터지게 여기서 막는다
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
    // 보고 중엔 CurrentMatch·PrepareNextMatch가 옛 라운드를 읽으므로 둘은 오류로 막고, 결과 화면은 끝날 때까지 버튼을 잠근다
    public bool IsReporting { get; private set; }
    public bool LastReportFailed { get; private set; }   // 직전 보고가 실패했나. 결과 화면이 성공처럼 보이지 않게 "저장 실패"를 띄운다
    public event Action Reported;   // 보고가 끝났다(성공·실패 모두). 열려 있는 홈·매치데이가 다시 그린다(10-19: 치트로 돌리는 동안 연 홈이 "계산 중"에 멈춰 있었다)

    // readout = 내 팀의 이번 경기 실측 요약(로비 홈 한 줄). 결과와 같은 저장에 묶어 두 값이 서로 다른 경기를 가리키지 않게 한다
    public async Awaitable ReportMyResultAsync(MatchResult myResult, TacticsReadoutSummary readout)
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
            ReportPlayoffResult(myResult, readout);   // 승강전: 헤드리스 경기가 없어 동기. 승부차기까지 여기서 정하고 저장한다
            IsReporting = false;
            Reported?.Invoke();
            return;
        }

        SeasonState state = State;
        bool recorded = false;
        try
        {
            // Unity API는 스레드 안전하지 않아 재료는 메인 스레드에서 미리 잡는다(TeamTacticsRepository.All은 처음 부를 때 Resources.Load를 탄다).
            // try 안에 둬야 여기서 던져도 finally가 IsReporting을 푼다(밖에 두면 결과 화면 버튼이 영영 잠겼다)
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
            // 저장하지 않고 빠진다. 실기기는 앱 종료 때 프로세스가 같이 끝나 여기로 안 온다
            if (Application.exitCancellationToken.IsCancellationRequested) { return; }

            // 넣기는 메인 스레드에서 한 번에 한다. 헤드리스 경기 중 예외가 나면 여기까지 못 와서 반쪽 라운드가 생기지 않는다(09-27 리뷰 D3)
            state.AddRound(results);
            state.SetLastReadout(readout);
            recorded = true;
            currentMatch = null;
            AwardMatchCoins(myResult);
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
            Reported?.Invoke();
        }
    }

    // ── 승강전. 재료는 SeasonRunner, 흐름 판정은 SeasonProgress. 여기는 다른 부 팀 캐시와 저장만
    private MatchSetup SetupPlayoff()
    {
        PlayoffStage stage = PendingPlayoff;
        GeneratedTeam foreign = stage == PlayoffStage.Semifinal ? null : EnsureForeignTeam();
        return SeasonRunner.SetupPlayoffMatch(State, Tier, PlayoffPlan, stage, Opponents, foreign, TeamTacticsRepository.All);
    }

    // 승강전 1·2차전의 다른 부 상대. 세이브에 없고 시드로 만든다. 처음 필요할 때 한 번(경기 차릴 때 또는 일정표가 이름을 읽을 때)
    private GeneratedTeam EnsureForeignTeam()
    {
        if (foreignTeam != null) { return foreignTeam; }
        PlayoffPlan plan = PlayoffPlan;
        TierRule other = TierRuleRepository.Get(plan.OtherTier);
        foreignTeam = SeasonRunner.ForeignTeam(State.SeasonSeed, other, plan.Role, PlayerTableRepository.All, FormationTemplateRepository.All, TeamNameRepository.Table, TeamTacticsRepository.All);
        return foreignTeam;
    }

    // 개발용(에디터 치트 창): 이번 경기를 헤드리스로 끝까지 돌려 정식 보고 경로로 넣는다. 나머지 경기·저장·승강전은 보고가 알아서 한다.
    // 실측 요약은 없어 홈의 실측 줄은 "아직 치른 경기가 없다"로 남는다
    public async Awaitable AutoPlayCurrentMatchAsync()
    {
        MatchSetup setup = CurrentMatch;
        MatchSimulation sim = MatchAssembler.Create(setup.Team0, setup.Team1, setup.Tactics0, setup.Tactics1, setup.Seed);
        var probe = new MatchProbe(sim);
        probe.Run(MatchTuning.MatchTicks, MatchTuning.FixedStep);
        await ReportMyResultAsync(setup.ResultFor(sim.HomeGoals, sim.AwayGoals), null);
    }

    // 개발용: 시뮬 없이 이번 경기를 정한 점수로 보고한다. 순위를 2·3위나 최하위 바로 위로 맞춰 승강전 경로를 본다(1위는 바로 승격이라 승강전이 없다)
    public async Awaitable ReportFixedResultAsync(int myGoals, int theirGoals)
    {
        MatchSetup setup = CurrentMatch;
        await ReportMyResultAsync(setup.ResultFor(myGoals, theirGoals), null);
    }

    private void ReportPlayoffResult(MatchResult myResult, TacticsReadoutSummary readout)
    {
        try
        {
            PlayoffStage stage = PendingPlayoff;
            if (stage == PlayoffStage.None || stage == PlayoffStage.Done) { throw new InvalidOperationException($"[Season] 치를 승강전이 없다({stage})"); }
            MatchSetup setup = currentMatch ?? SetupPlayoff();
            PlayoffResult? legOne = stage == PlayoffStage.LegTwo ? State.PlayoffResults[State.PlayoffResults.Count - 1] : (PlayoffResult?)null;
            State.AddPlayoffResult(SeasonRunner.ResolvePlayoff(setup, stage, myResult, legOne));
            State.SetLastReadout(readout);
            currentMatch = null;
            AwardMatchCoins(myResult);
            SaveLoadSystem.Instance.Save();
            Debug.Log($"[Season] {Tier.Tier}부 승강전 {stage} 완료" + (IsOver ? " → 시즌 종료" : string.Empty));
        }
        catch (Exception e)
        {
            LastReportFailed = true;
            Debug.LogError($"[Season] 승강전 결과 처리 실패: {e}");
        }
    }

    // 홈·매치데이의 다음 경기 문구. 시즌이 끝났으면 승강 판정, 승강전이 남았으면 그 단계, 아니면 다음 라운드 번호(RoundLine은 끝난 라운드 기준이라 따로 둔다)
    public string NextMatchLine()
    {
        if (IsOver) { return OutcomeLine(); }
        if (IsRegularSeasonOver) { return PlayoffNextLine(); }
        return $"{Tier.Tier}부 {State.RoundsPlayed + 1}/{Tier.Matches} 라운드";
    }

    // 승강전이 남았을 때 다음 경기 안내. 결과 화면과 홈·매치데이가 같은 글자를 쓴다
    public string PlayoffNextLine()
    {
        PlayoffStage pending = PendingPlayoff;
        if (pending == PlayoffStage.Semifinal) { return "다음: 승강 플레이오프 단판"; }
        if (pending == PlayoffStage.LegOne) { return "다음: 승강전 1차전"; }
        if (pending == PlayoffStage.LegTwo) { return "다음: 승강전 2차전"; }
        return string.Empty;
    }

    // 시즌 끝 문구. 승강전 결과까지 Outcome에 들어 있다
    public string OutcomeLine()
    {
        SeasonOutcome outcome = Outcome();
        if (outcome.Kind == SeasonOutcomeKind.Champion) { return $"시즌 종료 · {Tier.Tier}부 우승!"; }
        if (outcome.Kind == SeasonOutcomeKind.Promoted) { return $"시즌 종료 · 최종 {outcome.FinalRank}위 · {outcome.NextTier}부 승격!"; }
        if (outcome.Kind == SeasonOutcomeKind.Relegated) { return $"시즌 종료 · 최종 {outcome.FinalRank}위 · {outcome.NextTier}부 강등"; }
        return $"시즌 종료 · 최종 {outcome.FinalRank}위 · 잔류";
    }

    // 결과 화면의 라운드 자리 문구. 정규 시즌이면 "n부 r/m 라운드", 승강전이면 단계와 합산
    public string RoundLine()
    {
        if (!IsRegularSeasonOver) { return $"{Tier.Tier}부 {State.RoundsPlayed}/{Tier.Matches} 라운드"; }
        IReadOnlyList<PlayoffResult> results = State.PlayoffResults;
        if (results.Count == 0) { return $"{Tier.Tier}부 정규 시즌 종료"; }
        PlayoffResult last = results[results.Count - 1];
        if (last.Stage == PlayoffStage.Semifinal) { return $"{Tier.Tier}부 승강 플레이오프 단판 · {SemifinalLine(last)}"; }
        if (last.Stage == PlayoffStage.LegOne) { return "승강전 1차전"; }
        PlayoffResult legOne = results[results.Count - 2];
        int me = SeasonState.MyTeamId;
        int mine = PlayoffRules.AggregateGoals(legOne, last, me);
        int theirs = PlayoffRules.AggregateGoals(legOne, last, last.HomeTeamId == me ? last.AwayTeamId : last.HomeTeamId);
        string pens = PenaltyLine(last, me);
        return $"승강전 2차전 · 합산 {mine}:{theirs}{pens}";
    }

    // 단판은 무승부면 홈(2위)이 올라간다. 제목(무승부)과 어긋나 보이지 않게 사유를 붙인다
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

    // 팀 전술 설정창의 입구 3개: 카드·슬라이더·세부 표. 규칙(없는 id 거부, 기준 카드 유지/교체, 범위 검증)은 SeasonState가 정하고 여기는 앞뒤만 맡는다.
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

    // 개인 전술: 전술 화면에서 필드의 선수 칩을 눌러 역할과 개인 지시를 정한다. 규칙(같은 자리의 노출된 역할만, 지시 범위)은 SeasonState가 정한다.
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

    // ── 스카우트(스펙 §4-5, 10-12). 판정은 ScoutRules, 소모·영입은 SeasonState, 코인은 PlayerAccountData. 여기는 재료를 모아 넘긴다. 저장은 TryScout·BuyScoutAttempt만
    // 이번 시즌 후보 4명(스펙 §4-5 10-19 일괄 제시). 세이브에 없고 시드·로스터·시도 기록에서 매번 같게 나온다
    public IReadOnlyList<ScoutCandidate> ScoutCandidates()
    {
        return ScoutPool.Pick(State.SeasonSeed, State.Roster, Opponents, ScoutUpperTeam, State.ScoutTargets);
    }

    // 후보 4명 중 가장 높은 ★(홈 한 줄 소식의 재료, 10-21). 후보가 없으면 0. 규칙 집계라 문장을 만드는 MatchdayLines가 아니라 여기 둔다
    public int BestScoutStars()
    {
        IReadOnlyList<ScoutCandidate> candidates = ScoutCandidates();
        int best = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            int stars = PreviewScout(candidates[i].TeamId, candidates[i].PlayerIndex).Stars;
            if (stars > best) { best = stars; }
        }
        return best;
    }

    // 후보가 속한 팀. 위 부 팀은 Opponents에 없고 id가 ForeignTeamIdBase 이상이다
    public GeneratedTeam ScoutTeam(int teamId)
    {
        if (!IsUpperTierTeam(teamId)) { return SeasonRunner.Find(Opponents, teamId); }
        if (ScoutUpperTeam == null || ScoutUpperTeam.TeamId != teamId) { throw new InvalidOperationException($"[Season] 스카우트 위 부 팀이 아니다: {teamId}"); }
        return ScoutUpperTeam;
    }

    private static bool IsUpperTierTeam(int teamId)
    {
        return teamId >= PlayoffRules.ForeignTeamIdBase;
    }

    // 미리보기: 별·확률만(주사위 안 굴림). 목록 화면이 후보마다 부른다.
    // 위 부 후보는 "안 만난 팀 ×0.5"를 안 받는다: 상위 부 +1과 겹치면 이중 벌점(스펙 §4-5 10-19)
    public ScoutVerdict PreviewScout(int teamId, int playerIndex)
    {
        GeneratedTeam team = ScoutTeam(teamId);
        bool upper = IsUpperTierTeam(teamId);
        TierRule targetTier = upper ? TierRuleRepository.Get(Tier.Tier - 1) : Tier;
        int stars = ScoutRules.Stars(team.Players[playerIndex], team.Rows[playerIndex], targetTier, Tier.Tier, PlayerTableRepository.All);
        int myRank = Table().RankOf(SeasonState.MyTeamId);
        bool metBefore = upper || ScoutRules.MetBefore(State.Results, SeasonState.MyTeamId, teamId);
        int retries = State.ScoutRetriesOn(teamId, playerIndex);
        float chance = ScoutRules.SuccessChance(stars, myRank, Tier.Teams, metBefore, retries);
        return new ScoutVerdict(stars, chance, 0f, success: false, retries, unmet: !metBefore);
    }

    // 벤치에 자리가 있어야 시도할 수 있다(스펙 §4-5 10-19: 영입 = 벤치 추가). 화면이 먼저 잠근다
    public bool CanRecruit => SquadRules.BenchHasRoom(State.BenchCount);

    // 지목 → 즉시 판정. 시도 1 소모, 성공이면 벤치에 추가(빌드는 내 총점 재분배, ★은 판정 값). 주사위는 (시즌 시드, 팀, 선수, 시도 번호)라 같은 세팅 = 같은 결과.
    // 벤치·자리 검사는 시도를 소모하기 전에 한다: 추가에서 던지면 시도만 줄고 저장은 안 되는 반쪽 상태가 된다
    public ScoutVerdict TryScout(int teamId, int playerIndex)
    {
        ThrowIfReporting();
        GeneratedTeam team = ScoutTeam(teamId);
        PlayerStats target = team.Players[playerIndex];
        if (!CanRecruit) { throw new InvalidOperationException($"[Season] 벤치가 찼다({State.BenchCount}/{SquadRules.MaxBench})"); }
        if (!State.FitsLineupRole(target)) { throw new InvalidOperationException($"[Season] {target.RoleId}({teamId}팀 {playerIndex}번) 자리가 내 라인업에 없다"); }

        ScoutVerdict preview = PreviewScout(teamId, playerIndex);
        float roll = ScoutRules.Roll(State.SeasonSeed, teamId, playerIndex, State.ScoutAttempts);
        bool success = ScoutRules.Succeeds(preview.Chance, roll);
        State.SpendScoutAttempt(teamId, playerIndex, success);
        if (success) { State.AddToBench(target, preview.Stars, PortraitRules.Of(teamId, playerIndex)); }   // 후보 행에서 보여 준 얼굴 그대로
        CommitMyTactics();
        return new ScoutVerdict(preview.Stars, preview.Chance, roll, success: success, preview.Retries, preview.Unmet);
    }

    // 방출(벤치 선수만) → ★ × 코인. 규칙은 SeasonState·SquadRules, 코인은 PlayerAccountData
    public int ReleasePlayer(int playerId)
    {
        ThrowIfReporting();
        RosterPlayer released = State.Release(playerId);
        int coins = SquadRules.ReleaseCoins(released.Stars);
        GameDataManager.Instance.PlayerAccountData.AddCoins(coins);
        CommitMyTactics();
        return coins;
    }

    // 선발 교체(같은 자리). 이번 경기 재료가 옛 라인업이라 비우고 저장한다
    public void SwapStarter(int starterId, int benchId)
    {
        ThrowIfReporting();
        State.SwapStarter(starterId, benchId);
        CommitMyTactics();
    }

    // 코인으로 시도 +1. 코인이 모자라면 false(PlayerAccountData.SpendCoin이 판정)
    public bool BuyScoutAttempt()
    {
        ThrowIfReporting();
        if (!GameDataManager.Instance.PlayerAccountData.SpendCoin(ScoutRules.ExtraAttemptCoinPrice)) { return false; }
        State.AddExtraScoutAttempt();
        SaveLoadSystem.Instance.Save();
        return true;
    }

    // 경기 결과 → 코인(값은 ScoutRules 상수, [가정]). 코인 용처가 스카우트 시도뿐이라 수입도 시즌이 준다
    private static void AwardMatchCoins(MatchResult myResult)
    {
        GameDataManager.Instance.PlayerAccountData.AddCoins(ScoutRules.CoinsFor(myResult, SeasonState.MyTeamId));
    }

    // ── 슬롯 격자(10-25~26). 규칙(역할군·GK 슬롯·없는 id)은 SeasonState가 정하고 여기는 앞뒤만

    public void SetFormations(string attackFormationId, string defenseFormationId)
    {
        ThrowIfReporting();
        State.SetFormations(attackFormationId, defenseFormationId, FormationTemplateRepository.All);
        CommitMyTactics();
    }

    // 전술판에서 칩을 다른 칩 위에 놓았을 때. 바꿀 수 있는지는 PitchView가 CanSwapLineupSlots로 먼저 본다
    public void SwapLineupSlots(int playerA, int playerB, bool defending)
    {
        ThrowIfReporting();
        State.SwapLineupSlots(playerA, playerB, defending, FormationTemplateRepository.All);
        CommitMyTactics();
    }

    public void SetDefenseRole(int playerId, string defenseRoleId)
    {
        ThrowIfReporting();
        State.SetDefenseRole(playerId, defenseRoleId, DefenseRoleRepository.All);
        CommitMyTactics();
    }

    // 저장 전술 칸(TacticLibrary.SlotName). 지금 세팅을 그 칸에 뜬다. 같은 칸은 덮어쓴다(확인은 화면이 두 번 누르기로)
    public void SaveTacticSlot(int slotIndex)
    {
        ThrowIfReporting();
        Library.Put(State.SnapshotTactic(TacticLibrary.SlotName(slotIndex)));
        SaveLoadSystem.Instance.Save();
    }

    // 칸의 전술을 입힌다. 빈 칸이면 아무 일도 없다(화면이 버튼을 꺼 두지만 조용히 틀리지 않게)
    public void LoadTacticSlot(int slotIndex)
    {
        ThrowIfReporting();
        SavedTactic tactic = Library.Find(TacticLibrary.SlotName(slotIndex));
        if (tactic == null) { return; }
        State.ApplyTactic(tactic, FormationTemplateRepository.All, DefenseRoleRepository.All, TeamTacticsRepository.All);
        CommitMyTactics();
    }

    // CurrentMatch·PrepareNextMatch와 같은 방어선: 전술 편집은 로비(보고가 끝난 뒤)에서만 열려 겹칠 길이 없지만, 새 진입 경로가 생겨도 조용히 틀리지 않게
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

    // 경기가 차려질 때(MatchManager.ResetMatch) 부른다. 시즌이 끝났으면 다음 부에서 새 시즌.
    // 보통은 홈의 [다음 시즌](StartNextSeason)이 먼저 열어 여기선 아무 일도 없다. 끝난 시즌으로 인게임에 바로 들어온 경우(에디터 직접 Play)의 안전장치로 남긴다(스펙 §2-1)
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
        TeamTactics custom = State.MyCustomTactics;   // 바꾼 전술도 다음 시즌으로 이어 간다. 카드만 넘기면 승격하자마자 세팅이 풀린다
        // 시즌이 끝난 뒤 로비 전술 화면에서 바꾼 값도 끝난 시즌의 State에 들어가 있다가 여기서 읽혀 새 시즌으로 넘어간다(로비 편집 경로가 생기며 처음 도달 가능해진 순서)
        IReadOnlyList<RosterPlayer> previousRoster = State.Roster;   // 역할·개인 지시도 이어 간다. 새 시즌 로스터는 기본 편성에서 다시 만들어져 그대로 두면 풀린다
        IReadOnlyList<LineupEntry> previousLineup = State.Lineup;    // 슬롯·수비 역할·배치도
        string attackFormation = State.AttackFormationId;            // 공수 포메이션도. 4-4-2로 되돌리면 유저가 짠 전술이 시즌마다 풀린다(10-25 유저 "리셋은 절대 안 되지")
        string defenseFormation = State.DefenseFormationId;
        List<RosterPlayer> previousBench = State.Bench();            // 벤치는 역할·★만 넘어간다(스펙 §4-5 10-19)
        StartSeason(nextTier, unchecked(State.SeasonSeed + 1), State.MyPresetId);   // 다음 시즌 시드도 결정적(이전 시드 + 1)
        if (custom != null) { State.SetMyTactics(custom, TeamTacticsRepository.All); }
        State.SetFormations(attackFormation, defenseFormation, FormationTemplateRepository.All);   // AdoptStarters보다 먼저: 슬롯 좌표가 이 id 기준이라서
        State.AdoptStarters(previousRoster, previousLineup, PlayerTableRepository.All);   // 슬롯 기준(스펙 §11 10-19)
        State.AdoptBench(previousBench, PlayerTableRepository.All);
        SaveLoadSystem.Instance.Save();
    }

    // 스카우트 후보 풀의 바로 위 부 팀. 1부는 위가 없어 null
    private GeneratedTeam BuildScoutUpperTeam(int seasonSeed)
    {
        if (Tier.Tier <= 1) { return null; }
        TierRule upper = TierRuleRepository.Get(Tier.Tier - 1);
        return SeasonRunner.ScoutUpperTeam(seasonSeed, upper, PlayerTableRepository.All, FormationTemplateRepository.All, TeamNameRepository.Table);
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
        ScoutUpperTeam = BuildScoutUpperTeam(state.SeasonSeed);
        Schedule = SeasonSchedule.RoundRobin(Tier.Teams);
        currentMatch = null;
        foreignTeam = null;
    }

    private static int NewSeed()
    {
        return Environment.TickCount & 0x7fffffff;
    }
}
