using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Tactics;

namespace Game.Core.AutoMatch
{
    // 자동 대전 러너(플랜 09-23): 같은 세팅을 시드만 바꿔 N판 돌린다. "세팅을 바꾸면 결과가 바뀐다"를 숫자로 실증하는 도구.
    // 한 판 = MatchAssembler.Create → MatchProbe.Run(9,000틱). 엔진 없음, 결정적(시드 = 판 번호)
    public static class AutoMatchRunner
    {

        public static List<MatchSummary> Run(IReadOnlyList<PlayerStats> table, IReadOnlyList<StageEntry> rows, TeamTactics home, TeamTactics away, int firstSeed, int matches)
        {
            var results = new List<MatchSummary>(matches);
            for (int seed = firstSeed; seed < firstSeed + matches; seed++)
            {
                MatchSimulation sim = MatchAssembler.Create(table, rows, home, away, seed);
                var probe = new MatchProbe(sim);
                probe.Run(MatchTuning.MatchTicks, MatchTuning.FixedStep);
                results.Add(probe.Summarize(seed, home.PresetId, away.PresetId));
            }
            return results;
        }

        // 승률·평균 골·평균 슛. 밸런스 판단의 첫 줄(10-06 칸: 같은 세팅 = 50% 근처, 기대 골 2~4)
        public static AutoMatchStats Aggregate(IReadOnlyList<MatchSummary> results)
        {
            var s = new AutoMatchStats { Matches = results.Count };
            if (results.Count == 0) { return s; }

            int homeWins = 0, awayWins = 0, draws = 0;
            float homeGoals = 0f, awayGoals = 0f, homeShots = 0f, awayShots = 0f, turnovers = 0f, homeOwned = 0f;
            float wideShare = 0f, topRoleShare = 0f, shotAbsZ = 0f; int shotMatches = 0;
            for (int i = 0; i < results.Count; i++)
            {
                MatchSummary r = results[i];
                if (r.Winner == 0) { homeWins++; } else if (r.Winner == 1) { awayWins++; } else { draws++; }
                homeGoals += r.HomeGoals; awayGoals += r.AwayGoals;
                homeShots += r.HomeShots; awayShots += r.AwayShots;
                turnovers += r.Turnovers;
                int owned = r.HomeOwnedTicks + r.AwayOwnedTicks;
                homeOwned += owned == 0 ? 0.5f : (float)r.HomeOwnedTicks / owned;
                wideShare += owned == 0 ? 0f : (float)r.WideOwnedTicks / owned;
                topRoleShare += r.TopRoleShare;
                if (r.HomeShots + r.AwayShots > 0)
                {
                    shotAbsZ += r.MeanShotAbsZ;
                    shotMatches++;
                }
            }
            float n = results.Count;
            s.HomeWinRate = homeWins / n; s.AwayWinRate = awayWins / n; s.DrawRate = draws / n;
            s.MeanHomeGoals = homeGoals / n; s.MeanAwayGoals = awayGoals / n;
            s.MeanHomeShots = homeShots / n; s.MeanAwayShots = awayShots / n;
            s.MeanTurnovers = turnovers / n; s.MeanHomePossession = homeOwned / n;
            s.MeanWideShare = wideShare / n; s.MeanTopRoleShare = topRoleShare / n;
            s.MeanShotAbsZ = shotMatches == 0 ? 0f : shotAbsZ / shotMatches;
            return s;
        }
    }

    public sealed class AutoMatchStats
    {
        public int Matches;
        public float HomeWinRate;
        public float AwayWinRate;
        public float DrawRate;
        public float MeanHomeGoals;
        public float MeanAwayGoals;
        public float MeanHomeShots;
        public float MeanAwayShots;
        public float MeanTurnovers;
        public float MeanHomePossession;   // 소유 틱 중 홈 비율(0~1)
        public float MeanWideShare;        // 소유 틱 중 측면 구역(|z| ≥ WideZoneZ) 비율. 09-26 기준 0.13, 0.31이면 터치라인 빌드업
        public float MeanTopRoleShare;     // 최다 역할의 소유 틱 비율. 0.5 넘으면 한 역할이 공을 독점
        public float MeanShotAbsZ;         // 슛 위치 |z| 평균(m)

        public override string ToString()
        {
            return $"{Matches}판: 홈 승 {HomeWinRate:P0} · 무 {DrawRate:P0} · 원정 승 {AwayWinRate:P0} | 평균 골 {MeanHomeGoals:0.00}:{MeanAwayGoals:0.00} | 평균 슛 {MeanHomeShots:0.0}:{MeanAwayShots:0.0} | 턴오버 {MeanTurnovers:0.0} | 홈 점유 {MeanHomePossession:P0} | 형태: 측면 소유 {MeanWideShare:P0} · 최다 역할 {MeanTopRoleShare:P0} · 슛 |z| {MeanShotAbsZ:0.0}m";
        }
    }
}
