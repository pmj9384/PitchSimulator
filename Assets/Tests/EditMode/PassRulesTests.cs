using System.Collections.Generic;
using Game.Core.Match;
using NUnit.Framework;

// PassRules 검증(09-18 확정 스펙 §6). ①안전 판정 = Simple Soccer 로컬 좌표(상대가 공 뒤면 안전, 앞이면 도착 시간 t에
// 속도×t + 잡기 반경이 수직 거리를 넘으면 위험) ②리스크 허용(0.2/0.5/0.8)이 판정을 가름 ③리시버 적합도(ReceiverFit) = 선호 거리·폭(전진 가중은 11-03 P1에서 지우고 xT가 센다)
// ④역습 리시버 = 최전방 1명 ⑤GK 배급 짧게·섞어·길게
public class PassRulesTests
{
    private static readonly TargetInfo Passer = new TargetInfo(playerId: 0, x: 0f, z: 0f);

    [Test]
    public void 상대가_패스_방향_뒤에_있으면_안전하다()
    {
        var behind = new List<TargetInfo> { new TargetInfo(playerId: 11, x: -5f, z: 1f) };
        float risk = PassRules.InterceptRisk(0f, 0f, 20f, 0f, behind, opponentSpeed: 5f, ballSpeed: 15f, opponentReaction: 0f);
        Assert.AreEqual(0f, risk, "뒤에 있는 상대는 위험도 0");
    }

    [Test]
    public void 경로_옆_1m_상대는_공_속도_15에_가로챈다()
    {
        // 패스 20m, 공 15m/s → 도착 1.33초. 상대는 x=10(공이 0.67초 뒤 지나감), z=1. 0.67초에 7m/s면 4.7m > 1m
        var near = new List<TargetInfo> { new TargetInfo(playerId: 11, x: 10f, z: 1f) };
        float risk = PassRules.InterceptRisk(0f, 0f, 20f, 0f, near, opponentSpeed: 5f, ballSpeed: 15f, opponentReaction: 0f);
        Assert.AreEqual(1f, risk, "발 뻗는 범위 1.2 안이라 확실히 1");
        Assert.IsFalse(PassRules.IsPassSafe(risk, MatchTuning.PassRiskAllow[2]), "모험(0.8)이어도 안 함");
    }

    [Test]
    public void 경로에서_충분히_먼_상대는_못_닿는다()
    {
        // 상대 x=10, z=8. 공이 지나가는 0.67초에 5m/s면 3.3 + 1.2 = 4.5 < 8
        var far = new List<TargetInfo> { new TargetInfo(playerId: 11, x: 10f, z: 8f) };
        float risk = PassRules.InterceptRisk(0f, 0f, 20f, 0f, far, opponentSpeed: 5f, ballSpeed: 15f, opponentReaction: 0f);
        Assert.AreEqual(0f, risk);
    }

    [Test]
    public void 위험도는_가까울수록_1에_가깝고_리스크_허용치가_판정을_가른다()
    {
        // 상대 z=3: 달려야 하는 거리 3-1.2=1.8, 0.67초에 5m/s면 3.3 → 위험도 1-1.8/3.3 ≈ 0.45. 여기선 단조성과 허용치만
        var mid = new List<TargetInfo> { new TargetInfo(playerId: 11, x: 10f, z: 3f) };
        float risk = PassRules.InterceptRisk(0f, 0f, 20f, 0f, mid, 5f, 15f, 0f);
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
        Assert.AreEqual(1f, PassRules.InterceptRisk(0f, 0f, 20f, 0f, two, 5f, 15f, 0f), "8m 상대는 0, 축 위 상대는 1 → 최대 1");
    }

    [Test]
    public void 발치에_붙은_상대는_등_뒤로_차도_위험도_1이다()
    {
        // 패서 (0,0)이 +X로 찬다. 상대는 (-0.5, 0.3): 축 뒤(along < 0)지만 발 뻗는 범위 1.2 안
        var glued = new List<TargetInfo> { new TargetInfo(11, -0.5f, 0.3f) };
        Assert.AreEqual(1f, PassRules.InterceptRisk(0f, 0f, 20f, 0f, glued, 5f, 15f, 0f));
        var justOutside = new List<TargetInfo> { new TargetInfo(11, -1.5f, 0f) };
        Assert.AreEqual(0f, PassRules.InterceptRisk(0f, 0f, 20f, 0f, justOutside, 5f, 15f, 0f), "범위 밖 뒤쪽 상대는 예전처럼 무관");
    }

    [Test]
    public void 리시버_적합도는_선호_거리에_가까울수록_높고_전진_여부는_안_본다()
    {
        // 패서 (0,0), 선호 거리 15m. 15m 앞·15m 뒤는 거리가 같아 적합도가 같다(앞뒤 구분은 착지점 xT 몫)
        float ahead = PassRules.ReceiverFit(0f, 0f, 15f, 0f, passStyle: 1, passLength: 15f, widthLevel: 1);
        float behind = PassRules.ReceiverFit(0f, 0f, -15f, 0f, passStyle: 1, passLength: 15f, widthLevel: 1);
        float near = PassRules.ReceiverFit(0f, 0f, 5f, 0f, passStyle: 1, passLength: 15f, widthLevel: 1);

        Assert.AreEqual(ahead, behind, 1e-6f);
        Assert.Greater(ahead, near, "선호 거리에 더 가까운 쪽");
    }

    [Test]
    public void 리시버_적합도는_상한_함수를_넘지_않는다_무작위_100개()
    {
        var random = new System.Random(1105);
        for (int i = 0; i < 100; i++)
        {
            float passerX = (float)random.NextDouble() * 60f - 30f;
            float passerZ = (float)random.NextDouble() * 50f - 25f;
            float candX = (float)random.NextDouble() * 100f - 50f;
            float candZ = (float)random.NextDouble() * 60f - 30f;
            int style = random.Next(0, 3);
            int width = random.Next(0, 3);
            float fit = PassRules.ReceiverFit(passerX, passerZ, candX, candZ, style, 15f, width);
            Assert.LessOrEqual(fit, PassRules.ReceiverFitCeiling(candZ, width) + 1e-5f, $"{i}번째");
        }
    }

    [Test]
    public void 패스_방식이_롱볼이면_먼_후보를_더_선호한다()
    {
        float shortNear = PassRules.ReceiverFit(0f, 0f, 8f, 0f, passStyle: 0, passLength: 15f, widthLevel: 1);
        float shortFar = PassRules.ReceiverFit(0f, 0f, 30f, 0f, passStyle: 0, passLength: 15f, widthLevel: 1);
        float longNear = PassRules.ReceiverFit(0f, 0f, 8f, 0f, passStyle: 2, passLength: 15f, widthLevel: 1);
        float longFar = PassRules.ReceiverFit(0f, 0f, 30f, 0f, passStyle: 2, passLength: 15f, widthLevel: 1);

        Assert.Greater(shortNear - shortFar, longNear - longFar, "짧게는 가까운 쪽으로, 롱볼은 먼 쪽으로 기운다");
    }

    [Test]
    public void 폭이_넓으면_측면_후보_점수가_오른다()
    {
        float narrow = PassRules.ReceiverFit(0f, 0f, 10f, 20f, passStyle: 1, passLength: 15f, widthLevel: 0);
        float wide = PassRules.ReceiverFit(0f, 0f, 10f, 20f, passStyle: 1, passLength: 15f, widthLevel: 2);
        Assert.Greater(wide, narrow);
    }

    [Test]
    public void 옆_뒤_리시버에겐_리드를_주지_않는다()
    {
        // 09-21: 뒤 5m 아군에게 앞으로 8m 리드하면 착지점이 패서보다 앞이 된다. 옆·뒤는 지금 위치가 목표
        (float x, float z) back = PassRules.LeadTarget(passerX: 0f, receiverX: -5f, receiverZ: 8f, attackSign: +1, passDistance: 9.4f, ballSpeed: 15f, receiverSpeed: 7f, leadMax: MatchTuning.PassLeadMax);
        Assert.AreEqual(-5f, back.x, 1e-4f);
        Assert.AreEqual(8f, back.z, 1e-4f);
        (float x, float z) side = PassRules.LeadTarget(0f, 0f, 10f, +1, 10f, 15f, 7f, MatchTuning.PassLeadMax);
        Assert.AreEqual(0f, side.x, 1e-4f, "전진 0도 리드 없음");
    }

    [Test]
    public void 리드_패스_목표는_리시버_앞쪽이고_상한과_필드_안으로_잘린다()
    {
        // 20m 패스를 15m/s로 → 1.33초. 리시버 7m/s면 9.3m 앞이지만 상한 8m
        (float x, float z) lead = PassRules.LeadTarget(passerX: 0f, 10f, 5f, +1, passDistance: 20f, ballSpeed: 15f, receiverSpeed: 7f, leadMax: MatchTuning.PassLeadMax);
        Assert.AreEqual(18f, lead.x, 1e-4f);
        Assert.AreEqual(5f, lead.z, 1e-4f, "Z는 그대로");

        lead = PassRules.LeadTarget(0f, 10f, 0f, +1, 20f, 15f, receiverSpeed: 3f, leadMax: MatchTuning.PassLeadMax);
        Assert.AreEqual(14f, lead.x, 1e-4f, "느린 리시버는 4m 앞");

        lead = PassRules.LeadTarget(20f, 10f, 0f, -1, 20f, 15f, 3f, MatchTuning.PassLeadMax);
        Assert.AreEqual(6f, lead.x, 1e-4f, "팀 1은 -X 앞");

        lead = PassRules.LeadTarget(30f, 50f, 0f, +1, 20f, 15f, 7f, MatchTuning.PassLeadMax);
        Assert.AreEqual(52f, lead.x, 1e-4f, "골라인 안(52.5 - 0.5)으로 클램프");

        lead = PassRules.LeadTarget(0f, 10f, 0f, +1, 20f, 15f, receiverSpeed: 0f, leadMax: MatchTuning.PassLeadMax);
        Assert.AreEqual(10f, lead.x, 1e-4f, "정지 리시버는 제자리");

        lead = PassRules.LeadTarget(0f, 10f, 0f, +1, 20f, 15f, receiverSpeed: 7f, leadMax: 3.5f);
        Assert.AreEqual(13.5f, lead.x, 1e-4f, "리드 상한을 낮추면 그만큼만 앞");
        lead = PassRules.LeadTarget(0f, 10f, 0f, +1, 20f, 15f, receiverSpeed: 7f, leadMax: 0f);
        Assert.AreEqual(10f, lead.x, 1e-4f, "상한 0은 발밑");
    }

    [Test]
    public void 킥_초속은_평균_속도와_거리로_역산하고_상한을_넘지_않는다()
    {
        // 10-25: 입력이 평균 속도(실측 Metrica). v0 = v̄ + a·d/(2v̄). 감속 1.2·평균 12.2: 15m → 12.2 + 18/24.4 = 12.938, 도착 = 2v̄ − v0 = 11.462
        Assert.AreEqual(12.938f, PassRules.KickSpeed(15f, 12.2f, 1.2f, 22f), 1e-3f, "표준 15m");
        Assert.AreEqual(8.8f + 1.2f * 5f / 17.6f, PassRules.KickSpeed(5f, 8.8f, 1.2f, 22f), 1e-3f, "짧은 패스는 평균 근처");
        Assert.AreEqual(22f, PassRules.KickSpeed(400f, 12.2f, 1.2f, 22f), "상한(12.2 + 480/24.4 = 31.9 → 22)");
        // 실제 평균 = (초속 + 도착) / 2. 입력 평균이 그대로 나와야 한다. 상한에 걸려 못 미치면 도착 0
        Assert.AreEqual(12.2f, PassRules.AverageSpeed(12.938f, 15f, 1.2f), 1e-3f, "역산이 맞으면 평균이 입력과 같다");
        Assert.AreEqual(11f, PassRules.AverageSpeed(22f, 400f, 1.2f), 1e-3f, "484 − 960 < 0 → 도착 0 → 평균 11");
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

    // ── 착지점 후보(09-30 공격 칼날 A). 받는 선수 앞 8m 한 곳만 보던 것을 8·3.5·0m 중 안전한 가장 긴 것으로
    private const float Arrival = 5f;   // 속도 "표준"의 도착 속도
    private const float Allow = 0.5f;   // 패스 리스크 "균형"

    [Test]
    public void 착지점은_앞이_비면_가장_긴_리드다()
    {
        PassLanding landing = PassRules.PickLanding(0f, 0f, 15f, 0f, +1, Arrival, new List<TargetInfo>(), Allow);
        Assert.AreEqual(15f + MatchTuning.PassLeadOptions[0], landing.X, 1e-4f, "받는 선수 8m 앞");
        Assert.AreEqual(0f, landing.Z, 1e-4f);
        Assert.AreEqual(0f, landing.Risk, 1e-4f);
    }

    [Test]
    public void 긴_리드_착지점이_막히면_짧은_리드로_내린다()
    {
        // 상대가 8m 앞 착지점(23, 0) 바로 옆에 서 있다. 3.5m 앞(18.5)은 그 상대보다 4.5m 앞이라 안전
        var opp = new List<TargetInfo> { new TargetInfo(11, 23f, 0.5f) };
        PassLanding landing = PassRules.PickLanding(0f, 0f, 15f, 0f, +1, Arrival, opp, Allow);
        Assert.AreEqual(15f + MatchTuning.PassLeadOptions[1], landing.X, 1e-4f, "3.5m 앞");
        Assert.IsTrue(PassRules.IsPassSafe(landing.Risk, Allow));
    }

    [Test]
    public void 짧은_리드도_막히면_발밑으로_준다()
    {
        // (19, 0.5)의 상대는 3.5m 앞 착지점(18.5)에서 0.7m: 너머에 서 있어도 발이 닿는다. 받는 선수 발밑(15)에선 4m 떨어져 안전
        var opp = new List<TargetInfo> { new TargetInfo(11, 23f, 0.5f), new TargetInfo(12, 19f, 0.5f) };
        PassLanding landing = PassRules.PickLanding(0f, 0f, 15f, 0f, +1, Arrival, opp, Allow);
        Assert.AreEqual(15f, landing.X, 1e-4f, "발밑");
        Assert.IsTrue(PassRules.IsPassSafe(landing.Risk, Allow));
    }

    [Test]
    public void 후보가_전부_막히면_허용치를_넘는_위험이_돌아온다()
    {
        // 패스 길 위(7, 0.2)에 상대: 어느 착지점으로 차도 끊긴다. 호출자는 Risk로 안전 여부를 본다
        var opp = new List<TargetInfo> { new TargetInfo(11, 7f, 0.2f) };
        PassLanding landing = PassRules.PickLanding(0f, 0f, 15f, 0f, +1, Arrival, opp, Allow);
        Assert.IsFalse(PassRules.IsPassSafe(landing.Risk, Allow));
    }

    [Test]
    public void 옆_뒤_받는_선수의_착지점은_후보와_무관하게_지금_위치다()
    {
        PassLanding landing = PassRules.PickLanding(0f, 0f, -5f, 8f, +1, Arrival, new List<TargetInfo>(), Allow);
        Assert.AreEqual(-5f, landing.X, 1e-4f);
        Assert.AreEqual(8f, landing.Z, 1e-4f);
    }

    [Test]
    public void 착지점_너머에_선_상대는_발이_닿는_거리면_위험하고_멀면_무관하다()
    {
        // 09-30 전엔 착지점보다 먼 상대는 전부 무관이었다. 착지점이 받는 선수 발밑까지 내려오면 0.7m 뒤에 붙은 수비수도 "무관"이 된다
        var touching = new List<TargetInfo> { new TargetInfo(11, 19f, 0.5f) };
        Assert.AreEqual(1f, PassRules.InterceptRisk(0f, 0f, 18.5f, 0f, touching, 7f, 10f, 0f), "착지점에서 0.7m = 발 뻗는 범위(1.2m) 안");
        var beyond = new List<TargetInfo> { new TargetInfo(11, 23f, 0.5f) };
        Assert.AreEqual(0f, PassRules.InterceptRisk(0f, 0f, 18.5f, 0f, beyond, 7f, 10f, 0f), "4.5m 너머는 예전처럼 무관");
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

    // 안전 가중(10-29 가속 E): (1 − 위험도) × 경계 띠. 띠는 허용치 ± band에서 1 → 0
    [Test]
    public void 안전_가중은_허용치_아래_띠_밖이면_1_빼기_위험도다()
    {
        Assert.AreEqual(0.8f, PassRules.SafetyWeight(0.2f, 0.5f, 0.1f), 1e-5f);
        Assert.AreEqual(1f, PassRules.SafetyWeight(0f, 0.5f, 0.1f), 1e-5f);
    }

    [Test]
    public void 안전_가중은_허용치에서_절반이고_띠_위끝에서_0이다()
    {
        Assert.AreEqual(0.5f * 0.5f, PassRules.SafetyWeight(0.5f, 0.5f, 0.1f), 1e-5f, "허용치 = 띠 가운데: (1 − 0.5) × 0.5");
        Assert.AreEqual(0f, PassRules.SafetyWeight(0.6f, 0.5f, 0.1f), 1e-5f);
        Assert.AreEqual(0f, PassRules.SafetyWeight(0.9f, 0.5f, 0.1f), 1e-5f);
    }

    [Test]
    public void 안전_가중은_위험도가_오르면_줄기만_한다()
    {
        float previous = float.MaxValue;
        for (float risk = 0f; risk <= 1f; risk += 0.05f)
        {
            float w = PassRules.SafetyWeight(risk, 0.5f, 0.1f);
            Assert.LessOrEqual(w, previous + 1e-6f, $"위험도 {risk}");
            previous = w;
        }
    }

    [Test]
    public void 안전_가중의_띠가_0이면_옛_이진_판정이다()
    {
        Assert.AreEqual(0.6f, PassRules.SafetyWeight(0.4f, 0.5f, 0f), 1e-5f);
        Assert.AreEqual(0f, PassRules.SafetyWeight(0.51f, 0.5f, 0f), 1e-5f, "나누기 0 없이 탈락");
    }

    [Test]
    public void 상대_반응_지연이_있으면_가로채기_위험이_낮다()
    {
        var mid = new List<TargetInfo> { new TargetInfo(1, 10f, 4f) };
        float instant = PassRules.InterceptRisk(0f, 0f, 20f, 0f, mid, 7f, 15f, 0f);
        float delayed = PassRules.InterceptRisk(0f, 0f, 20f, 0f, mid, 7f, 15f, MatchTuning.ReactionTime);
        Assert.Less(delayed, instant);
    }

    // ── 로빙(10-29 ④-1 3단계)
    [Test]
    public void 로빙_초속은_그_거리에_떨어지게_역산된다()
    {
        (float h, float v, float t) lob = BallRules.LobVelocity(25f, 40f, 9.81f);
        Assert.AreEqual(25f, lob.h * lob.t, 1e-3f, "수평 속도 × 체공 = 거리");
        Assert.AreEqual(2f * lob.v / 9.81f, lob.t, 1e-5f);
    }

    [Test]
    public void 로빙_위험은_경로가_아니라_도달_경쟁이다()
    {
        // 경로 한가운데(10,0)의 수비는 땅볼이면 확실히 끊지만 로빙엔 무관하다. 그 점에선 공이 이미 머리 위(5m)다
        var onPath = new List<TargetInfo> { new TargetInfo(11, 10f, 0f) };
        PassLanding lob = PassRules.LobLanding(0f, 0f, 23f, 0f, 1, onPath, -1);
        float ground = PassRules.InterceptRisk(0f, 0f, 25f, 0f, onPath, 7f, 15f, 0.5f);
        Assert.AreEqual(1f, ground, 1e-5f);
        Assert.IsTrue(lob.Found);
        Assert.Less(lob.Risk, 0.1f);
    }

    [Test]
    public void 킥_지점에_붙은_상대는_로빙도_뜨기_전에_막는다()
    {
        var glued = new List<TargetInfo> { new TargetInfo(11, 0.5f, 0f) };
        PassLanding lob = PassRules.LobLanding(0f, 0f, 23f, 0f, 1, glued, -1);
        Assert.Greater(lob.Risk, 0.99f);
    }

    [Test]
    public void 상대_GK는_자기_박스_안에서_손_높이로_재서_같은_자리_필드_선수보다_위험하다()
    {
        // 팀 0(공격 +X)이 (20,0)에서 상대 박스 쪽 리시버에게 로빙. 상대 GK의 공격 방향은 −X라 자기 골은 +X 끝이다.
        // 상대는 공이 2.4m(필드 선수는 못 닿고 GK만 닿는 높이)로 내려오는 하강 지점에서 옆으로 6m 비껴 선다.
        // 붙어 서면 필드 선수도 같은 틱에 잡아 도착 시각 가르기로 위험이 이미 포화한다(11-02 동률 처리). 6m 비키면 필드 선수는 리시버보다 늦게 도착하고, GK만 2.6m에서 더 일찍 잡는다
        PassLanding open = PassRules.LobLanding(20f, 0f, 46f, 0f, 1, new List<TargetInfo>(), -1);
        float distance = open.X - 20f;
        (float h, float v, float t) lob = BallRules.LobVelocity(distance, MatchTuning.LobLaunchAngleDeg, MatchTuning.Gravity);
        float fall = (lob.v + (float)System.Math.Sqrt(lob.v * lob.v - 2f * 9.81f * 2.4f)) / 9.81f;
        var opponents = new List<TargetInfo> { new TargetInfo(11, 20f + lob.h * fall, 6f) };
        PassLanding asField = PassRules.LobLanding(20f, 0f, 46f, 0f, 1, opponents, -1);
        PassLanding asKeeper = PassRules.LobLanding(20f, 0f, 46f, 0f, 1, opponents, 11);
        Assert.Greater(asKeeper.Risk, asField.Risk + 0.05f, "GK는 2.6m에서 일찍 잡아 위험이 눈에 띄게 크다");
    }

    [Test]
    public void 상대가_둘이면_순서와_상관없이_가장_위험한_상대로_정해진다()
    {
        // 하나는 하강 입구에 붙어 같은 틱에 먼저 와 있고, 하나는 킥 지점에서 멀어 늦다. 목록 순서를 바꿔도 같은 위험이어야 하고 가까운 쪽 하나만 있을 때와 같다
        PassLanding open = PassRules.LobLanding(0f, 0f, 23f, 0f, 1, new List<TargetInfo>(), -1);
        var entrance = new TargetInfo(11, open.X - 3f, 0f);
        var far = new TargetInfo(12, open.X - 12f, 9f);
        float both = PassRules.LobLanding(0f, 0f, 23f, 0f, 1, new List<TargetInfo> { far, entrance }, -1).Risk;
        float reversed = PassRules.LobLanding(0f, 0f, 23f, 0f, 1, new List<TargetInfo> { entrance, far }, -1).Risk;
        float onlyEntrance = PassRules.LobLanding(0f, 0f, 23f, 0f, 1, new List<TargetInfo> { entrance }, -1).Risk;
        Assert.AreEqual(both, reversed, 0f);
        Assert.GreaterOrEqual(both, onlyEntrance);
    }

    [Test]
    public void 로빙_범위_밖이면_후보가_없다()
    {
        var none = new List<TargetInfo>();
        Assert.IsFalse(PassRules.LobLanding(0f, 0f, 5f, 0f, 1, none, -1).Found, "15m보다 짧다");
        Assert.IsFalse(PassRules.LobLanding(0f, 0f, 0f, 50f, 1, none, -1).Found, "45m보다 길다(옆 리시버라 리드 없음)");
        PassLanding inRange = PassRules.LobLanding(0f, 0f, 20f, 0f, 1, none, -1);
        Assert.IsTrue(inRange.Found);
        Assert.AreEqual(0f, inRange.Risk, 1e-6f);
    }


    [Test]
    public void 로빙_범위_경계는_15m와_45m를_포함한다()
    {
        // 옆에 선 리시버는 공격 방향 성분이 0이라 리드 없이 리시버 위치가 착지점이 된다. 거리를 정확히 줄 수 있다
        var none = new List<TargetInfo>();
        Assert.IsTrue(PassRules.LobLanding(0f, 0f, 0f, 15f, 1, none, -1).Found, "15m는 포함");
        Assert.IsTrue(PassRules.LobLanding(0f, 0f, 0f, 45f, 1, none, -1).Found, "45m는 포함");
        Assert.IsFalse(PassRules.LobLanding(0f, 0f, 0f, 14.9f, 1, none, -1).Found, "15m 미만");
        Assert.IsFalse(PassRules.LobLanding(0f, 0f, 0f, 45.1f, 1, none, -1).Found, "45m 초과");
    }

    [Test]
    public void 스루_허용치는_실측_맞춘_기준값에_팀_개인_허용치의_균형_대비_편차를_얹는다()
    {
        Assert.AreEqual(MatchTuning.ThroughRiskAllow, PassRules.ThroughAllow(1, 0f), 1e-5f, "균형 = 기준값");
        Assert.AreEqual(MatchTuning.ThroughRiskAllow - 0.3f, PassRules.ThroughAllow(0, 0f), 1e-5f, "안전 카드(0.2)는 0.3 낮다");
        Assert.AreEqual(1f, PassRules.ThroughAllow(2, 0.95f), 1e-5f, "1로 자른다");
        Assert.AreEqual(MatchTuning.ThroughRiskAllow + 0.2f, PassRules.ThroughAllow(1, 0.7f), 1e-5f, "개인 허용치가 더 크면 그 편차");
    }
}
