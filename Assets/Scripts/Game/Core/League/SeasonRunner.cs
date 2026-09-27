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
            state.AddResult(myResult);

            var fixtures = new List<Fixture>();
            SeasonSchedule.FixturesOfRound(schedule, round, fixtures);
            for (int i = 0; i < fixtures.Count; i++)
            {
                Fixture f = fixtures[i];
                if (f.HomeTeamId == SeasonState.MyTeamId || f.AwayTeamId == SeasonState.MyTeamId) { continue; }
                state.AddResult(Simulate(state.SeasonSeed, f, opponents, presets));
            }
            state.CompleteRound();
        }

        // 상대끼리 한 경기(헤드리스). 테스트가 결정성·스폰 진영을 이 단위로 본다
        public static MatchSimulation Assemble(int seasonSeed, Fixture f, IReadOnlyList<GeneratedTeam> opponents, IReadOnlyList<TeamTactics> presets)
        {
            GeneratedTeam home = Find(opponents, f.HomeTeamId);
            GeneratedTeam away = Find(opponents, f.AwayTeamId);
            int seed = unchecked(seasonSeed * 1000003 + f.Round * 7919 + f.HomeTeamId * 104729 + f.AwayTeamId * 31);
            return MatchAssembler.Create(home.ToLineup(asHome: true), away.ToLineup(asHome: false), FindPreset(presets, home.PresetId), FindPreset(presets, away.PresetId), seed);
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
