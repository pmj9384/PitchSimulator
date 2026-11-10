using Game.Core.Match;
using NUnit.Framework;

// PressRules 검증(09-18 확정 스펙 §6). ①팀 압박 시작이 "안 감"이면 아무도 안 감 ②반경 = 개인 압박 거리 × 팀 배율(표준 1.0·적극 1.5)
// ③역압박 중 단계 배율 ×2.0 = 표준 2.0·적극 3.0(10-08) ④역압박 판정 = 뺏긴 순간 우리 뒤 수비 수 ≥ 문턱, 창 300틱
public class PressRulesTests
{
    [Test]
    public void 추격_예측은_공_속도_방향_앞을_노리고_서_있는_공은_그_자리다()
    {
        // 나 (0,0) 7m/s, 공 (10,0)이 +X로 5m/s → 거리 10 ÷ (7+5) = 0.83초 → 10 + 5×0.83 = 14.17
        (float x, float z) aim = PressRules.PursuitPoint(0f, 0f, 7f, 10f, 0f, 5f, 0f);
        Assert.AreEqual(14.1667f, aim.x, 1e-3f);
        Assert.AreEqual(0f, aim.z, 1e-4f);

        (float x, float z) still = PressRules.PursuitPoint(0f, 0f, 7f, 10f, 3f, 0f, 0f);
        Assert.AreEqual(10f, still.x, 1e-4f, "서 있는 공은 지금 위치");
        Assert.AreEqual(3f, still.z, 1e-4f);

        // 멀면 예측 시간 상한(1초): 공 (40,0) 5m/s → 40 ÷ 12 = 3.3초 → 1초로 잘려 45
        (float x, float z) far = PressRules.PursuitPoint(0f, 0f, 7f, 40f, 0f, 5f, 0f);
        Assert.AreEqual(45f, far.x, 1e-3f);
    }

    [Test]
    public void 압박_시작이_안_감이면_거리와_무관하게_안_간다()
    {
        Assert.IsFalse(PressRules.ShouldPress(distToBall: 1f, pressRange: 12f, pressStartLevel: 0, counterPressing: false));
    }

    [Test]
    public void 반경은_개인_압박_거리에_팀_배율을_곱한다()
    {
        // 표준(1) 배율 1.0: ST 12m는 10m 공에 감, CB 3m는 안 감
        Assert.IsTrue(PressRules.ShouldPress(10f, 12f, 1, false), "ST");
        Assert.IsFalse(PressRules.ShouldPress(10f, 3f, 1, false), "CB");
        // 적극(2) 배율 1.5: CB 3m → 4.5m. 4m 공에 감
        Assert.IsTrue(PressRules.ShouldPress(4f, 3f, 2, false));
        Assert.IsFalse(PressRules.ShouldPress(5f, 3f, 2, false));
        // 경계 포함
        Assert.IsTrue(PressRules.ShouldPress(12f, 12f, 1, false));
    }

    [Test]
    public void 압박_순위는_압박_거리_안_아군_중_공에_더_가까운_수이고_거리_밖이면_최대값이다()
    {
        // 공 (0,0). 후보: 1번 5m, 2번 10m, 3번 5m(1번과 동률 → id 작은 1번이 앞)
        var eligible = new[] { new TargetInfo(2, 10f, 0f), new TargetInfo(1, 5f, 0f), new TargetInfo(3, 0f, 5f) };
        Assert.AreEqual(0, PressRules.PressRank(1, eligible, 0f, 0f), "가장 가까움");
        Assert.AreEqual(1, PressRules.PressRank(3, eligible, 0f, 0f), "동률은 id 작은 쪽이 앞");
        Assert.AreEqual(2, PressRules.PressRank(2, eligible, 0f, 0f));
        Assert.AreEqual(int.MaxValue, PressRules.PressRank(9, eligible, 0f, 0f), "압박 거리 밖(목록에 없음)");
    }

    [Test]
    public void 지금_맡은_선수는_도전자가_여유_넘게_가까워야_순위를_넘긴다()
    {
        // 09-28 F1b 히스테리시스. 공 (0,0), 여유 2m. 맡은 선수 1번 3m
        var close = new[] { new TargetInfo(1, 3f, 0f), new TargetInfo(2, 1.5f, 0f) };   // 도전자 2번이 1.5m 더 가까움 → 유지
        Assert.AreEqual(0, PressRules.PressRank(1, close, 0f, 0f, incumbentId: 1, margin: 2f), "여유 안: 맡은 선수 유지");
        Assert.AreEqual(1, PressRules.PressRank(2, close, 0f, 0f, incumbentId: 1, margin: 2f));
        Assert.AreEqual(1, PressRules.FirstInRank(close, 0f, 0f, 1, 2f));

        var far = new[] { new TargetInfo(1, 3f, 0f), new TargetInfo(2, 0.5f, 0f) };     // 2.5m 더 가까움 → 교체
        Assert.AreEqual(2, PressRules.FirstInRank(far, 0f, 0f, 1, 2f), "여유 넘음: 도전자에게");

        Assert.AreEqual(2, PressRules.FirstInRank(close, 0f, 0f, -1, 2f), "맡은 선수가 없으면 최근접");
        Assert.AreEqual(2, PressRules.FirstInRank(close, 0f, 0f, 7, 2f), "맡은 선수가 후보에 없으면(압박 거리 밖·얼음) 최근접");
        Assert.AreEqual(-1, PressRules.FirstInRank(new TargetInfo[0], 0f, 0f, 1, 2f), "후보 없음");
    }

    [Test]
    public void 역압박_중이면_단계_배율에_2를_곱한다()
    {
        Assert.IsTrue(PressRules.ShouldPress(20f, 12f, 1, counterPressing: true), "표준: 12 × 1.0 × 2 = 24 ≥ 20");
        Assert.IsFalse(PressRules.ShouldPress(25f, 12f, 1, counterPressing: true));
        Assert.IsFalse(PressRules.ShouldPress(1f, 12f, 0, counterPressing: true), "안 감은 역압박도 안 감");
    }

    // 10-08 결정 ⑥: 역압박 창 안에서 배율을 2로 덮으면 표준과 적극이 같은 거리를 봐서 상대 진영 압박 2단이 같은 결과였다(09-30 스윕)
    [Test]
    public void 역압박_중에도_압박_시작_단계가_다른_거리를_본다()
    {
        Assert.IsFalse(PressRules.ShouldPress(30f, 12f, 1, counterPressing: true), "표준: 24 < 30");
        Assert.IsTrue(PressRules.ShouldPress(30f, 12f, 2, counterPressing: true), "적극: 12 × 1.5 × 2 = 36 ≥ 30");
        Assert.IsTrue(PressRules.ShouldPress(36f, 12f, 2, counterPressing: true), "적극 경계 포함");
        Assert.IsFalse(PressRules.ShouldPress(36.1f, 12f, 2, counterPressing: true));
    }

    [Test]
    public void 역압박_판정은_뒤_수비_수_문턱과_창으로_정한다()
    {
        // 성향 상황 봐서(1): 문턱 3. 뺏긴 직후(0틱)에 뒤에 4명이면 역압박
        Assert.IsTrue(PressRules.IsCounterPressing(defendersBehind: 4, counterPressLevel: 1, ticksSinceTurnover: 0));
        Assert.IsTrue(PressRules.IsCounterPressing(3, 1, 0), "문턱 포함");
        Assert.IsFalse(PressRules.IsCounterPressing(2, 1, 0), "뒤가 비었으면 재정비");
        Assert.IsFalse(PressRules.IsCounterPressing(4, 0, 0), "안 함");
        Assert.IsTrue(PressRules.IsCounterPressing(2, 2, 0), "적극(2): 문턱 2");
        Assert.IsTrue(PressRules.IsCounterPressing(4, 1, MatchTuning.CounterPressWindowTicks - 1), "창 안");
        Assert.IsFalse(PressRules.IsCounterPressing(4, 1, MatchTuning.CounterPressWindowTicks), "창 만료");
        Assert.AreEqual(300, MatchTuning.CounterPressWindowTicks, "6초 = 300틱(0.02s)");
    }
}

// 슛 조준(09-18 균등 반폭 → 11-09 B2 구석 목표 + 정규 각 오차). 표본 비율은 균등 격자 주사위(결정적)로 본다
public class ShotDirectionTests
{
    [Test]
    public void 조준_오차는_shot이_낮을수록_넓고_거리에_비례한다()
    {
        Assert.Less(MatchRules.AimSigmaDeg(90), MatchRules.AimSigmaDeg(30));
        Assert.AreEqual(MatchTuning.AimSigmaDegMin, MatchRules.AimSigmaDeg(100), 1e-5f);
        Assert.AreEqual(MatchTuning.AimSigmaDegMax, MatchRules.AimSigmaDeg(0), 1e-5f);
        float near = MatchRules.AimSigmaMeters(10f, 6f, pressed: false);
        float far = MatchRules.AimSigmaMeters(20f, 6f, pressed: false);
        Assert.AreEqual(2f, far / near, 1e-4f, "σ(m) = 거리 × tan(σθ)");
        Assert.AreEqual(10f * (float)System.Math.Tan(6.0 * System.Math.PI / 180.0), near, 1e-5f);
    }

    [Test]
    public void 압박_슛은_조준_오차가_1_25배다()
    {
        float calm = MatchRules.AimSigmaMeters(15f, 6f, pressed: false);
        Assert.AreEqual(calm * MatchTuning.PressedAimScale, MatchRules.AimSigmaMeters(15f, 6f, pressed: true), 1e-5f);
        Assert.AreEqual(1.25f, MatchTuning.PressedAimScale, "StatsBomb 압박 빗나감 43.4 vs 무압박 34.9 → 1.24");
    }

    [Test]
    public void 조준_Z는_구석_목표에_정규_오차를_더하고_roll1이_0이면_목표_그대로다()
    {
        float corner = Game.Core.Placement.FieldBounds.GoalHalfWidth - MatchTuning.AimInset;   // 3.16
        Assert.AreEqual(corner, MatchRules.ShotAimZ(+1f, 1.5f, 0f, 0.37f), 1e-5f, "roll1 0 → 표준정규 0");
        Assert.AreEqual(-corner, MatchRules.ShotAimZ(-1f, 1.5f, 0f, 0.37f), 1e-5f);
        // Box-Muller: roll (0.5, 0.5) → √(−2 ln 0.5) × cos π = −1.1774. roll (0.5, 0) → +1.1774
        Assert.AreEqual(-1.1774f, MatchRules.StandardNormal(0.5f, 0.5f), 1e-3f);
        Assert.AreEqual(1.1774f, MatchRules.StandardNormal(0.5f, 0f), 1e-3f);
        Assert.AreEqual(corner - 1.1774f * 1.5f, MatchRules.ShotAimZ(+1f, 1.5f, 0.5f, 0.5f), 1e-3f, "σ 1.5m × −1.18");
    }

    [Test]
    public void 조준_목표는_GK_반대쪽_구석이고_GK가_중앙이면_먼_포스트다()
    {
        Assert.AreEqual(-1f, MatchRules.AimSide(keeperZAtLine: 1f, centerZAtLine: 0f, shooterZ: 0f), "GK가 +쪽이면 −구석");
        Assert.AreEqual(1f, MatchRules.AimSide(-0.5f, 0f, 0f), "GK가 −쪽이면 +구석");
        Assert.AreEqual(-1f, MatchRules.AimSide(2.2f, 2f, 8f), "0.2m 차이는 경계 포함이라 벗어남 → GK 반대쪽(−)");
        Assert.AreEqual(-1f, MatchRules.AimSide(2.1f, 2f, 8f), "0.1m 차이는 중앙 → 슈터 z +8의 먼 포스트는 −");
        Assert.AreEqual(1f, MatchRules.AimSide(0f, 0f, -3f), "슈터 z −3의 먼 포스트는 +");
        Assert.AreEqual(1f, MatchRules.AimSide(0f, 0f, 0f), "정면 중앙은 +(결정적 기본값)");
    }

    [Test]
    public void 골문_안인지_판정한다()
    {
        float half = Game.Core.Placement.FieldBounds.GoalHalfWidth;
        Assert.IsTrue(MatchRules.IsOnTarget(0f));
        Assert.IsTrue(MatchRules.IsOnTarget(half), "포스트 안쪽 경계 포함");
        Assert.IsFalse(MatchRules.IsOnTarget(half + 0.01f));
        Assert.IsFalse(MatchRules.IsOnTarget(-half - 0.01f));
    }

    [Test]
    public void 정규_오차_표본의_골문_밖_비율은_멀수록_크고_shot이_낮을수록_크다()
    {
        // 균등 격자 주사위 32 × 32 = 1,024 표본. 8m·shot 90은 σ 0.76m라 구석 목표(3.16)에서 포스트(3.66)까지 0.5m = 0.66σ → 밖 약 25%(한쪽) + 반대쪽 거의 0
        float off8 = OffTargetShare(8f, 90);
        float off20 = OffTargetShare(20f, 90);
        float off20Low = OffTargetShare(20f, 30);
        Assert.Less(off8, off20, "멀수록 σ(m)가 커서 더 빗나간다");
        Assert.Less(off20, off20Low, "shot이 낮을수록 더 빗나간다");
        Assert.That(off20, Is.InRange(0.25f, 0.5f), "20m·shot 90: 설계-B 1-7 몬테카를로 30~40% 근처");
    }

    private static float OffTargetShare(float distance, int shot)
    {
        const int n = 32;
        int off = 0;
        float sigma = MatchRules.AimSigmaMeters(distance, MatchRules.AimSigmaDeg(shot), pressed: false);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                float z = MatchRules.ShotAimZ(+1f, sigma, (i + 0.5f) / n, (j + 0.5f) / n);
                if (!MatchRules.IsOnTarget(z)) { off++; }
            }
        }
        return off / (float)(n * n);
    }

    [Test]
    public void 뒷줄_전진_구역은_주어진_깊이_안이고_박스_반폭_안이며_경계는_포함이다()
    {
        // 09-29 수비 D1. 팀 0(+X 공격)의 골라인은 x = -52.5. 11-09 D2-b: 깊이는 시뮬이 라인 깊이 L + EngageAhead로 넘기고(여기선 21.5를 그대로 준다), 폭은 박스 반폭(±20.16)
        float depth = 21.5f;
        float half = MatchTuning.EngageHalfWidth;   // 20.16
        Assert.IsTrue(PressRules.IsInStepOutZone(-52.5f + 20f, 0f, +1, depth, half), "골라인에서 20m, 중앙");
        Assert.IsFalse(PressRules.IsInStepOutZone(-52.5f + 23f, 0f, +1, depth, half), "23m는 밖");
        Assert.IsFalse(PressRules.IsInStepOutZone(-52.5f + 10f, 25f, +1, depth, half), "박스 폭(±20.16) 밖 측면");
        Assert.IsTrue(PressRules.IsInStepOutZone(-52.5f + 10f, 15f, +1, depth, half), "박스 폭 안 측면(z 15)은 안(11-07 중앙 ±10.08을 11-09에 되돌림)");
        Assert.IsTrue(PressRules.IsInStepOutZone(52.5f - 10f, 0f, -1, depth, half), "팀 1은 +X 골라인");
        Assert.IsTrue(PressRules.IsInStepOutZone(-52.5f + 21.5f, 0f, +1, depth, half), "정확히 21.5m는 안(경계 포함, 09-30 리뷰)");
        Assert.IsFalse(PressRules.IsInStepOutZone(-52.5f + 21.6f, 0f, +1, depth, half), "21.6m는 밖");
        Assert.IsTrue(PressRules.IsInStepOutZone(-52.5f + 10f, half, +1, depth, half), "정확히 박스 반폭(20.16)은 안");
        Assert.IsFalse(PressRules.IsInStepOutZone(-52.5f + 10f, half + 0.01f, +1, depth, half), "20.17은 밖");
    }
}
