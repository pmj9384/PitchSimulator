using System;
using System.Collections.Generic;
using Game.Core.League;
using NUnit.Framework;

// 리그 순수 함수(스펙 §10): 승점표·순위·승강 판정. 씬 없음
public class LeagueRulesTests
{
    private static readonly int[] Six = { 10, 11, 12, 13, 14, 15 };

    [Test]
    public void 승점은_승3_무1_패0이고_득실이_쌓인다()
    {
        var results = new List<MatchResult> { new MatchResult(10, 11, 2, 0), new MatchResult(12, 10, 1, 1), new MatchResult(11, 12, 0, 3) };
        LeagueTable table = LeagueTable.Standings(Six, results);

        LeagueRow a = table.Rows[table.RankOf(10)];
        Assert.AreEqual(2, a.Played); Assert.AreEqual(1, a.Won); Assert.AreEqual(1, a.Drawn); Assert.AreEqual(0, a.Lost);
        Assert.AreEqual(4, a.Points); Assert.AreEqual(3, a.GoalsFor); Assert.AreEqual(1, a.GoalsAgainst); Assert.AreEqual(2, a.GoalDifference);
        LeagueRow b = table.Rows[table.RankOf(11)];
        Assert.AreEqual(0, b.Points); Assert.AreEqual(2, b.Lost);
        Assert.AreEqual(6, table.Count, "경기 안 한 팀도 0으로 표에 있다");
    }

    [Test]
    public void 순위는_승점_골득실_다득점_팀id_순이다()
    {
        // 10: 1승(3-0) 승점3 득실+3 / 11: 1승(2-0) 승점3 득실+2 / 12: 1승(4-2) 승점3 득실+2 다득점 4 → 12가 11 위 / 13·14·15: 패
        var results = new List<MatchResult> { new MatchResult(10, 13, 3, 0), new MatchResult(11, 14, 2, 0), new MatchResult(12, 15, 4, 2) };
        LeagueTable table = LeagueTable.Standings(Six, results);

        Assert.AreEqual(10, table[0].TeamId, "골득실 +3");
        Assert.AreEqual(12, table[1].TeamId, "득실 같으면 다득점");
        Assert.AreEqual(11, table[2].TeamId);
        Assert.AreEqual(15, table[3].TeamId, "0점·득실 −2끼리는 다득점(15는 2골, 14는 0골)");
        Assert.AreEqual(14, table[4].TeamId);
        Assert.AreEqual(13, table[5].TeamId, "골득실 −3 최하위");
    }

    [Test]
    public void 전부_동률이면_팀id_순으로_결정적이다()
    {
        LeagueTable table = LeagueTable.Standings(Six, new List<MatchResult>());
        for (int i = 0; i < Six.Length; i++) { Assert.AreEqual(Six[i], table[i].TeamId); }
    }

    [Test]
    public void 승강_판정은_1위_승격_2_3위_PO_최하위_강등이고_1부_승격_4부_강등은_없다()
    {
        var results = new List<MatchResult> { new MatchResult(10, 15, 3, 0), new MatchResult(11, 14, 2, 0), new MatchResult(12, 13, 1, 0) };
        LeagueTable table = LeagueTable.Standings(Six, results);   // 10, 11, 12, 13(0:1), 14(0:2), 15(0:3)

        PromotionDecision third = LeagueRules.Decide(table, 3);
        Assert.AreEqual(10, third.AutoPromoted);
        Assert.AreEqual(11, third.PlayoffHome, "2위 홈");
        Assert.AreEqual(12, third.PlayoffAway);
        Assert.AreEqual(15, third.Relegated);

        PromotionDecision bottom = LeagueRules.Decide(table, LeagueRules.BottomTier);
        Assert.AreEqual(10, bottom.AutoPromoted);
        Assert.AreEqual(PromotionDecision.None, bottom.Relegated, "4부 강등 없음");

        PromotionDecision top = LeagueRules.Decide(table, LeagueRules.TopTier);
        Assert.AreEqual(PromotionDecision.None, top.AutoPromoted, "1부 승격 없음");
        Assert.AreEqual(PromotionDecision.None, top.PlayoffHome);
        Assert.AreEqual(15, top.Relegated);

        Assert.Throws<ArgumentOutOfRangeException>(() => LeagueRules.Decide(table, 5));
    }

    [Test]
    public void PO_단판은_무승부면_홈인_2위가_이긴다()
    {
        Assert.AreEqual(11, LeagueRules.PlayoffWinner(11, 12, 1, 1), "무승부 = 상위(홈)");
        Assert.AreEqual(12, LeagueRules.PlayoffWinner(11, 12, 0, 1));
        Assert.AreEqual(11, LeagueRules.PlayoffWinner(11, 12, 2, 0));
    }
}
