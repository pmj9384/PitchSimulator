using System.Collections.Generic;
using Game.Core.League;
using NUnit.Framework;

// 홈 한 줄 소식(10-22): ①시즌 시작 전엔 스카우트 소식뿐, 후보 ★이 없으면 없음 ②같은 입력 = 같은 소식 ③다른 경기 소식에 내 팀은 안 나온다
// ④순위 소식은 LeagueTable과 같다 ⑤시드·라운드가 바뀌면 종류가 섞인다(한 종류로 굳지 않는다)
public class HomeNewsTests
{
    private const int Teams = 6;
    private const int Me = 0;
    private static readonly List<Fixture> Schedule = SeasonSchedule.RoundRobin(Teams);

    // 라운드 r까지 치른 결과. 점수는 (라운드, 경기 번호)로 정해 두고 내 팀은 늘 2-0으로 이긴다
    private static List<MatchResult> ResultsUpTo(int rounds)
    {
        var results = new List<MatchResult>();
        var fixtures = new List<Fixture>();
        for (int round = 0; round < rounds; round++)
        {
            fixtures.Clear();
            SeasonSchedule.FixturesOfRound(Schedule, round, fixtures);
            for (int i = 0; i < fixtures.Count; i++)
            {
                Fixture f = fixtures[i];
                bool mine = f.HomeTeamId == Me || f.AwayTeamId == Me;
                int home = mine ? (f.HomeTeamId == Me ? 2 : 0) : (round + i) % 3;
                int away = mine ? (f.AwayTeamId == Me ? 2 : 0) : (round * 2 + i) % 2;
                results.Add(new MatchResult(f.HomeTeamId, f.AwayTeamId, home, away));
            }
        }
        return results;
    }

    [Test]
    public void 시즌_시작_전엔_스카우트_소식만_있고_후보_별이_없으면_없음()
    {
        NewsItem item = HomeNews.Pick(42, 0, Schedule, new List<MatchResult>(), Me, Teams, bestCandidateStars: 3);
        Assert.AreEqual(NewsKind.ScoutCandidate, item.Kind);
        Assert.AreEqual(3, item.Stars);
        Assert.AreEqual(NewsKind.None, HomeNews.Pick(42, 0, Schedule, new List<MatchResult>(), Me, Teams, bestCandidateStars: 0).Kind);
    }

    [Test]
    public void 같은_입력이면_같은_소식이다()
    {
        List<MatchResult> results = ResultsUpTo(3);
        NewsItem a = HomeNews.Pick(42, 3, Schedule, results, Me, Teams, 1);
        NewsItem b = HomeNews.Pick(42, 3, Schedule, results, Me, Teams, 1);
        Assert.AreEqual(a.Kind, b.Kind);
        Assert.AreEqual(a.HomeTeamId, b.HomeTeamId);
        Assert.AreEqual(a.Rank, b.Rank);
        Assert.AreEqual(a.Stars, b.Stars);
    }

    [Test]
    public void 다른_경기_소식에_내_팀은_안_나오고_지난_라운드_경기다()
    {
        int seen = 0;
        for (int seed = 0; seed < 40; seed++)
        {
            for (int round = 1; round <= 5; round++)
            {
                List<MatchResult> results = ResultsUpTo(round);
                NewsItem item = HomeNews.Pick(seed, round, Schedule, results, Me, Teams, 1);
                if (item.Kind != NewsKind.OtherResult) { continue; }
                seen++;
                Assert.AreNotEqual(Me, item.HomeTeamId);
                Assert.AreNotEqual(Me, item.AwayTeamId);
                var lastRound = new List<Fixture>();
                SeasonSchedule.FixturesOfRound(Schedule, round - 1, lastRound);
                Assert.IsTrue(lastRound.Exists(f => f.HomeTeamId == item.HomeTeamId && f.AwayTeamId == item.AwayTeamId), $"seed {seed} round {round}: 지난 라운드 경기가 아니다");
            }
        }
        Assert.Greater(seen, 0, "다른 경기 소식이 한 번도 안 나왔다");
    }

    [Test]
    public void 순위_소식은_LeagueTable과_같다()
    {
        var ids = new List<int> { 0, 1, 2, 3, 4, 5 };
        int seen = 0;
        for (int seed = 0; seed < 40; seed++)
        {
            for (int round = 1; round <= 5; round++)
            {
                List<MatchResult> results = ResultsUpTo(round);
                NewsItem item = HomeNews.Pick(seed, round, Schedule, results, Me, Teams, 1);
                if (item.Kind != NewsKind.RankChange) { continue; }
                seen++;
                Assert.AreEqual(LeagueTable.Standings(ids, results).RankOf(Me), item.Rank);
                Assert.AreEqual(LeagueTable.Standings(ids, ResultsUpTo(round - 1)).RankOf(Me), item.PreviousRank);
            }
        }
        Assert.Greater(seen, 0, "순위 소식이 한 번도 안 나왔다");
    }

    [Test]
    public void 시드와_라운드에_따라_종류가_섞인다()
    {
        var kinds = new HashSet<NewsKind>();
        for (int seed = 0; seed < 10; seed++)
        {
            for (int round = 1; round <= 5; round++)
            {
                kinds.Add(HomeNews.Pick(seed, round, Schedule, ResultsUpTo(round), Me, Teams, 2).Kind);
            }
        }
        Assert.AreEqual(3, kinds.Count, "세 종류가 다 나와야 한다");
    }

    [Test]
    public void 후보_별이_없으면_스카우트_차례에도_다른_종류로_넘어간다()
    {
        for (int seed = 0; seed < 40; seed++)
        {
            NewsItem item = HomeNews.Pick(seed, 2, Schedule, ResultsUpTo(2), Me, Teams, bestCandidateStars: 0);
            Assert.AreNotEqual(NewsKind.ScoutCandidate, item.Kind);
            Assert.AreNotEqual(NewsKind.None, item.Kind);
        }
    }
}
