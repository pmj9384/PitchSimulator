using System.Collections.Generic;
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

// 슛 확률(xG 7변수 공개 모델 + 능력치 로짓 보정). 원문 검산값을 기준으로 잡고, 보정 방향과 범위를 본다
public class ShotProbabilityTests
{
    private const float Tol = 0.002f;

    private static float Neutral(float xToGoalLine, float z)
    {
        return MatchRules.ShotProbability(FieldBounds.HalfLength - xToGoalLine, z, +1, shooterShot: 50, keeperReflexes: 50, keeperDiving: 50);
    }

    [Test]
    public void 능력치가_중립이면_공개_모델_검산값과_같다()
    {
        Assert.AreEqual(0.1813f, Neutral(11f, 0f), Tol, "정면 11m(페널티 스팟)");
        Assert.AreEqual(0.0620f, Neutral(20f, 0f), Tol, "정면 20m");
        Assert.AreEqual(0.0253f, Neutral(30f, 0f), Tol, "정면 30m");
        Assert.AreEqual(0.0930f, Neutral(11f, 10f), Tol, "11m, 옆으로 10m");
    }

    [Test]
    public void 좌우_대칭이고_팀1도_같은_값이다()
    {
        Assert.AreEqual(Neutral(11f, 10f), Neutral(11f, -10f), 1e-5f);
        float team1 = MatchRules.ShotProbability(-(FieldBounds.HalfLength - 11f), 0f, -1, 50, 50, 50);
        Assert.AreEqual(Neutral(11f, 0f), team1, 1e-5f);
    }

    [Test]
    public void shot이_올리고_GK_reflexes_diving이_내린다()
    {
        float baseP = Neutral(11f, 0f);
        Assert.Greater(MatchRules.ShotProbability(FieldBounds.HalfLength - 11f, 0f, +1, 90, 50, 50), baseP);
        Assert.Less(MatchRules.ShotProbability(FieldBounds.HalfLength - 11f, 0f, +1, 50, 80, 40), baseP);
        Assert.AreEqual(baseP, MatchRules.ShotProbability(FieldBounds.HalfLength - 11f, 0f, +1, 90, 90, 90), 1e-5f, "슈터 +40과 GK 평균 +40은 상쇄");
    }

    [Test]
    public void 어떤_입력이어도_0과_1_사이다()
    {
        float best = MatchRules.ShotProbability(FieldBounds.HalfLength - 1f, 0f, +1, 100, 0, 0);
        float worst = MatchRules.ShotProbability(-FieldBounds.HalfLength, 30f, +1, 0, 100, 100);
        Assert.That(best, Is.InRange(0f, 1f));
        Assert.That(worst, Is.InRange(0f, 1f));
        Assert.Greater(best, worst);
    }

    [Test]
    public void 사거리_40m_밖은_확률_0이고_안은_양수다()
    {
        Assert.AreEqual(0f, Neutral(45f, 0f));
        Assert.AreEqual(0f, Neutral(30f, 27f), "√(30²+27²) ≈ 40.4m");
        Assert.Greater(Neutral(39f, 0f), 0f);
        Assert.Greater(Neutral(30f, 26f), 0f, "√(30²+26²) ≈ 39.7m");
    }

    [Test]
    public void 내_페널티_박스_판정()
    {
        // 팀 0(+X 공격)의 내 골은 -X. 골라인 -52.5에서 16.5m 안, 폭 ±20.15
        Assert.IsTrue(MatchRules.IsInOwnPenaltyBox(-50f, 0f, +1));
        Assert.IsTrue(MatchRules.IsInOwnPenaltyBox(-36f, 20.15f, +1), "경계 포함");
        Assert.IsFalse(MatchRules.IsInOwnPenaltyBox(-35.9f, 0f, +1), "깊이 밖");
        Assert.IsFalse(MatchRules.IsInOwnPenaltyBox(-50f, 20.2f, +1), "폭 밖");
        Assert.IsFalse(MatchRules.IsInOwnPenaltyBox(-53f, 0f, +1), "골라인 뒤");
        Assert.IsTrue(MatchRules.IsInOwnPenaltyBox(50f, 0f, -1), "팀 1의 내 골은 +X");
    }

    [Test]
    public void 압박받는_슛은_로짓만큼_확률이_줄고_0은_0이다()
    {
        // 09-29 수비 D3. 0.25를 로짓 0.4 깎으면 약 0.18(StatsBomb 예시와 같은 크기)
        Assert.AreEqual(0.182f, MatchRules.UnderPressure(0.25f, 0.4f), 0.002f);
        Assert.Less(MatchRules.UnderPressure(0.1f, 0.4f), 0.1f);
        Assert.AreEqual(0f, MatchRules.UnderPressure(0f, 0.4f));
    }

    [Test]
    public void 슛_블록_후보는_2m_안_슛_방향_20도_안의_가장_가까운_필드_선수다()
    {
        // 슈터 (40,0) → 골 (52.5,0). GK(id 9)는 제외
        var opp = new List<TargetInfo>
        {
            new TargetInfo(9, 41f, 0f),      // GK: 제외
            new TargetInfo(5, 41.5f, 0.2f),  // 1.5m, 거의 정면 → 후보
            new TargetInfo(6, 40.5f, 1.5f),  // 1.6m지만 71° 옆 → 제외
            new TargetInfo(7, 43f, 0f),      // 3m → 거리 밖
        };
        Assert.AreEqual(5, MatchRules.ShotBlocker(40f, 0f, 52.5f, 0f, opp, 9, 2f, 20f));
        Assert.AreEqual(-1, MatchRules.ShotBlocker(40f, 0f, 52.5f, 0f, new List<TargetInfo> { opp[2], opp[3] }, 9, 2f, 20f));
    }

    [Test]
    public void 막힌_공은_슛_반대_방향에서_블로커_쪽으로_60도_비틀어_튕긴다()
    {
        // 슛 +X, 블로커가 슛 라인 +Z 쪽 → 반대 방향(-X)을 +Z 쪽으로 비틀어 옆·뒤
        (float x, float z) d = MatchRules.BlockReboundDirection(40f, 0f, 52.5f, 0f, 41.5f, 0.3f, 60f);
        Assert.AreEqual(-0.5f, d.x, 1e-3f);
        Assert.AreEqual(0.866f, System.Math.Abs(d.z), 1e-3f);
        (float x, float z) other = MatchRules.BlockReboundDirection(40f, 0f, 52.5f, 0f, 41.5f, -0.3f, 60f);
        Assert.AreEqual(-d.z, other.z, 1e-3f, "반대쪽 블로커면 반대로");
    }
}
