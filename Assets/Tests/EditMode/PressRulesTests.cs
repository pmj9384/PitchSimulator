using Game.Core.Match;
using NUnit.Framework;

// PressRules 검증(09-18 확정 스펙 §6). ①팀 압박 시작이 "안 감"이면 아무도 안 감 ②반경 = 개인 압박 거리 × 팀 배율(표준 1.0·적극 1.5)
// ③역압박 중 ×2.0 ④역압박 판정 = 뺏긴 순간 우리 뒤 수비 수 ≥ 문턱, 창 300틱
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
    public void 역압박_중이면_배율_2다()
    {
        Assert.IsTrue(PressRules.ShouldPress(20f, 12f, 1, counterPressing: true), "12 × 2.0 = 24 ≥ 20");
        Assert.IsFalse(PressRules.ShouldPress(25f, 12f, 1, counterPressing: true));
        Assert.IsFalse(PressRules.ShouldPress(1f, 12f, 0, counterPressing: true), "안 감은 역압박도 안 감");
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

// 슛 방향 오차(09-18 확정: 빗나감). shot 스탯이 낮을수록 조준이 퍼져 골문 밖까지 간다
public class ShotDirectionTests
{
    [Test]
    public void 오차_폭은_shot이_낮을수록_넓다()
    {
        Assert.Less(MatchRules.ShotSpread(90), MatchRules.ShotSpread(30));
        Assert.Greater(MatchRules.ShotSpread(30), Game.Core.Placement.FieldBounds.GoalHalfWidth, "shot 30은 골문 밖까지 퍼진다");
        Assert.LessOrEqual(MatchRules.ShotSpread(100), Game.Core.Placement.FieldBounds.GoalHalfWidth, "shot 100은 골문 안");
    }

    [Test]
    public void 조준_Z는_roll로_대칭_구간에서_정해지고_중앙은_roll_0_5다()
    {
        float spread = MatchRules.ShotSpread(60);
        Assert.AreEqual(0f, MatchRules.ShotAimZ(60, 0.5f), 1e-4f);
        Assert.AreEqual(-spread, MatchRules.ShotAimZ(60, 0f), 1e-4f);
        Assert.AreEqual(spread, MatchRules.ShotAimZ(60, 1f), 1e-4f);
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
    public void shot_90은_대부분_골문_안_shot_30은_상당수_밖이다()
    {
        int onHigh = 0, onLow = 0, n = 100;
        for (int i = 0; i < n; i++)
        {
            float roll = (i + 0.5f) / n;
            if (MatchRules.IsOnTarget(MatchRules.ShotAimZ(90, roll))) { onHigh++; }
            if (MatchRules.IsOnTarget(MatchRules.ShotAimZ(30, roll))) { onLow++; }
        }
        Assert.GreaterOrEqual(onHigh, 90, "shot 90: 90% 이상 골문 안");
        Assert.That(onLow, Is.InRange(40, 80), "shot 30: 절반 안팎만 골문 안");
    }
}
