using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Placement;
using NUnit.Framework;

// 배치 판정(스펙 §8, WarTableSimulator에서 10-08 이식): ①내 진영 절반(팀 0은 x < 0, 여백 0.5) ②최소 간격 1.2m 경계 포함 ③영역이 간격보다 먼저
// ④SeasonState.MoveLineupSlot은 공격 시/수비 시 한쪽만 바꾸고 안 되면 던지며 세이브로 왕복한다
public class PlacementRulesTests
{
    private static List<PlayerStats> table;
    private static List<StageEntry> myRows;

    [OneTimeSetUp]
    public void Load()
    {
        table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        myRows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(r => r.Stage == 1 && r.Team == 0);
    }

    [Test]
    public void 내_진영_절반은_팀0이_x_음수_쪽이고_중앙선과_가장자리_여백은_밖이다()
    {
        Assert.IsTrue(PlacementRules.IsInsideOwnHalf(-10f, 0f, 0));
        Assert.IsFalse(PlacementRules.IsInsideOwnHalf(10f, 0f, 0), "상대 진영");
        Assert.IsFalse(PlacementRules.IsInsideOwnHalf(0f, 0f, 0), "중앙선");
        Assert.IsFalse(PlacementRules.IsInsideOwnHalf(-0.3f, 0f, 0), "중앙선 여백 안");
        Assert.IsFalse(PlacementRules.IsInsideOwnHalf(-52.3f, 0f, 0), "골라인 여백 안");
        Assert.IsFalse(PlacementRules.IsInsideOwnHalf(-10f, 33.8f, 0), "터치라인 여백 안");
        Assert.IsTrue(PlacementRules.IsInsideOwnHalf(10f, 0f, 1), "팀 1은 반대");
    }

    [Test]
    public void 최소_간격은_경계를_포함하고_영역이_간격보다_먼저다()
    {
        var occupied = new List<(float X, float Z)> { (-10f, 0f) };
        Assert.IsTrue(PlacementRules.IsClearOf(-10f, 1.2f, occupied, 1.2f), "정확히 간격만큼 = 허용");
        Assert.IsFalse(PlacementRules.IsClearOf(-10f, 1.1f, occupied, 1.2f));
        Assert.AreEqual(PlacementVerdict.Ok, PlacementRules.Evaluate(-20f, 5f, 0, occupied, 1.2f));
        Assert.AreEqual(PlacementVerdict.TooClose, PlacementRules.Evaluate(-10.5f, 0f, 0, occupied, 1.2f));
        Assert.AreEqual(PlacementVerdict.OutsideOwnHalf, PlacementRules.Evaluate(10f, 0f, 0, new List<(float X, float Z)> { (10f, 0f) }, 1.2f), "영역 밖이면 간격은 안 본다");
    }

    [Test]
    public void 라인업_자리_옮기기는_한쪽만_바꾸고_안_되면_던지며_세이브로_왕복한다()
    {
        SeasonState state = SeasonState.NewSeason(4, 42, myRows, table);
        LineupEntry before = state.Lineup[0];
        int id = before.PlayerId;

        state.MoveLineupSlot(id, defending: false, -30f, 20f);
        LineupEntry after = state.Lineup[0];
        Assert.AreEqual(-30f, after.AttackX);
        Assert.AreEqual(20f, after.AttackZ);
        Assert.AreEqual(before.DefendX, after.DefendX, "수비 시 자리는 그대로");
        Assert.AreEqual(before.DefendZ, after.DefendZ);

        state.MoveLineupSlot(id, defending: true, -45f, -10f);
        Assert.AreEqual(-45f, state.Lineup[0].DefendX);
        Assert.AreEqual(-30f, state.Lineup[0].AttackX, "공격 시 자리는 그대로");

        Assert.Throws<System.InvalidOperationException>(() => state.MoveLineupSlot(id, false, 5f, 0f), "상대 진영");
        LineupEntry other = state.Lineup[1];
        Assert.Throws<System.InvalidOperationException>(() => state.MoveLineupSlot(id, false, other.AttackX + 0.5f, other.AttackZ), "다른 선수와 겹침");
        Assert.AreEqual(PlacementVerdict.TooClose, state.EvaluateLineupSlot(id, false, other.AttackX + 0.5f, other.AttackZ));
        Assert.AreEqual(-30f, state.Lineup[0].AttackX, "실패하면 안 바뀐다");
        Assert.Throws<System.InvalidOperationException>(() => state.MoveLineupSlot(99, false, -30f, 0f), "없는 선수");

        SeasonState back = SeasonState.FromSave(state.ToSave(), table);
        Assert.AreEqual(-30f, back.Lineup[0].AttackX);
        Assert.AreEqual(-45f, back.Lineup[0].DefendX);
        Assert.AreEqual(state.MyLineup()[0].AttackX, back.MyLineup()[0].AttackX, "경기 재료도 옮긴 자리를 쓴다");
    }
}
