using System;
using System.Collections.Generic;

namespace Game.Core.League
{
    public readonly struct Fixture
    {
        public readonly int Round;      // 0부터
        public readonly int HomeTeamId;
        public readonly int AwayTeamId;

        public Fixture(int round, int homeTeamId, int awayTeamId)
        {
            Round = round;
            HomeTeamId = homeTeamId;
            AwayTeamId = awayTeamId;
        }
    }

    // 단판 라운드로빈 일정(스펙 §10: 4부 6팀 5경기 … 1부 12팀 11경기). 서클 방식: 0번을 고정하고 나머지를 한 칸씩 돌린다.
    // 결정적이라 세이브에 안 넣는다. 홈·어웨이는 라운드 홀짝으로 뒤집어 한 팀이 계속 홈이 되지 않게 한다
    public static class SeasonSchedule
    {
        public static List<Fixture> RoundRobin(int teamCount)
        {
            if (teamCount < 2 || teamCount % 2 != 0) { throw new ArgumentException($"팀 수는 2 이상 짝수여야 한다({teamCount})"); }

            var fixtures = new List<Fixture>(teamCount * (teamCount - 1) / 2);
            int rounds = teamCount - 1;
            int half = teamCount / 2;
            var ring = new int[teamCount - 1];   // 0번을 뺀 나머지
            for (int i = 0; i < ring.Length; i++) { ring[i] = i + 1; }

            for (int round = 0; round < rounds; round++)
            {
                // 0번 대 ring[0]. 홀수 라운드는 홈·어웨이를 바꾼다
                Add(fixtures, round, 0, ring[0], round % 2 == 1);
                for (int k = 1; k < half; k++)
                {
                    Add(fixtures, round, ring[k], ring[ring.Length - k], round % 2 == 1);
                }
                // 링을 한 칸 회전
                int last = ring[ring.Length - 1];
                for (int i = ring.Length - 1; i > 0; i--) { ring[i] = ring[i - 1]; }
                ring[0] = last;
            }
            return fixtures;
        }

        private static void Add(List<Fixture> fixtures, int round, int a, int b, bool swap)
        {
            fixtures.Add(swap ? new Fixture(round, b, a) : new Fixture(round, a, b));
        }

        public static void FixturesOfRound(IReadOnlyList<Fixture> all, int round, List<Fixture> into)
        {
            into.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Round == round) { into.Add(all[i]); }
            }
        }

        public static Fixture MyFixture(IReadOnlyList<Fixture> all, int round, int myTeamId)
        {
            for (int i = 0; i < all.Count; i++)
            {
                Fixture f = all[i];
                if (f.Round == round && (f.HomeTeamId == myTeamId || f.AwayTeamId == myTeamId)) { return f; }
            }
            throw new InvalidOperationException($"라운드 {round}에 팀 {myTeamId}의 경기가 없다");
        }
    }
}
