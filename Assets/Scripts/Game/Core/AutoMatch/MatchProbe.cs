using Game.Core.Match;

namespace Game.Core.AutoMatch
{
    // 경기 한 판의 결과 한 줄. 러너 CSV의 행이자 밸런스 판단의 단위(승률·기대 골·점유).
    // 진단 지표(접촉·최소 거리 등)는 여기 없다: 그건 규칙을 고칠 때 보는 값이라 FullMatchTests에 둔다
    public sealed class MatchSummary
    {
        public int Seed { get; set; }
        public string HomePreset { get; set; } = string.Empty;
        public string AwayPreset { get; set; } = string.Empty;
        public int HomeGoals { get; set; }
        public int AwayGoals { get; set; }
        public int HomeShots { get; set; }
        public int AwayShots { get; set; }
        public int Passes { get; set; }
        public int Intercepts { get; set; }
        public int TackleAttempts { get; set; }
        public int TackleSuccesses { get; set; }
        public int Turnovers { get; set; }
        public int HomeOwnedTicks { get; set; }
        public int AwayOwnedTicks { get; set; }
        public int HomeOppThirdTicks { get; set; }   // 홈이 소유한 채 공이 원정 서드(x ≥ 17.5)에 있던 틱
        public int AwayOppThirdTicks { get; set; }

        // 0 = 홈 승, 1 = 원정 승, -1 = 무승부
        public int Winner => HomeGoals == AwayGoals ? -1 : (HomeGoals > AwayGoals ? 0 : 1);
    }

    // 시뮬을 틱마다 돌리며 팀별 소유·서드 틱을 세고, 끝나면 MatchSummary를 낸다. 슛은 이벤트로 팀을 가른다
    public sealed class MatchProbe
    {
        private readonly MatchSimulation sim;
        private int homeShots;
        private int awayShots;
        private int homeOwned;
        private int awayOwned;
        private int homeOppThird;
        private int awayOppThird;

        public MatchProbe(MatchSimulation sim)
        {
            this.sim = sim;
            sim.ShotResolved += OnShot;
        }

        private void OnShot(ShotReport r)
        {
            if (sim.Players[r.ShooterId].Team == 0) { homeShots++; } else { awayShots++; }
        }

        public void Run(int ticks, float deltaTime)
        {
            for (int i = 0; i < ticks; i++)
            {
                sim.Tick(deltaTime);
                int owner = sim.OwnerTeam();
                if (owner == 0) { homeOwned++; if (sim.Ball.X >= MatchTuning.ThirdBoundary) { homeOppThird++; } }
                else if (owner == 1) { awayOwned++; if (sim.Ball.X <= -MatchTuning.ThirdBoundary) { awayOppThird++; } }
            }
        }

        public MatchSummary Summarize(int seed, string homePreset, string awayPreset)
        {
            return new MatchSummary
            {
                Seed = seed, HomePreset = homePreset, AwayPreset = awayPreset,
                HomeGoals = sim.HomeGoals, AwayGoals = sim.AwayGoals,
                HomeShots = homeShots, AwayShots = awayShots,
                Passes = sim.PassCount, Intercepts = sim.InterceptCount,
                TackleAttempts = sim.TackleAttemptCount, TackleSuccesses = sim.TackleSuccessCount, Turnovers = sim.TurnoverCount,
                HomeOwnedTicks = homeOwned, AwayOwnedTicks = awayOwned,
                HomeOppThirdTicks = homeOppThird, AwayOppThirdTicks = awayOppThird,
            };
        }
    }
}
