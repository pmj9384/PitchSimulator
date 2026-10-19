using System.Collections.Generic;

namespace Game.Core.League
{
    // 일정표의 내 팀 한 행(스펙 §2-1 10-19 일정표). 날짜는 없다: 시뮬에 달력이 없어 라운드 번호만 쓴다
    public readonly struct FixtureRow
    {
        public readonly int Round;        // 0부터
        public readonly int OpponentId;
        public readonly bool IsHome;
        public readonly bool Played;
        public readonly int MyGoals;      // Played일 때만
        public readonly int TheirGoals;

        public FixtureRow(int round, int opponentId, bool isHome, bool played, int myGoals, int theirGoals)
        {
            Round = round;
            OpponentId = opponentId;
            IsHome = isHome;
            Played = played;
            MyGoals = myGoals;
            TheirGoals = theirGoals;
        }

        public bool Won => Played && MyGoals > TheirGoals;
        public bool Drawn => Played && MyGoals == TheirGoals;
        public bool Lost => Played && MyGoals < TheirGoals;
    }

    // 일정(SeasonSchedule) + 결과(SeasonState.Results)에서 내 팀 행만 라운드 순으로 뽑는다. 순수 함수.
    // 결과는 라운드마다 teamCount/2개가 순서대로 쌓이므로(SeasonState.AddRound) 라운드 r의 결과는 그 슬라이스 안에서 내 팀이 든 것을 찾는다
    public static class FixtureList
    {
        public static List<FixtureRow> Build(IReadOnlyList<Fixture> schedule, IReadOnlyList<MatchResult> results, int myTeamId, int teamCount)
        {
            int perRound = teamCount / 2;
            var rows = new List<FixtureRow>();
            for (int i = 0; i < schedule.Count; i++)
            {
                Fixture f = schedule[i];
                bool home = f.HomeTeamId == myTeamId;
                bool away = f.AwayTeamId == myTeamId;
                if (!home && !away) { continue; }
                int opponent = home ? f.AwayTeamId : f.HomeTeamId;
                MatchResult? result = MyResultOfRound(results, f.Round, perRound, myTeamId);
                if (result == null)
                {
                    rows.Add(new FixtureRow(f.Round, opponent, home, played: false, 0, 0));
                    continue;
                }
                MatchResult r = result.Value;
                int mine = r.HomeTeamId == myTeamId ? r.HomeGoals : r.AwayGoals;
                int theirs = r.HomeTeamId == myTeamId ? r.AwayGoals : r.HomeGoals;
                rows.Add(new FixtureRow(f.Round, opponent, home, played: true, mine, theirs));
            }
            rows.Sort(ByRound);
            return rows;
        }

        private static MatchResult? MyResultOfRound(IReadOnlyList<MatchResult> results, int round, int perRound, int myTeamId)
        {
            int start = round * perRound;
            int end = start + perRound;
            if (end > results.Count) { return null; }
            for (int i = start; i < end; i++)
            {
                MatchResult r = results[i];
                if (r.HomeTeamId == myTeamId || r.AwayTeamId == myTeamId) { return r; }
            }
            return null;
        }

        private static int ByRound(FixtureRow a, FixtureRow b)
        {
            return a.Round.CompareTo(b.Round);
        }
    }
}
