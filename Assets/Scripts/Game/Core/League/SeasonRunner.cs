using System;
using System.Collections.Generic;
using Game.Core.AutoMatch;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Tactics;

namespace Game.Core.League
{
    // 시즌 한 라운드를 진행한다: 내 경기 결과는 밖(Play)에서 받고, 같은 라운드의 나머지 경기는 여기서 헤드리스로 돌린다(3분 = 0.5초).
    // 승점표엔 전 경기가 필요하다(스펙 §10). 상대끼리의 경기 시드는 (시즌 시드, 라운드, 홈, 원정)에서만 나와 결정적이다
    public static class SeasonRunner
    {

        public static void PlayRound(SeasonState state, IReadOnlyList<GeneratedTeam> opponents, IReadOnlyList<Fixture> schedule,
            IReadOnlyList<TeamTactics> presets, MatchResult myResult)
        {
            // 결과를 모아 끝에 한 번에 넣는다: 헤드리스 경기 중 예외가 나도 내 결과만 남는 반쪽 상태가 생기지 않는다(09-27 리뷰 D3)
            state.AddRound(ComputeRound(state.RoundsPlayed, state.SeasonSeed, opponents, schedule, presets, myResult));
        }

        // 한 라운드의 전 경기 결과(내 결과가 맨 앞)를 계산만 한다. 입력을 읽기만 해서 백그라운드 스레드에서 불러도 된다.
        // 인게임에선 휘슬 뒤 헤드리스 경기가 FixedUpdate를 2.5초 붙잡던 것을 스레드로 뺀다. 넣기(AddRound)는 부르는 쪽이 메인 스레드에서 한 번에 한다(09-28 G1).
        // 시즌 상태 대신 라운드·시드 값만 받는다: 스레드로 넘어가는 가변 객체가 없어야 "상태를 안 건드린다"가 주석이 아니라 시그니처로 보장된다(09-28 리뷰)
        public static List<MatchResult> ComputeRound(int round, int seasonSeed, IReadOnlyList<GeneratedTeam> opponents, IReadOnlyList<Fixture> schedule,
            IReadOnlyList<TeamTactics> presets, MatchResult myResult)
        {
            Fixture mine = SeasonSchedule.MyFixture(schedule, round, SeasonState.MyTeamId);
            if (myResult.HomeTeamId != mine.HomeTeamId || myResult.AwayTeamId != mine.AwayTeamId)
            {
                throw new InvalidOperationException($"[SeasonRunner] 라운드 {round} 내 경기는 {mine.HomeTeamId} 대 {mine.AwayTeamId}인데 결과는 {myResult.HomeTeamId} 대 {myResult.AwayTeamId}");
            }
            var results = new List<MatchResult> { myResult };
            var fixtures = new List<Fixture>();
            SeasonSchedule.FixturesOfRound(schedule, round, fixtures);
            for (int i = 0; i < fixtures.Count; i++)
            {
                Fixture f = fixtures[i];
                if (f.HomeTeamId == SeasonState.MyTeamId || f.AwayTeamId == SeasonState.MyTeamId) { continue; }
                results.Add(Simulate(seasonSeed, f, opponents, presets));
            }
            return results;
        }

        // 상대끼리 한 경기(헤드리스). 테스트가 결정성·스폰 진영을 이 단위로 본다
        public static MatchSimulation Assemble(int seasonSeed, Fixture f, IReadOnlyList<GeneratedTeam> opponents, IReadOnlyList<TeamTactics> presets)
        {
            GeneratedTeam home = Find(opponents, f.HomeTeamId);
            GeneratedTeam away = Find(opponents, f.AwayTeamId);
            return MatchAssembler.Create(home.ToLineup(asHome: true), away.ToLineup(asHome: false), FindPreset(presets, home.PresetId), FindPreset(presets, away.PresetId), MatchSeed(seasonSeed, f));
        }

        // 경기 시드는 (시즌 시드, 라운드, 홈, 원정)에서만 나온다. 인게임 내 경기와 헤드리스 상대 경기가 같은 식을 쓴다(같은 세팅 = 같은 경기, "막히면 재세팅")
        public static int MatchSeed(int seasonSeed, Fixture f)
        {
            return unchecked(seasonSeed * 1000003 + f.Round * 7919 + f.HomeTeamId * 104729 + f.AwayTeamId * 31);
        }

        // 이번 라운드 내 경기의 재료. 나는 항상 팀 0(-X 진영), 상대는 팀 1(+X 진영, 생성 행 관례 그대로). 홈·원정은 MatchSetup이 킥오프·결과 변환에서 반영한다
        public static MatchSetup SetupMyMatch(SeasonState state, IReadOnlyList<GeneratedTeam> opponents, IReadOnlyList<Fixture> schedule,
            IReadOnlyList<TeamTactics> presets, int round)
        {
            Fixture mine = SeasonSchedule.MyFixture(schedule, round, SeasonState.MyTeamId);
            int opponentId = mine.HomeTeamId == SeasonState.MyTeamId ? mine.AwayTeamId : mine.HomeTeamId;
            GeneratedTeam opponent = Find(opponents, opponentId);
            return new MatchSetup(mine, MatchSeed(state.SeasonSeed, mine), state.MyLineup(), opponent.ToLineup(asHome: false),
                state.MyTactics(presets), FindPreset(presets, opponent.PresetId), opponent.Name);   // 내 전술 = 바꾼 값 또는 기준 카드(09-30)
        }

        // ── 승강전(10-08, 스펙 §10 10-08 구현 설계). 내 팀이 걸린 경기만 치른다

        // 다른 부의 승강전 상대. [가정] 상위 부(도전자일 때)는 그 부 생성 목록의 마지막 팀이 "최하위 바로 위"를 맡고,
        // 하위 부(수성일 때)는 생성 2·3번 팀의 단판 PO(2번 홈)를 헤드리스로 돌려 승자가 올라온다. id는 1000 + 생성 id, 이름엔 부를 붙인다
        public static GeneratedTeam ForeignTeam(int seasonSeed, TierRule otherTier, PlayoffRole role, IReadOnlyList<PlayerStats> table,
            IReadOnlyList<FormationTemplate> formations, TeamNameTable names, IReadOnlyList<TeamTactics> presets)
        {
            List<GeneratedTeam> teams = TeamGenerator.Generate(seasonSeed, otherTier, table, formations, names);
            if (teams.Count < 3) { throw new InvalidOperationException($"[SeasonRunner] {otherTier.Tier}부 생성 팀이 3개 미만이다({teams.Count})"); }

            GeneratedTeam picked;
            if (role == PlayoffRole.Challenger)
            {
                picked = teams[teams.Count - 1];
            }
            else
            {
                GeneratedTeam home = teams[1];
                GeneratedTeam away = teams[2];
                var f = new Fixture(otherTier.Matches, home.TeamId, away.TeamId);
                MatchSimulation sim = MatchAssembler.Create(home.ToLineup(asHome: true), away.ToLineup(asHome: false), FindPreset(presets, home.PresetId), FindPreset(presets, away.PresetId), MatchSeed(seasonSeed, f));
                var probe = new MatchProbe(sim);
                probe.Run(MatchTuning.MatchTicks, MatchTuning.FixedStep);
                picked = LeagueRules.PlayoffWinner(home.TeamId, away.TeamId, sim.HomeGoals, sim.AwayGoals) == home.TeamId ? home : away;
            }
            return new GeneratedTeam(PlayoffRules.ForeignTeamIdBase + picked.TeamId, $"{picked.Name} ({otherTier.Tier}부)", picked.FormationId, picked.PresetId,
                new List<StageEntry>(picked.Rows), new List<PlayerStats>(picked.Players));
        }

        // 이번 승강전 경기의 재료. 단판은 같은 부 상대, 1·2차전은 다른 부 팀(foreign). 라운드 번호는 정규 뒤에 이어 붙여 시드가 안 겹친다
        public static MatchSetup SetupPlayoffMatch(SeasonState state, TierRule tier, PlayoffPlan plan, PlayoffStage stage,
            IReadOnlyList<GeneratedTeam> opponents, GeneratedTeam? foreign, IReadOnlyList<TeamTactics> presets)
        {
            int me = SeasonState.MyTeamId;
            GeneratedTeam opponent;
            bool myHome;
            if (stage == PlayoffStage.Semifinal)
            {
                opponent = Find(opponents, plan.SemifinalOpponentId);
                myHome = plan.SemifinalAtHome;
            }
            else
            {
                if (foreign == null) { throw new InvalidOperationException("[SeasonRunner] 승강전 1·2차전엔 다른 부 팀이 필요하다"); }
                opponent = foreign;
                bool challengerHomeFirst = plan.Role == PlayoffRole.Challenger;   // 도전자 1차전 홈·2차전 원정, 수성은 반대
                myHome = stage == PlayoffStage.LegOne ? challengerHomeFirst : !challengerHomeFirst;
            }
            var fixture = new Fixture(tier.Matches + (int)stage - 1, myHome ? me : opponent.TeamId, myHome ? opponent.TeamId : me);
            return new MatchSetup(fixture, MatchSeed(state.SeasonSeed, fixture), state.MyLineup(), opponent.ToLineup(asHome: false),
                state.MyTactics(presets), FindPreset(presets, opponent.PresetId), opponent.Name);
        }

        // 내 승강전 경기의 결과. 2차전에서 합산 동점이면 승부차기까지 여기서 정한다(주사위 = 경기 시드 + 1, 결정적)
        public static PlayoffResult ResolvePlayoff(MatchSetup setup, PlayoffStage stage, MatchResult myResult, PlayoffResult? legOne)
        {
            var result = new PlayoffResult(stage, myResult.HomeTeamId, myResult.AwayTeamId, myResult.HomeGoals, myResult.AwayGoals);
            if (stage != PlayoffStage.LegTwo) { return result; }
            if (legOne == null) { throw new InvalidOperationException("[SeasonRunner] 2차전엔 1차전 결과가 필요하다"); }
            if (PlayoffRules.AggregateWinner(legOne.Value, result) != -1) { return result; }

            IReadOnlyList<LineupSlot> home = setup.MyTeamIsHome ? setup.Team0 : setup.Team1;
            IReadOnlyList<LineupSlot> away = setup.MyTeamIsHome ? setup.Team1 : setup.Team0;
            var rng = new Random(unchecked(setup.Seed + 1));
            ShootoutResult pens = PlayoffRules.Shootout(PlayoffRules.TopShooters(home, PlayoffRules.ShootoutKickers), PlayoffRules.KeeperHandling(home),
                PlayoffRules.TopShooters(away, PlayoffRules.ShootoutKickers), PlayoffRules.KeeperHandling(away), () => (float)rng.NextDouble());
            return new PlayoffResult(stage, myResult.HomeTeamId, myResult.AwayTeamId, myResult.HomeGoals, myResult.AwayGoals, pens.HomeScore, pens.AwayScore);
        }

        private static MatchResult Simulate(int seasonSeed, Fixture f, IReadOnlyList<GeneratedTeam> opponents, IReadOnlyList<TeamTactics> presets)
        {
            MatchSimulation sim = Assemble(seasonSeed, f, opponents, presets);
            var probe = new MatchProbe(sim);
            probe.Run(MatchTuning.MatchTicks, MatchTuning.FixedStep);
            return new MatchResult(f.HomeTeamId, f.AwayTeamId, sim.HomeGoals, sim.AwayGoals);
        }

        public static GeneratedTeam Find(IReadOnlyList<GeneratedTeam> teams, int teamId)
        {
            for (int i = 0; i < teams.Count; i++)
            {
                if (teams[i].TeamId == teamId) { return teams[i]; }
            }
            throw new InvalidOperationException($"[SeasonRunner] 생성 팀에 id {teamId}가 없다");
        }

        public static TeamTactics FindPreset(IReadOnlyList<TeamTactics> presets, string presetId)
        {
            for (int i = 0; i < presets.Count; i++)
            {
                if (presets[i].PresetId == presetId) { return presets[i]; }
            }
            throw new InvalidOperationException($"[SeasonRunner] TacticPresets에 없는 프리셋: {presetId}");
        }
    }
}
