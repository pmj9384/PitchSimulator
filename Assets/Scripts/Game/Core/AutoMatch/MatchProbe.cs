using Game.Core.Match;

namespace Game.Core.AutoMatch
{
    // 경기 한 판의 결과 한 줄. 러너 CSV의 행이자 밸런스 판단의 단위(승률·기대 골·점유).
    // 진단 지표(접촉·최소 거리 등)는 여기 없다: 그건 규칙을 고칠 때 보는 값이라 FullMatchTests에 둔다.
    // 형태 지표 3개(09-26)는 예외: 측면 소유 비율·최다 역할 비율·슛 |z|. 경기가 "정상처럼 보이는 이상"(터치라인 빌드업)은 골·승률로 안 보여서 매 판 같이 찍는다
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
        public int WideOwnedTicks { get; set; }        // 양 팀 소유 틱 중 |z| ≥ WideZoneZ(터치라인 9m 안)
        public string TopRole { get; set; } = string.Empty;   // 소유 틱을 가장 많이 가진 역할
        public float TopRoleShare { get; set; }        // 그 역할의 소유 틱 비율(0~1). 한 역할이 절반 넘으면 형태가 무너진 것
        public float MeanShotAbsZ { get; set; }        // 슛 위치 |z| 평균(m). 코너에서만 쏘면 커진다. 슛 0이면 0

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
        private int wideOwned;
        private float shotAbsZSum;
        private readonly System.Collections.Generic.Dictionary<string, int> ownedByRole = new System.Collections.Generic.Dictionary<string, int>();

        public MatchProbe(MatchSimulation sim)
        {
            this.sim = sim;
            sim.ShotResolved += OnShot;
        }

        private void OnShot(ShotReport r)
        {
            PlayerState shooter = sim.Players[r.ShooterId];
            if (shooter.Team == 0) { homeShots++; } else { awayShots++; }
            shotAbsZSum += System.Math.Abs(shooter.Z);
        }

        public void Run(int ticks, float deltaTime)
        {
            for (int i = 0; i < ticks; i++)
            {
                sim.Tick(deltaTime);
                int owner = sim.OwnerTeam();
                if (owner == 0) { homeOwned++; if (sim.Ball.X >= MatchTuning.ThirdBoundary) { homeOppThird++; } }
                else if (owner == 1) { awayOwned++; if (sim.Ball.X <= -MatchTuning.ThirdBoundary) { awayOppThird++; } }
                if (owner != -1)
                {
                    if (System.Math.Abs(sim.Ball.Z) >= MatchTuning.WideZoneZ) { wideOwned++; }
                    if (sim.Ball.Phase == BallPhase.Owned)
                    {
                        string role = sim.Players[sim.Ball.OwnerId].Stats.RoleId;
                        ownedByRole[role] = ownedByRole.TryGetValue(role, out int count) ? count + 1 : 1;
                    }
                }
            }
        }

        public MatchSummary Summarize(int seed, string homePreset, string awayPreset)
        {
            string topRole = string.Empty; int topCount = 0; int ownedTotal = 0;
            foreach (System.Collections.Generic.KeyValuePair<string, int> kv in ownedByRole)
            {
                ownedTotal += kv.Value;
                if (kv.Value > topCount || (kv.Value == topCount && string.CompareOrdinal(kv.Key, topRole) < 0)) { topCount = kv.Value; topRole = kv.Key; }
            }
            return new MatchSummary
            {
                Seed = seed, HomePreset = homePreset, AwayPreset = awayPreset,
                HomeGoals = sim.HomeGoals, AwayGoals = sim.AwayGoals,
                HomeShots = homeShots, AwayShots = awayShots,
                Passes = sim.PassCount, Intercepts = sim.InterceptCount,
                TackleAttempts = sim.TackleAttemptCount, TackleSuccesses = sim.TackleSuccessCount, Turnovers = sim.TurnoverCount,
                HomeOwnedTicks = homeOwned, AwayOwnedTicks = awayOwned,
                HomeOppThirdTicks = homeOppThird, AwayOppThirdTicks = awayOppThird,
                WideOwnedTicks = wideOwned,
                TopRole = topRole, TopRoleShare = ownedTotal == 0 ? 0f : (float)topCount / ownedTotal,
                MeanShotAbsZ = homeShots + awayShots == 0 ? 0f : shotAbsZSum / (homeShots + awayShots),
            };
        }
    }
}
