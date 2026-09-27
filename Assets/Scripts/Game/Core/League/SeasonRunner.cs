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
        public const int MatchTicks = 9000;   // 3분 ÷ 0.02

        public static void PlayRound(SeasonState state, IReadOnlyList<GeneratedTeam> opponents, IReadOnlyList<Fixture> schedule,
            IReadOnlyList<TeamTactics> presets, MatchResult myResult)
        {
            int round = state.RoundsPlayed;
            Fixture mine = SeasonSchedule.MyFixture(schedule, round, SeasonState.MyTeamId);
            if (myResult.HomeTeamId != mine.HomeTeamId || myResult.AwayTeamId != mine.AwayTeamId)
            {
                throw new InvalidOperationException($"[SeasonRunner] 라운드 {round} 내 경기는 {mine.HomeTeamId} 대 {mine.AwayTeamId}인데 결과는 {myResult.HomeTeamId} 대 {myResult.AwayTeamId}");
            }
            // 결과를 모아 끝에 한 번에 넣는다: 헤드리스 경기 중 예외가 나도 내 결과만 남는 반쪽 상태가 생기지 않는다(09-27 리뷰 D3)
            var results = new List<MatchResult> { myResult };
            var fixtures = new List<Fixture>();
            SeasonSchedule.FixturesOfRound(schedule, round, fixtures);
            for (int i = 0; i < fixtures.Count; i++)
            {
                Fixture f = fixtures[i];
                if (f.HomeTeamId == SeasonState.MyTeamId || f.AwayTeamId == SeasonState.MyTeamId) { continue; }
                results.Add(Simulate(state.SeasonSeed, f, opponents, presets));
            }
            state.AddRound(results);
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
                FindPreset(presets, state.MyPresetId), FindPreset(presets, opponent.PresetId), opponent.Name);
        }

        private static MatchResult Simulate(int seasonSeed, Fixture f, IReadOnlyList<GeneratedTeam> opponents, IReadOnlyList<TeamTactics> presets)
        {
            MatchSimulation sim = Assemble(seasonSeed, f, opponents, presets);
            var probe = new MatchProbe(sim);
            probe.Run(MatchTicks, MatchTuning.FixedStep);
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
