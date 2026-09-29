using System.Collections.Generic;
using Game.Core.Match;
using Game.Core.Placement;
using NUnit.Framework;

// 드리블 돌파(09-29). 방향 고르기는 HELIOS 드리블 생성기 방식: 상대가 먼저 닿는 후보를 버리고 남은 것 중 가장 앞으로
public class DribbleRulesTests
{
    private const float Carry = 8.4f;   // W speed 80 × 7/50 × 드리블 배율 0.75

    private static (float x, float z) Aim(float x, float z, List<TargetInfo> opponents)
    {
        return DribbleRules.TakeOnTarget(x, z, +1, opponents, Carry, MatchTuning.InterceptRunSpeed, MatchTuning.TackleRange,
            MatchTuning.TakeOnLookahead, MatchTuning.TakeOnAngleStep, FieldBounds.HalfLength, FieldBounds.HalfWidth - MatchTuning.TouchlineMargin);
    }

    [Test]
    public void 앞에_상대가_없으면_정면으로_간다()
    {
        (float x, float z) aim = Aim(10f, 0f, new List<TargetInfo>());
        Assert.AreEqual(10f + MatchTuning.TakeOnLookahead, aim.x, 1e-3f);
        Assert.AreEqual(0f, aim.z, 1e-3f);
    }

    [Test]
    public void 정면에_수비수가_서_있으면_옆으로_돌아_제친다()
    {
        // 수비수 정면 5m. 정면 4m 지점은 수비수가 먼저 닿는다 → 비껴 가는 방향
        (float x, float z) aim = Aim(10f, 0f, new List<TargetInfo> { new TargetInfo(20, 15f, 0f) });
        Assert.Greater(System.Math.Abs(aim.z), 1f, "정면이 아니라 옆으로 비낀다");
        Assert.Greater(aim.x, 10f - 1e-3f, "뒤로 물러나지는 않는다");
    }

    [Test]
    public void 수비수가_한쪽에_치우쳐_있으면_반대쪽으로_비낀다()
    {
        (float x, float z) aim = Aim(10f, 0f, new List<TargetInfo> { new TargetInfo(20, 14f, 1.5f) });
        Assert.Less(aim.z, 0f, "수비수가 +z 쪽이면 -z 쪽으로");
    }

    [Test]
    public void 터치라인_밖_후보는_버린다()
    {
        // 터치라인(여유 3m 안쪽 = 31) 바로 앞에서 +z 쪽 수비수를 만나도 라인 밖으로는 안 간다
        (float x, float z) aim = Aim(10f, 30.5f, new List<TargetInfo> { new TargetInfo(20, 14f, 30.5f) });
        Assert.LessOrEqual(aim.z, FieldBounds.HalfWidth - MatchTuning.TouchlineMargin + 1e-3f);
    }

    [Test]
    public void 앞쪽_판정은_공격_방향_앞의_사거리_안만()
    {
        var opp = new List<TargetInfo> { new TargetInfo(20, 5f, 0f) };
        Assert.IsTrue(DribbleRules.HasDefenderAhead(0f, 0f, +1, opp, 8f));
        Assert.IsFalse(DribbleRules.HasDefenderAhead(0f, 0f, -1, opp, 8f), "반대 방향 공격이면 뒤");
        Assert.IsFalse(DribbleRules.HasDefenderAhead(0f, 0f, +1, opp, 4f), "사거리 밖");
    }

    [Test]
    public void 돌파_확률은_드리블_성향에_비례하고_0_1로_자른다()
    {
        Assert.AreEqual(0.36f, DribbleRules.TakeOnChance(0.9f, 0.4f), 1e-5f);
        Assert.AreEqual(0f, DribbleRules.TakeOnChance(0f, 0.4f));
        Assert.AreEqual(1f, DribbleRules.TakeOnChance(1f, 5f));
    }
}
