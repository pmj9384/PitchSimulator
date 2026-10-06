using System;
using Game.Core.Tactics;

namespace Game.Core.Match
{
    // 한 팀이 이번 경기에 실제로 한 것(결과 화면 "설정 대 실측", 스펙 §6: 내 설정과 나란히 보여 "읽히는 전술"을 만든다).
    // 항목은 설정과 짝이 있는 것만 둔다: 패스 방식 ↔ 평균 패스 길이, 압박 시작 ↔ 공을 뺏은 곳, 역습 성향 ↔ 역습 횟수와 역습 슛.
    // 서드 배열의 순서는 TeamTactics와 같다([우리 진영, 중앙, 상대 진영])
    public sealed class TacticsReadout
    {
        private const int Thirds = 3;

        private readonly int[] passCount = new int[Thirds];
        private readonly float[] passLengthSum = new float[Thirds];
        private readonly int[] regainCount = new int[Thirds];

        public int Counters { get; private set; }       // 역습으로 판정된 소유 횟수
        public int CounterShots { get; private set; }   // 그 소유에서 나온 슛
        public int Shots { get; private set; }

        // 받은 패스 수. 서드는 찬 곳 기준(패스 방식 설정이 "공이 있는 서드"로 읽히는 것과 같게)
        public int PassCount(Third third)
        {
            return passCount[(int)third];
        }

        // 받은 패스의 평균 길이(m). 패스가 없으면 0
        public float MeanPassLength(Third third)
        {
            int count = passCount[(int)third];
            return count == 0 ? 0f : passLengthSum[(int)third] / count;
        }

        // 상대 공을 뺏은 횟수(가로채기 + 태클). 서드는 뺏은 곳 기준
        public int RegainCount(Third third)
        {
            return regainCount[(int)third];
        }

        internal void AddPass(Third third, float length)
        {
            passCount[(int)third]++;
            passLengthSum[(int)third] += length;
        }

        internal void AddRegain(Third third)
        {
            regainCount[(int)third]++;
        }

        internal void AddCounter()
        {
            Counters++;
        }

        internal void AddShot(bool duringCounter)
        {
            Shots++;
            if (duringCounter) { CounterShots++; }
        }
    }

    // 시뮬을 지켜보며 한 팀의 TacticsReadout을 채운다. 시뮬의 규칙은 건드리지 않고 이미 내는 보고(소유 변경·슛 결과)와 틱 뒤의 공 상태만 읽는다.
    // 쓰는 쪽(MatchManager)이 Tick 뒤마다 Sample을 부른다. MatchProbe(러너)와 따로 둔 이유: 저쪽은 양 팀 밸런스 한 줄, 이쪽은 한 팀의 전술 항목별 값이다
    public sealed class TacticsProbe
    {
        private readonly MatchSimulation sim;
        private readonly int team;

        // 우리 선수가 마지막으로 공을 쥐고 있던 자리 = 다음 패스의 킥 지점. 공을 잡은 순간(소유 변경 보고)과 틱 뒤(Sample) 두 곳에서 적는다:
        // 받자마자 같은 틱에 차는 패스는 틱 뒤에 보면 이미 날아가고 있어서, 잡은 순간에 안 적으면 앞 선수의 킥 지점부터 잰다
        private bool hasKickOrigin;
        private float kickOriginX;
        private float kickOriginZ;
        private Third kickOriginThird;
        private bool kickerIsGoalkeeper;

        private bool possessionCountered;   // 지금 우리 소유가 역습으로 판정된 적이 있는가. 상대가 공을 잡으면 지운다

        public TacticsReadout Readout { get; } = new TacticsReadout();

        public TacticsProbe(MatchSimulation sim, int team)
        {
            this.sim = sim;
            this.team = team;
            sim.PossessionChanged += OnPossessionChanged;
            sim.ShotResolved += OnShotResolved;
        }

        // Tick 뒤마다 한 번
        public void Sample()
        {
            BallState ball = sim.Ball;
            if (ball.Phase != BallPhase.Owned) { return; }

            PlayerState owner = sim.Players[ball.OwnerId];
            if (owner.Team != team) { return; }

            MarkKickOrigin(ball.X, ball.Z, owner);

            if (possessionCountered) { return; }
            if (!sim.IsCountering(team)) { return; }
            possessionCountered = true;
            Readout.AddCounter();
        }

        private void OnPossessionChanged(PossessionReport report)
        {
            if (report.NewOwnerTeam != team)
            {
                possessionCountered = false;
                hasKickOrigin = false;
                return;
            }

            if (report.Kind == PossessionChange.PassReceived)
            {
                AddReceivedPass(report);
            }
            else if (report.Kind == PossessionChange.Intercepted || report.Kind == PossessionChange.Turnover)
            {
                Readout.AddRegain(PositionRules.ThirdOf(report.X, sim.AttackSignOf(team)));
            }
            MarkKickOrigin(report.X, report.Z, sim.Players[report.NewOwnerId]);   // 받은 패스를 센 뒤에: 세는 데 앞 선수의 킥 지점을 쓴다
        }

        private void MarkKickOrigin(float x, float z, PlayerState owner)
        {
            hasKickOrigin = true;
            kickOriginX = x;
            kickOriginZ = z;
            kickOriginThird = PositionRules.ThirdOf(x, sim.AttackSignOf(team));
            kickerIsGoalkeeper = owner.IsGoalkeeper;
        }

        // GK 배급은 뺀다: 패스 방식이 아니라 GK 배급 설정을 따르는 패스라 섞으면 우리 진영 평균이 배급 길이에 끌려간다
        private void AddReceivedPass(PossessionReport report)
        {
            if (!hasKickOrigin) { return; }
            if (kickerIsGoalkeeper) { return; }

            float dx = report.X - kickOriginX;
            float dz = report.Z - kickOriginZ;
            Readout.AddPass(kickOriginThird, (float)Math.Sqrt(dx * dx + dz * dz));
        }

        private void OnShotResolved(ShotReport report)
        {
            if (sim.Players[report.ShooterId].Team != team) { return; }
            Readout.AddShot(possessionCountered);
        }
    }
}
