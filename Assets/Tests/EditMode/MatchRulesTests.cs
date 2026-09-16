using System;
using Game.Core.Match;
using Game.Core.Placement;
using NUnit.Framework;

// MatchRules 검증(09-16 확정 스펙). ①speed→m/s 선형 ②슛 기하: 페널티 스팟 11m·각도 2·atan(3.66/11), 옆으로 갈수록 각도 감소, 골라인 뒤 0
// ③캐치 확률 기본 0.65·handling 보정·클램프 ④Resolve 경계(roll == p는 실패)
public class MatchRulesTests
{
    private const float PenaltySpotX = FieldBounds.HalfLength - 11f;

    [Test]
    public void speed_50은_7mps이고_선형이다()
    {
        Assert.AreEqual(7f * MatchTuning.Tempo, MatchRules.SpeedMps(50), 1e-4f);
        Assert.AreEqual(3.5f * MatchTuning.Tempo, MatchRules.SpeedMps(25), 1e-4f);
        Assert.AreEqual(11.2f * MatchTuning.Tempo, MatchRules.SpeedMps(80), 1e-4f);
    }

    [Test]
    public void 페널티_스팟은_거리_11m_각도_2atan_3_66_over_11이다()
    {
        float expectedAngle = 2f * (float)Math.Atan(FieldBounds.GoalHalfWidth / 11f);   // 약 36.9°

        Assert.AreEqual(11f, MatchRules.ShotDistance(PenaltySpotX, 0f, +1), 1e-4f);
        Assert.AreEqual(expectedAngle, MatchRules.ShotAngle(PenaltySpotX, 0f, +1), 1e-4f);
    }

    [Test]
    public void 팀1은_반대_골을_노린다()
    {
        Assert.AreEqual(11f, MatchRules.ShotDistance(-PenaltySpotX, 0f, -1), 1e-4f);
        Assert.AreEqual(MatchRules.ShotAngle(PenaltySpotX, 0f, +1), MatchRules.ShotAngle(-PenaltySpotX, 0f, -1), 1e-5f);
    }

    [Test]
    public void 옆으로_갈수록_각도가_좁아지고_멀수록_좁아진다()
    {
        float center = MatchRules.ShotAngle(PenaltySpotX, 0f, +1);
        float side = MatchRules.ShotAngle(PenaltySpotX, 10f, +1);
        float far = MatchRules.ShotAngle(FieldBounds.HalfLength - 25f, 0f, +1);

        Assert.Less(side, center);
        Assert.Less(far, center);
    }

    [Test]
    public void 골라인_위나_뒤에서는_각도가_0이다()
    {
        Assert.AreEqual(0f, MatchRules.ShotAngle(FieldBounds.HalfLength, 0f, +1));
        Assert.AreEqual(0f, MatchRules.ShotAngle(FieldBounds.HalfLength + 1f, 0f, +1));
    }

    [Test]
    public void 캐치_확률은_기본_0_65에_handling이_보정한다()
    {
        Assert.AreEqual(0.65f, MatchRules.CatchProbability(50), 1e-5f);
        Assert.AreEqual(0.85f, MatchRules.CatchProbability(100), 1e-5f);
        Assert.AreEqual(0.45f, MatchRules.CatchProbability(0), 1e-5f);
        Assert.Greater(MatchRules.CatchProbability(60), MatchRules.CatchProbability(50));
    }

    [Test]
    public void Resolve는_roll이_p보다_작을_때만_성공이고_경계는_실패다()
    {
        Assert.IsTrue(MatchRules.Resolve(0.3f, 0.29f));
        Assert.IsFalse(MatchRules.Resolve(0.3f, 0.3f), "roll == p는 실패. p = 0이 절대 성공하지 않게");
        Assert.IsFalse(MatchRules.Resolve(0f, 0f));
        Assert.IsTrue(MatchRules.Resolve(1f, 0.999f));
    }
}
