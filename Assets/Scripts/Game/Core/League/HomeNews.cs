using System.Collections.Generic;

namespace Game.Core.League
{
    public enum NewsKind
    {
        None,
        OtherResult,      // 지난 라운드 다른 팀 경기 결과
        RankChange,       // 내 순위와 지난 라운드 대비 변동
        ScoutCandidate,   // 스카우트 후보 중 가장 높은 ★
    }

    // 홈 한 줄 소식의 재료. 글자(팀 이름·문장)는 화면이 만든다
    public readonly struct NewsItem
    {
        public static readonly NewsItem None = default;

        public readonly NewsKind Kind;
        public readonly int HomeTeamId;    // OtherResult
        public readonly int AwayTeamId;
        public readonly int HomeGoals;
        public readonly int AwayGoals;
        public readonly int Rank;          // RankChange. 0부터
        public readonly int PreviousRank;
        public readonly int Stars;         // ScoutCandidate

        private NewsItem(NewsKind kind, int homeTeamId, int awayTeamId, int homeGoals, int awayGoals, int rank, int previousRank, int stars)
        {
            Kind = kind;
            HomeTeamId = homeTeamId;
            AwayTeamId = awayTeamId;
            HomeGoals = homeGoals;
            AwayGoals = awayGoals;
            Rank = rank;
            PreviousRank = previousRank;
            Stars = stars;
        }

        public static NewsItem OfResult(MatchResult r)
        {
            return new NewsItem(NewsKind.OtherResult, r.HomeTeamId, r.AwayTeamId, r.HomeGoals, r.AwayGoals, 0, 0, 0);
        }

        public static NewsItem OfRank(int rank, int previousRank)
        {
            return new NewsItem(NewsKind.RankChange, 0, 0, 0, 0, rank, previousRank, 0);
        }

        public static NewsItem OfScout(int stars)
        {
            return new NewsItem(NewsKind.ScoutCandidate, 0, 0, 0, 0, 0, 0, stars);
        }
    }

    // 홈 경기 사이 한 줄 소식(10-19 유저 "달력처럼 지나가게" → 조사 뒤 A안, 10-22 구현). 직행 버튼은 그대로 두고 사이가 비지 않게 한 줄만 얹는다.
    // 근거 `실험/2026-10-19-달력-진행-레퍼런스.md`: FM Continue는 결정이 있는 곳에서만 멈추고, 사이가 비면 "체크리스트"가 된다.
    // 순수 함수·시드 결정적: (시즌 시드, 치른 라운드)로 종류를 고르고, 그 종류의 재료가 없으면 다음 종류로 넘어간다. 세이브에 안 둔다
    public static class HomeNews
    {
        private const int KindCount = 3;
        private const uint KindSalt = 11u;     // ScoutRules.Hash와 입력 수가 같아 소금으로 값을 떼어 놓는다
        private const uint PickSalt = 23u;

        public static NewsItem Pick(int seasonSeed, int roundsPlayed, IReadOnlyList<Fixture> schedule, IReadOnlyList<MatchResult> results,
            int myTeamId, int teamCount, int bestCandidateStars)
        {
            if (roundsPlayed <= 0) { return Scout(bestCandidateStars); }

            int first = (int)(ScoutRules.Hash((uint)seasonSeed, (uint)roundsPlayed, KindSalt) % KindCount);
            for (int i = 0; i < KindCount; i++)
            {
                NewsItem item = Build((first + i) % KindCount, seasonSeed, roundsPlayed, results, myTeamId, teamCount, bestCandidateStars);
                if (item.Kind != NewsKind.None) { return item; }
            }
            return NewsItem.None;
        }

        private static NewsItem Build(int kind, int seasonSeed, int roundsPlayed, IReadOnlyList<MatchResult> results, int myTeamId, int teamCount, int bestCandidateStars)
        {
            if (kind == 0) { return OtherResult(seasonSeed, roundsPlayed, results, myTeamId, teamCount); }
            if (kind == 1) { return RankChange(results, myTeamId, teamCount); }
            return Scout(bestCandidateStars);
        }

        // 지난 라운드 결과 중 내 경기를 뺀 것에서 하나. 결과는 라운드마다 teamCount/2개가 순서대로 쌓인다(FixtureList와 같은 전제)
        private static NewsItem OtherResult(int seasonSeed, int roundsPlayed, IReadOnlyList<MatchResult> results, int myTeamId, int teamCount)
        {
            int perRound = teamCount / 2;
            int start = results.Count - perRound;
            if (start < 0) { return NewsItem.None; }

            var others = new List<MatchResult>(perRound);
            for (int i = start; i < results.Count; i++)
            {
                MatchResult r = results[i];
                if (r.HomeTeamId == myTeamId || r.AwayTeamId == myTeamId) { continue; }
                others.Add(r);
            }
            if (others.Count == 0) { return NewsItem.None; }

            int pick = (int)(ScoutRules.Hash((uint)seasonSeed, (uint)roundsPlayed, PickSalt) % (uint)others.Count);
            return NewsItem.OfResult(others[pick]);
        }

        // 지금 순위와 지난 라운드 전 순위. 한 라운드도 안 쌓였으면 없음
        private static NewsItem RankChange(IReadOnlyList<MatchResult> results, int myTeamId, int teamCount)
        {
            int perRound = teamCount / 2;
            if (results.Count < perRound) { return NewsItem.None; }

            var ids = new List<int>(teamCount);
            for (int i = 0; i < teamCount; i++) { ids.Add(i); }
            var before = new List<MatchResult>(results.Count - perRound);
            for (int i = 0; i < results.Count - perRound; i++) { before.Add(results[i]); }

            int rank = LeagueTable.Standings(ids, results).RankOf(myTeamId);
            int previous = LeagueTable.Standings(ids, before).RankOf(myTeamId);
            return NewsItem.OfRank(rank, previous);
        }

        private static NewsItem Scout(int bestCandidateStars)
        {
            if (bestCandidateStars <= 0) { return NewsItem.None; }
            return NewsItem.OfScout(bestCandidateStars);
        }
    }
}
