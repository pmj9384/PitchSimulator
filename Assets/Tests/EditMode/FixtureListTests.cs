using System.Collections.Generic;
using Game.Core.League;
using NUnit.Framework;

// 일정표(스펙 §2-1 10-19): ①내 팀 행은 라운드 수만큼 라운드 순 ②홈·원정이 일정과 같다 ③치른 라운드만 점수가 있고 내 팀 기준으로 뒤집힌다 ④안 치른 라운드는 Played false
public class FixtureListTests
{
    private const int Me = SeasonState.MyTeamId;

    [Test]
    public void 내_팀_행은_라운드_수만큼_라운드_순이고_홈_원정이_일정과_같다()
    {
        List<Fixture> schedule = SeasonSchedule.RoundRobin(6);
        List<FixtureRow> rows = FixtureList.Build(schedule, new List<MatchResult>(), Me, 6);

        Assert.AreEqual(5, rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            Assert.AreEqual(i, rows[i].Round, "라운드 순");
            Assert.IsFalse(rows[i].Played);
            Fixture f = schedule.Find(x => x.Round == i && (x.HomeTeamId == Me || x.AwayTeamId == Me));
            Assert.AreEqual(f.HomeTeamId == Me, rows[i].IsHome);
            Assert.AreEqual(f.HomeTeamId == Me ? f.AwayTeamId : f.HomeTeamId, rows[i].OpponentId);
        }
    }

    [Test]
    public void 치른_라운드만_점수가_있고_내_팀_기준으로_뒤집히며_다른_경기는_안_섞인다()
    {
        List<Fixture> schedule = SeasonSchedule.RoundRobin(6);
        var results = new List<MatchResult>();
        for (int round = 0; round < 2; round++)
        {
            // 라운드 결과는 내 경기가 맨 앞이 아니어도 된다: 슬라이스 안에서 내 팀을 찾는다
            foreach (Fixture f in schedule.FindAll(x => x.Round == round))
            {
                bool mine = f.HomeTeamId == Me || f.AwayTeamId == Me;
                results.Add(new MatchResult(f.HomeTeamId, f.AwayTeamId, mine ? 2 : 9, mine ? 1 : 9));
            }
        }
        List<FixtureRow> rows = FixtureList.Build(schedule, results, Me, 6);

        Assert.IsTrue(rows[0].Played);
        Assert.IsTrue(rows[1].Played);
        Assert.IsFalse(rows[2].Played, "3라운드는 아직");
        for (int i = 0; i < 2; i++)
        {
            int expectedMine = rows[i].IsHome ? 2 : 1;
            int expectedTheirs = rows[i].IsHome ? 1 : 2;
            Assert.AreEqual(expectedMine, rows[i].MyGoals, $"R{i + 1} 내 골(홈이면 2, 원정이면 1)");
            Assert.AreEqual(expectedTheirs, rows[i].TheirGoals);
            Assert.AreNotEqual(9, rows[i].MyGoals, "남의 경기 점수가 섞이지 않는다");
        }
        Assert.AreEqual(rows[0].IsHome, rows[0].Won, "홈이면 2-1 승, 원정이면 1-2 패");
    }

    [Test]
    public void 무승부와_패배가_갈리고_라운드가_반만_쌓인_결과는_안_치른_것으로_본다()
    {
        List<Fixture> schedule = SeasonSchedule.RoundRobin(6);
        var results = new List<MatchResult>();
        Fixture r0 = schedule.Find(x => x.Round == 0 && (x.HomeTeamId == Me || x.AwayTeamId == Me));
        results.Add(new MatchResult(r0.HomeTeamId, r0.AwayTeamId, 1, 1));   // R1 무
        for (int k = 1; k < 3; k++) { results.Add(new MatchResult(100 + k, 200 + k, 0, 0)); }   // R1 나머지 2경기(6팀 = 라운드당 3경기)
        Fixture r1 = schedule.Find(x => x.Round == 1 && (x.HomeTeamId == Me || x.AwayTeamId == Me));
        int myGoals = 0;
        int theirGoals = 3;
        results.Add(r1.HomeTeamId == Me ? new MatchResult(r1.HomeTeamId, r1.AwayTeamId, myGoals, theirGoals) : new MatchResult(r1.HomeTeamId, r1.AwayTeamId, theirGoals, myGoals));   // R2 패, 그런데 라운드가 아직 반만 쌓임

        List<FixtureRow> rows = FixtureList.Build(schedule, results, Me, 6);
        Assert.IsTrue(rows[0].Drawn);
        Assert.IsFalse(rows[0].Won);
        Assert.IsFalse(rows[0].Lost);
        Assert.IsFalse(rows[1].Played, "라운드 결과가 다 쌓이기 전엔 안 치른 것으로 본다(AddRound는 라운드 통째로 넣는다)");
    }
}
