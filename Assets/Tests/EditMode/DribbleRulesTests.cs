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
        return DribbleRules.TakeOnTarget(x, z, +1, opponents, Carry, MatchTuning.InterceptRunSpeed, MatchTuning.TackleRange, MatchTuning.ReactionTime,
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
    public void 운반_끝점은_골_쪽으로_구간만큼_가고_골_가까이서도_골_중심에_붙지_않는다()
    {
        (float x, float z) far = DribbleRules.CarryEnd(0f, 0f, 52.5f, 0f, 8f, 52.5f, 34f);
        Assert.AreEqual(8f, far.x, 1e-4f);
        Assert.AreEqual(0f, far.z, 1e-4f);

        (float x, float z) diagonal = DribbleRules.CarryEnd(0f, 30f, 40f, 0f, 10f, 52.5f, 34f);
        Assert.AreEqual(8f, diagonal.x, 1e-3f, "골 중심 방향 단위벡터 (0.8, -0.6) × 10");
        Assert.AreEqual(24f, diagonal.z, 1e-3f);

        (float x, float z) near = DribbleRules.CarryEnd(40f, 10f, 52.5f, 0f, 8f, 52.5f, 34f);
        Assert.Less(near.x, 52.5f, "골 가까이서도 구간만큼만 간다(골 중심 스냅 없음)");
        Assert.AreEqual(8f, System.MathF.Sqrt((near.x - 40f) * (near.x - 40f) + (near.z - 10f) * (near.z - 10f)), 1e-3f);
    }

    [Test]
    public void 골까지가_구간보다_짧으면_골_중심에서_멈추고_실행_방향과_같다()
    {
        // 11-05 리뷰 Y1: (40,10)에서 구간 30이면 골 중심(52.5, 0)을 지나친 점을 x·z 따로 자르면 (52.5, -8.75)로 꺾였다
        (float x, float z) end = DribbleRules.CarryEnd(40f, 10f, 52.5f, 0f, 30f, 52.5f, 34f);
        Assert.AreEqual(52.5f, end.x, 1e-3f);
        Assert.AreEqual(0f, end.z, 1e-3f);

        // 실행(RunToward 골 중심)과 같은 방향: 끝점이 패서에서 골 중심으로 가는 직선 위에 있다
        float cross = (end.x - 40f) * (0f - 10f) - (end.z - 10f) * (52.5f - 40f);
        Assert.AreEqual(0f, cross, 1e-2f);

        // 구간 안이면 원래대로 구간만큼만
        (float x, float z) shorter = DribbleRules.CarryEnd(40f, 10f, 52.5f, 0f, 8f, 52.5f, 34f);
        Assert.AreEqual(8f, System.MathF.Sqrt((shorter.x - 40f) * (shorter.x - 40f) + (shorter.z - 10f) * (shorter.z - 10f)), 1e-3f);
    }

    [Test]
    public void 운반_끝점은_골라인과_터치라인_밖으로_나가지_않는다()
    {
        (float x, float z) pastGoalLine = DribbleRules.CarryEnd(50f, 0f, 52.5f, 0f, 8f, 52.5f, 34f);
        Assert.AreEqual(52.5f, pastGoalLine.x, 1e-4f);

        (float x, float z) pastTouchline = DribbleRules.CarryEnd(0f, 30f, 0f, 60f, 8f, 52.5f, 34f);
        Assert.AreEqual(34f, pastTouchline.z, 1e-4f);

        (float x, float z) atGoal = DribbleRules.CarryEnd(52.5f, 0f, 52.5f, 0f, 8f, 52.5f, 34f);
        Assert.AreEqual(52.5f, atGoal.x, 1e-4f, "골 중심에 이미 있으면 제자리");
    }

    [Test]
    public void 운반_위험은_앞_구간_안_상대가_가까울수록_크고_구간_밖이면_0이다()
    {
        float scale = 1f;
        var none = new List<TargetInfo>();
        var outside = new List<TargetInfo> { new TargetInfo(20, 9f, 0f) };
        var far = new List<TargetInfo> { new TargetInfo(20, 3f, 0f) };
        var near = new List<TargetInfo> { new TargetInfo(20, 1f, 0f) };

        Assert.AreEqual(0f, DribbleRules.CarryRisk(0f, 0f, 8f, 0f, none, 4f, 2f, scale));
        Assert.AreEqual(0f, DribbleRules.CarryRisk(0f, 0f, 8f, 0f, outside, 4f, 2f, scale), "구간 밖 상대는 무관");
        Assert.AreEqual(0.25f, DribbleRules.CarryRisk(0f, 0f, 8f, 0f, far, 4f, 2f, scale), 1e-5f, "1 − 3 ÷ 4");
        Assert.AreEqual(0.75f, DribbleRules.CarryRisk(0f, 0f, 8f, 0f, near, 4f, 2f, scale), 1e-5f, "1 − 1 ÷ 4");
    }

    [Test]
    public void 운반_위험은_옆_상대에겐_0이고_붙은_압박은_뒤에서도_거리_항을_쓴다()
    {
        var side = new List<TargetInfo> { new TargetInfo(20, 0f, 3f) };
        var behindFar = new List<TargetInfo> { new TargetInfo(20, -3f, 0f) };
        var behindGlued = new List<TargetInfo> { new TargetInfo(20, -1f, 0f) };

        Assert.AreEqual(0f, DribbleRules.CarryRisk(0f, 0f, 8f, 0f, side, 4f, 2f, 1f), "운반 방향과 직각이면 무관");
        Assert.AreEqual(0f, DribbleRules.CarryRisk(0f, 0f, 8f, 0f, behindFar, 4f, 2f, 1f), "압박 반경 밖 뒤쪽은 무관");
        Assert.AreEqual(0.75f, DribbleRules.CarryRisk(0f, 0f, 8f, 0f, behindGlued, 4f, 2f, 1f), 1e-5f, "압박 반경 안이면 각과 무관하게 거리 항");
    }

    [Test]
    public void 운반_위험_계수는_곱하고_1로_자른다()
    {
        var near = new List<TargetInfo> { new TargetInfo(20, 2f, 0f) };
        Assert.AreEqual(0.25f, DribbleRules.CarryRisk(0f, 0f, 8f, 0f, near, 4f, 0.5f, 0.5f), 1e-5f);
        Assert.AreEqual(1f, DribbleRules.CarryRisk(0f, 0f, 8f, 0f, near, 4f, 0.5f, 4f));
    }

    [Test]
    public void 돌파_확률은_드리블_성향에_비례하고_0_1로_자른다()
    {
        Assert.AreEqual(0.36f, DribbleRules.TakeOnChance(0.9f, 0.4f), 1e-5f);
        Assert.AreEqual(0f, DribbleRules.TakeOnChance(0f, 0.4f));
        Assert.AreEqual(1f, DribbleRules.TakeOnChance(1f, 5f));
    }
}
