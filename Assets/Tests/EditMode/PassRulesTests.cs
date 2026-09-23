using System.Collections.Generic;
using Game.Core.Match;
using NUnit.Framework;

// PassRules 검증(09-18 확정 스펙 §6). ①안전 판정 = Simple Soccer 로컬 좌표(상대가 공 뒤면 안전, 앞이면 도착 시간 t에
// 속도×t + 잡기 반경이 수직 거리를 넘으면 위험) ②리스크 허용(0.2/0.5/0.8)이 판정을 가름 ③리시버 점수 = 전진·선호 거리·폭
// ④역습 리시버 = 최전방 1명 ⑤GK 배급 짧게·섞어·길게
public class PassRulesTests
{
    private static readonly TargetInfo Passer = new TargetInfo(playerId: 0, x: 0f, z: 0f);

    [Test]
    public void 상대가_패스_방향_뒤에_있으면_안전하다()
    {
        var behind = new List<TargetInfo> { new TargetInfo(playerId: 11, x: -5f, z: 1f) };
        float risk = PassRules.InterceptRisk(0f, 0f, 20f, 0f, behind, opponentSpeed: 5f, ballSpeed: 15f);
        Assert.AreEqual(0f, risk, "뒤에 있는 상대는 위험도 0");
    }

    [Test]
    public void 경로_옆_1m_상대는_공_속도_15에_가로챈다()
    {
        // 패스 20m, 공 15m/s → 도착 1.33초. 상대는 x=10(공이 0.67초 뒤 지나감), z=1. 0.67초에 7m/s면 4.7m > 1m
        var near = new List<TargetInfo> { new TargetInfo(playerId: 11, x: 10f, z: 1f) };
        float risk = PassRules.InterceptRisk(0f, 0f, 20f, 0f, near, opponentSpeed: 5f, ballSpeed: 15f);
        Assert.AreEqual(1f, risk, "발 뻗는 범위 1.2 안이라 확실히 1");
        Assert.IsFalse(PassRules.IsPassSafe(risk, MatchTuning.PassRiskAllow[2]), "모험(0.8)이어도 안 함");
    }

    [Test]
    public void 경로에서_충분히_먼_상대는_못_닿는다()
    {
        // 상대 x=10, z=8. 공이 지나가는 0.67초에 5m/s면 3.3 + 1.2 = 4.5 < 8
        var far = new List<TargetInfo> { new TargetInfo(playerId: 11, x: 10f, z: 8f) };
        float risk = PassRules.InterceptRisk(0f, 0f, 20f, 0f, far, opponentSpeed: 5f, ballSpeed: 15f);
        Assert.AreEqual(0f, risk);
    }

    [Test]
    public void 위험도는_가까울수록_1에_가깝고_리스크_허용치가_판정을_가른다()
    {
        // 상대 z=3: 달려야 하는 거리 3-1.2=1.8, 0.67초에 5m/s면 3.3 → 위험도 1-1.8/3.3 ≈ 0.45. 여기선 단조성과 허용치만
        var mid = new List<TargetInfo> { new TargetInfo(playerId: 11, x: 10f, z: 3f) };
        float risk = PassRules.InterceptRisk(0f, 0f, 20f, 0f, mid, 5f, 15f);
        Assert.That(risk, Is.InRange(0.01f, 0.99f), "닿을락 말락은 중간값");

        Assert.IsFalse(PassRules.IsPassSafe(risk, riskAllow: MatchTuning.PassRiskAllow[0]), "안전(0.2)이면 막힘");
        Assert.IsTrue(PassRules.IsPassSafe(risk, riskAllow: MatchTuning.PassRiskAllow[2]), "모험(0.8)이면 통과");
        Assert.IsTrue(PassRules.IsPassSafe(0f, 0.2f));
        Assert.IsFalse(PassRules.IsPassSafe(1f, 0.8f), "확실히 닿으면 모험이어도 안 함");
    }

    [Test]
    public void 위험도는_여러_상대_중_최대값이다()
    {
        var two = new List<TargetInfo> { new TargetInfo(11, 10f, 8f), new TargetInfo(12, 10f, 0.5f) };
        Assert.AreEqual(1f, PassRules.InterceptRisk(0f, 0f, 20f, 0f, two, 5f, 15f), "8m 상대는 0, 축 위 상대는 1 → 최대 1");
    }

    [Test]
    public void 발치에_붙은_상대는_등_뒤로_차도_위험도_1이다()
    {
        // 패서 (0,0)이 +X로 찬다. 상대는 (-0.5, 0.3): 축 뒤(along < 0)지만 발 뻗는 범위 1.2 안
        var glued = new List<TargetInfo> { new TargetInfo(11, -0.5f, 0.3f) };
        Assert.AreEqual(1f, PassRules.InterceptRisk(0f, 0f, 20f, 0f, glued, 5f, 15f));
        var justOutside = new List<TargetInfo> { new TargetInfo(11, -1.5f, 0f) };
        Assert.AreEqual(0f, PassRules.InterceptRisk(0f, 0f, 20f, 0f, justOutside, 5f, 15f), "범위 밖 뒤쪽 상대는 예전처럼 무관");
    }

    [Test]
    public void 리시버_점수는_앞선_선수가_높고_선호_거리에_가까울수록_높다()
    {
        // 팀 0(+X). 패서 (0,0). 후보 A (15, 0) 전진 15, B (5, 0) 전진 5, C (-5, 0) 뒤
        float a = PassRules.ScoreReceiver(0f, 0f, 15f, 0f, +1, passStyle: 1, passLength: 15f, widthLevel: 1);
        float b = PassRules.ScoreReceiver(0f, 0f, 5f, 0f, +1, passStyle: 1, passLength: 15f, widthLevel: 1);
        float c = PassRules.ScoreReceiver(0f, 0f, -5f, 0f, +1, passStyle: 1, passLength: 15f, widthLevel: 1);

        Assert.Greater(a, b, "더 앞선 A");
        Assert.Greater(c, 0f, "5m 뒤 후보도 후보다(09-21 C)");
        Assert.Less(c, b, "뒤 후보는 어떤 앞 후보보다 낮다");
    }

    [Test]
    public void 옆_후보는_양수지만_앞_후보를_못_이기고_깊은_뒤_후보는_빠진다()
    {
        // 패서 (0,0). 옆 (0, 8) 전진 0, 앞 후보 (5, 0) 전진 5, 깊은 뒤 (-25, 0)
        // 경계: 옆·뒤 최대치는 0.3 × (근접 1 + 측면). 1~3m 앞처럼 전진이 거의 없는 후보는 근접 항이 낮아 좋은 옆 후보에게 질 수 있고
        // 그게 의도다(찔끔 패스 핑퐁 방지). 전진 5m 정도부터는 앞 후보가 확실히 이긴다
        float side = PassRules.ScoreReceiver(0f, 0f, 0f, 8f, +1, passStyle: 1, passLength: 15f, widthLevel: 1);
        float forward5 = PassRules.ScoreReceiver(0f, 0f, 5f, 0f, +1, passStyle: 1, passLength: 15f, widthLevel: 1);
        float deepBack = PassRules.ScoreReceiver(0f, 0f, -25f, 0f, +1, passStyle: 1, passLength: 15f, widthLevel: 1);

        Assert.Greater(side, 0f, "옆 아군은 후보");
        Assert.Less(side, forward5, "전진 5m 앞 후보에게 진다");
        Assert.LessOrEqual(deepBack, 0f, "25m 뒤는 감점으로 후보에서 빠진다");
    }

    [Test]
    public void 패스_방식이_롱볼이면_먼_후보를_더_선호한다()
    {
        float shortNear = PassRules.ScoreReceiver(0f, 0f, 8f, 0f, +1, passStyle: 0, passLength: 15f, widthLevel: 1);
        float shortFar = PassRules.ScoreReceiver(0f, 0f, 30f, 0f, +1, passStyle: 0, passLength: 15f, widthLevel: 1);
        float longNear = PassRules.ScoreReceiver(0f, 0f, 8f, 0f, +1, passStyle: 2, passLength: 15f, widthLevel: 1);
        float longFar = PassRules.ScoreReceiver(0f, 0f, 30f, 0f, +1, passStyle: 2, passLength: 15f, widthLevel: 1);

        Assert.Greater(shortNear - shortFar, longNear - longFar, "짧게는 가까운 쪽으로, 롱볼은 먼 쪽으로 기운다");
    }

    [Test]
    public void 폭이_넓으면_측면_후보_점수가_오른다()
    {
        float narrow = PassRules.ScoreReceiver(0f, 0f, 10f, 20f, +1, passStyle: 1, passLength: 15f, widthLevel: 0);
        float wide = PassRules.ScoreReceiver(0f, 0f, 10f, 20f, +1, passStyle: 1, passLength: 15f, widthLevel: 2);
        Assert.Greater(wide, narrow);
    }

    [Test]
    public void 옆_뒤_리시버에겐_리드를_주지_않는다()
    {
        // 09-21: 뒤 5m 아군에게 앞으로 8m 리드하면 착지점이 패서보다 앞이 된다. 옆·뒤는 지금 위치가 목표
        (float x, float z) back = PassRules.LeadTarget(passerX: 0f, receiverX: -5f, receiverZ: 8f, attackSign: +1, passDistance: 9.4f, ballSpeed: 15f, receiverSpeed: 7f);
        Assert.AreEqual(-5f, back.x, 1e-4f);
        Assert.AreEqual(8f, back.z, 1e-4f);
        (float x, float z) side = PassRules.LeadTarget(0f, 0f, 10f, +1, 10f, 15f, 7f);
        Assert.AreEqual(0f, side.x, 1e-4f, "전진 0도 리드 없음");
    }

    [Test]
    public void 리드_패스_목표는_리시버_앞쪽이고_상한과_필드_안으로_잘린다()
    {
        // 20m 패스를 15m/s로 → 1.33초. 리시버 7m/s면 9.3m 앞이지만 상한 8m
        (float x, float z) lead = PassRules.LeadTarget(passerX: 0f, 10f, 5f, +1, passDistance: 20f, ballSpeed: 15f, receiverSpeed: 7f);
        Assert.AreEqual(18f, lead.x, 1e-4f);
        Assert.AreEqual(5f, lead.z, 1e-4f, "Z는 그대로");

        lead = PassRules.LeadTarget(0f, 10f, 0f, +1, 20f, 15f, receiverSpeed: 3f);
        Assert.AreEqual(14f, lead.x, 1e-4f, "느린 리시버는 4m 앞");

        lead = PassRules.LeadTarget(20f, 10f, 0f, -1, 20f, 15f, 3f);
        Assert.AreEqual(6f, lead.x, 1e-4f, "팀 1은 -X 앞");

        lead = PassRules.LeadTarget(30f, 50f, 0f, +1, 20f, 15f, 7f);
        Assert.AreEqual(52f, lead.x, 1e-4f, "골라인 안(52.5 - 0.5)으로 클램프");

        lead = PassRules.LeadTarget(0f, 10f, 0f, +1, 20f, 15f, receiverSpeed: 0f);
        Assert.AreEqual(10f, lead.x, 1e-4f, "정지 리시버는 제자리");
    }

    [Test]
    public void 킥_초속은_도착_속도와_거리로_역산하고_상한을_넘지_않는다()
    {
        // 감속 4, 도착 5: 5m → √(25 + 40) = 8.06, 30m → √(25 + 240) = 16.28, 100m → √(825) = 28.7은 상한 22로
        Assert.AreEqual(8.062f, PassRules.KickSpeed(5f, 5f, 4f, 22f), 1e-3f, "짧은 패스는 살살");
        Assert.AreEqual(16.279f, PassRules.KickSpeed(30f, 5f, 4f, 22f), 1e-3f, "긴 패스는 세게");
        Assert.AreEqual(22f, PassRules.KickSpeed(100f, 5f, 4f, 22f), "상한");
        // 평균 속도 = (초속 + 도착) / 2. 상한에 걸려 못 미치면 도착 0
        Assert.AreEqual(6.531f, PassRules.AverageSpeed(8.062f, 5f, 4f), 1e-3f);
        Assert.AreEqual(11f, PassRules.AverageSpeed(22f, 100f, 4f), 1e-3f, "484 − 800 < 0 → 도착 0 → 평균 11");
    }

    [Test]
    public void 역습_리시버는_가장_앞선_아군_1명이다()
    {
        var mates = new List<TargetInfo> { new TargetInfo(1, 5f, 0f), new TargetInfo(2, 25f, -10f), new TargetInfo(3, 18f, 4f) };
        Assert.AreEqual(2, PassRules.CounterReceiver(0f, mates, +1, passerId: 0, OffsideRules.NoLine));
        Assert.AreEqual(-1, PassRules.CounterReceiver(0f, mates, -1, passerId: 0, OffsideRules.NoLine), "팀 1은 -X가 앞이라 셋 다 뒤 → 없음");
        Assert.AreEqual(-1, PassRules.CounterReceiver(0f, new List<TargetInfo>(), +1, 0, OffsideRules.NoLine));
        Assert.AreEqual(-1, PassRules.CounterReceiver(25f, mates, +1, passerId: 2, OffsideRules.NoLine), "최전방(25)이 가지면 뒤로 안 돌린다(09-21 핑퐁 수정)");
        Assert.AreEqual(2, PassRules.CounterReceiver(5f, mates, +1, passerId: 1, OffsideRules.NoLine), "패서(5)보다 3m 이상 앞선 후보(18·25) 중 가장 앞");
        var tooClose = new List<TargetInfo> { new TargetInfo(1, 2f, 0f) };
        Assert.AreEqual(-1, PassRules.CounterReceiver(0f, tooClose, +1, passerId: 0, OffsideRules.NoLine), "2m 앞은 마진(3m) 미만");
        Assert.AreEqual(3, PassRules.CounterReceiver(0f, mates, +1, passerId: 0, onsideLine: 20f), "온사이드 선 20: 25는 오프사이드 위치라 제외, 18이 최전방");
    }

    [Test]
    public void GK_배급은_짧게면_가장_가까운_아군_길게면_가장_먼_앞선_아군이다()
    {
        // GK (-48, 0). 아군 CB(-36, 7) 거리 13.9, DM(-20, 0) 거리 28, ST(-8, 0) 거리 40
        var mates = new List<TargetInfo> { new TargetInfo(1, -36f, 7f), new TargetInfo(2, -20f, 0f), new TargetInfo(3, -8f, 0f) };
        Assert.AreEqual(1, PassRules.KeeperDistributionTarget(-48f, 0f, mates, +1, level: 0, keeperId: 9, alternate: false), "짧게");
        Assert.AreEqual(3, PassRules.KeeperDistributionTarget(-48f, 0f, mates, +1, level: 2, keeperId: 9, alternate: false), "길게");
        Assert.AreEqual(1, PassRules.KeeperDistributionTarget(-48f, 0f, mates, +1, level: 1, keeperId: 9, alternate: false), "섞어: 이번엔 짧게");
        Assert.AreEqual(3, PassRules.KeeperDistributionTarget(-48f, 0f, mates, +1, level: 1, keeperId: 9, alternate: true), "섞어: 다음엔 길게");
        Assert.AreEqual(-1, PassRules.KeeperDistributionTarget(-48f, 0f, new List<TargetInfo>(), +1, 0, 9, false));
    }
}
