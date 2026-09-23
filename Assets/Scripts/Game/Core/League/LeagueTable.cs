using System.Collections.Generic;

namespace Game.Core.League
{
    // 승점표 한 행. 승 3·무 1·패 0(스펙 §10)
    public sealed class LeagueRow
    {
        public int TeamId { get; }
        public int Played { get; private set; }
        public int Won { get; private set; }
        public int Drawn { get; private set; }
        public int Lost { get; private set; }
        public int GoalsFor { get; private set; }
        public int GoalsAgainst { get; private set; }
        public int Points => Won * 3 + Drawn;
        public int GoalDifference => GoalsFor - GoalsAgainst;

        public LeagueRow(int teamId) { TeamId = teamId; }

        internal void Record(int scored, int conceded)
        {
            Played++;
            GoalsFor += scored;
            GoalsAgainst += conceded;
            if (scored > conceded) { Won++; } else if (scored == conceded) { Drawn++; } else { Lost++; }
        }
    }

    // 순위가 매겨진 승점표. Rows[0]이 1위. 순위 = 승점 → 골득실 → 다득점 → 팀 id(결정성).
    // 상대 전적은 안 넣는다(K리그 규정엔 있지만 5~11경기 단판 시즌엔 과함, 09-23)
    public sealed class LeagueTable
    {
        public IReadOnlyList<LeagueRow> Rows { get; }

        private LeagueTable(List<LeagueRow> rows) { Rows = rows; }

        public LeagueRow this[int rank] => Rows[rank];
        public int Count => Rows.Count;

        public int RankOf(int teamId)
        {
            for (int i = 0; i < Rows.Count; i++) { if (Rows[i].TeamId == teamId) { return i; } }
            return -1;
        }

        public static LeagueTable Standings(IReadOnlyList<int> teamIds, IReadOnlyList<MatchResult> results)
        {
            var byId = new Dictionary<int, LeagueRow>(teamIds.Count);
            var rows = new List<LeagueRow>(teamIds.Count);
            for (int i = 0; i < teamIds.Count; i++)
            {
                var row = new LeagueRow(teamIds[i]);
                byId[teamIds[i]] = row;
                rows.Add(row);
            }
            for (int i = 0; i < results.Count; i++)
            {
                MatchResult r = results[i];
                byId[r.HomeTeamId].Record(r.HomeGoals, r.AwayGoals);
                byId[r.AwayTeamId].Record(r.AwayGoals, r.HomeGoals);
            }
            rows.Sort(Compare);
            return new LeagueTable(rows);
        }

        private static int Compare(LeagueRow a, LeagueRow b)
        {
            if (a.Points != b.Points) { return b.Points.CompareTo(a.Points); }
            if (a.GoalDifference != b.GoalDifference) { return b.GoalDifference.CompareTo(a.GoalDifference); }
            if (a.GoalsFor != b.GoalsFor) { return b.GoalsFor.CompareTo(a.GoalsFor); }
            return a.TeamId.CompareTo(b.TeamId);
        }
    }
}
