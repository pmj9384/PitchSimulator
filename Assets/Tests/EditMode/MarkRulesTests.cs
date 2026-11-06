using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Placement;
using NUnit.Framework;

// MarkRules 검증(11-06 S3 ①). ①발동 = 공이 우리 골 중심 40m 안 ②대상 = 골에 가까운 상대 필드 선수(소유자·GK 제외) 상한까지
// ③배정 = 대상마다 가장 가까운 마커, 동률 id, 히스테리시스(대상·마커 둘 다) ④마크 지점 = 상대의 골 쪽 MarkDistance(1.5m, 반응 지연만큼 지난 자리 기준, 실제 상대보다 공 쪽으로는 안 감)
// ⑤반응 지연 = positioning ⑥패스 비행 중 리시버는 도착점 기준(선정·배정·지점 모두) ⑦교대 계측 ⑧시뮬의 지점은 수비 하한 위(GK 선으로 안 내려감, 11-06)
// ⑨마커 후보 = 뒷줄 1차·tracking 미드필더 2차, 상한 = 뒷줄 수(11-06 D1). 순수 함수 테스트의 거리 인자는 읽기 쉽게 2m를 직접 준다
// 팀 0 기준(attackSign +1): 우리 골은 x = −52.5
public class MarkRulesTests
{
    private const int Sign = +1;
    private const float OwnGoalX = -FieldBounds.HalfLength;
    private const float Dt = 0.02f;

    [Test]
    public void 공이_우리_골_중심_40m_안이면_발동하고_밖이면_안_한다()
    {
        Assert.IsTrue(MarkRules.IsActive(OwnGoalX + 40f, 0f, Sign), "정면 40m 경계 포함");
        Assert.IsFalse(MarkRules.IsActive(OwnGoalX + 40.1f, 0f, Sign), "정면 40.1m");
        Assert.IsFalse(MarkRules.IsActive(OwnGoalX + 30f, 30f, Sign), "대각 42.4m");
        Assert.IsFalse(MarkRules.IsActive(0f, 0f, Sign), "센터 서클");
        Assert.IsTrue(MarkRules.IsActive(FieldBounds.HalfLength - 20f, 0f, -1), "팀 1은 +X 골을 지킨다");
    }

    [Test]
    public void 대상은_골에_가까운_순이고_소유자_GK_사거리_밖과_상한_넘는_선수는_빠진다()
    {
        var opponents = new List<TargetInfo>
        {
            new TargetInfo(11, OwnGoalX + 20f, 0f),   // 20m
            new TargetInfo(12, OwnGoalX + 12f, 0f),   // 12m, 소유자
            new TargetInfo(13, OwnGoalX + 15f, 0f),   // 15m
            new TargetInfo(14, OwnGoalX + 3f, 0f),    // 3m, GK
            new TargetInfo(15, OwnGoalX + 45f, 0f),   // 45m, 사거리 밖
            new TargetInfo(16, OwnGoalX + 25f, 0f),   // 25m, 상한 밖
            new TargetInfo(10, OwnGoalX + 15f, 0f),   // 15m, 13번과 동률 → id 작은 10번이 앞
        };
        var result = new List<TargetInfo>();
        MarkRules.SelectTargets(opponents, ownerId: 12, keeperId: 14, Sign, max: 3, System.Array.Empty<MarkAssignment>(), 2f, result);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(10, result[0].PlayerId);
        Assert.AreEqual(13, result[1].PlayerId);
        Assert.AreEqual(11, result[2].PlayerId);
    }

    [Test]
    public void 배정은_대상마다_남은_마커_중_가장_가까운_1명이고_마커가_모자라면_남은_대상은_비운다()
    {
        var targets = new[] { new TargetInfo(20, 0f, 0f), new TargetInfo(21, 10f, 0f), new TargetInfo(22, 20f, 0f) };
        // 1번은 대상 20·21 모두에 가깝지만 먼저 오는 20을 맡는다. 21은 남은 2번이 맡고 22는 마커가 없다
        var markers = new[] { new TargetInfo(1, 2f, 0f), new TargetInfo(2, 13f, 0f) };
        var result = new List<MarkAssignment>();
        MarkRules.Assign(targets, markers, System.Array.Empty<MarkAssignment>(), 2f, result);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(1, result[0].MarkerId);
        Assert.AreEqual(20, result[0].TargetId);
        Assert.AreEqual(2, result[1].MarkerId);
        Assert.AreEqual(21, result[1].TargetId);
    }

    [Test]
    public void 이단_배정은_일차_후보가_바닥났을_때만_이차_후보를_쓴다()
    {
        // 11-06 D1. 대상 20·21, 1차(뒷줄) 1명 = 1번, 2차(tracking 미드필더) = 9번(두 대상 모두에 1번보다 가깝다)
        var targets = new[] { new TargetInfo(20, 0f, 0f), new TargetInfo(21, 10f, 0f) };
        var primary = new[] { new TargetInfo(1, 2f, 0f) };
        var secondary = new[] { new TargetInfo(9, 1f, 0f) };
        var result = new List<MarkAssignment>();
        MarkRules.AssignTiered(targets, primary, secondary, System.Array.Empty<MarkAssignment>(), 2f, result);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(1, result[0].MarkerId, "첫 대상은 더 먼 1차 후보가 맡는다");
        Assert.AreEqual(20, result[0].TargetId);
        Assert.AreEqual(9, result[1].MarkerId, "1차가 바닥난 뒤에만 2차");
        Assert.AreEqual(21, result[1].TargetId);

        var enough = new[] { new TargetInfo(1, 2f, 0f), new TargetInfo(2, 12f, 0f) };
        MarkRules.AssignTiered(targets, enough, secondary, System.Array.Empty<MarkAssignment>(), 2f, result);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(1, result[0].MarkerId);
        Assert.AreEqual(2, result[1].MarkerId, "1차가 충분하면 더 가까운 2차도 안 쓴다");

        MarkRules.AssignTiered(targets, primary, System.Array.Empty<TargetInfo>(), System.Array.Empty<MarkAssignment>(), 2f, result);
        Assert.AreEqual(1, result.Count, "2차가 없으면 남은 대상은 비운다");
    }

    [Test]
    public void 같은_거리면_id_작은_마커이고_지금_맡은_마커는_도전자가_여유_넘게_가까워야_넘긴다()
    {
        var targets = new[] { new TargetInfo(20, 0f, 0f) };
        var result = new List<MarkAssignment>();

        var tie = new[] { new TargetInfo(5, 3f, 0f), new TargetInfo(4, -3f, 0f) };
        MarkRules.Assign(targets, tie, System.Array.Empty<MarkAssignment>(), 2f, result);
        Assert.AreEqual(4, result[0].MarkerId, "동률은 id 작은 쪽");

        var previous = new[] { new MarkAssignment(5, 20) };
        var close = new[] { new TargetInfo(5, 4f, 0f), new TargetInfo(4, 2.5f, 0f) };   // 도전자 1.5m 더 가까움 → 유지
        MarkRules.Assign(targets, close, previous, 2f, result);
        Assert.AreEqual(5, result[0].MarkerId, "여유 안: 맡은 마커 유지");

        var far = new[] { new TargetInfo(5, 5f, 0f), new TargetInfo(4, 2.5f, 0f) };     // 2.5m 더 가까움 → 넘김
        MarkRules.Assign(targets, far, previous, 2f, result);
        Assert.AreEqual(4, result[0].MarkerId, "여유 넘음: 넘긴다");
    }

    [Test]
    public void 마크_지점은_상대에서_우리_골_중심_쪽으로_2m이고_골이_더_가까우면_골_중심이다()
    {
        // 정면 15m에 서 있는 상대 → 골 쪽 2m = 13m
        (float x, float z) front = MarkRules.MarkPoint(OwnGoalX + 15f, 0f, 0f, 0f, 0.5f, Sign, 2f);
        Assert.AreEqual(OwnGoalX + 13f, front.x, 1e-4f);
        Assert.AreEqual(0f, front.z, 1e-4f);

        // 비스듬히 (골 + 6, 8) = 골까지 10m → 골 방향 단위 (−0.6, −0.8) × 2 = (−1.2, −1.6)
        (float x, float z) angled = MarkRules.MarkPoint(OwnGoalX + 6f, 8f, 0f, 0f, 0.5f, Sign, 2f);
        Assert.AreEqual(OwnGoalX + 4.8f, angled.x, 1e-4f);
        Assert.AreEqual(6.4f, angled.z, 1e-4f);

        (float x, float z) onLine = MarkRules.MarkPoint(OwnGoalX + 1f, 0f, 0f, 0f, 0.5f, Sign, 2f);
        Assert.AreEqual(OwnGoalX, onLine.x, 1e-4f, "골까지 1m < 2m");
        Assert.AreEqual(0f, onLine.z, 1e-4f);

        // 팀 1(attackSign −1)은 +X 골을 지킨다
        (float x, float z) away = MarkRules.MarkPoint(FieldBounds.HalfLength - 15f, 0f, 0f, 0f, 0.5f, -1, 2f);
        Assert.AreEqual(FieldBounds.HalfLength - 13f, away.x, 1e-4f);
    }

    [Test]
    public void 마크_지점은_반응_지연만큼_지난_상대_자리를_본다()
    {
        // 상대가 골에서 멀어지는 쪽(+X)으로 6m/s, 지연 0.5초 → 3m 전 자리(골 + 17)에서 골 쪽 2m = 골 + 15
        (float x, float z) lag = MarkRules.MarkPoint(OwnGoalX + 20f, 0f, 6f, 0f, 0.5f, Sign, 2f);
        Assert.AreEqual(OwnGoalX + 15f, lag.x, 1e-4f);
        // 지연이 짧으면(0.25초) 1.5m 전 자리 → 골 + 16.5. 반응이 빠른 마커가 상대에 더 붙는다
        (float x, float z) quick = MarkRules.MarkPoint(OwnGoalX + 20f, 0f, 6f, 0f, 0.25f, Sign, 2f);
        Assert.AreEqual(OwnGoalX + 16.5f, quick.x, 1e-4f);
    }

    [Test]
    public void 반응_지연은_positioning_50이면_ReactionTime이고_높을수록_짧으며_범위_밖은_자른다()
    {
        Assert.AreEqual(MatchTuning.ReactionTime, MarkRules.ReactionDelay(50), 1e-5f);
        Assert.AreEqual(0.25f, MarkRules.ReactionDelay(100), 1e-5f, "0.5 × (1 − 0.5)");
        Assert.AreEqual(0.75f, MarkRules.ReactionDelay(0), 1e-5f, "0.5 × (1 + 0.5)");
        Assert.AreEqual(0.4f, MarkRules.ReactionDelay(70), 1e-5f, "CB 70");
        Assert.AreEqual(MarkRules.ReactionDelay(100), MarkRules.ReactionDelay(150), 1e-6f, "100 위는 100");
        Assert.Less(MarkRules.ReactionDelay(80), MarkRules.ReactionDelay(40), "단조");
    }

    [Test]
    public void 직전_대상은_골_거리_여유를_받아_상한_경계에서_틱마다_교대하지_않는다()
    {
        // 상한 2명, 골 거리가 비슷한 3명. 직전 대상은 10번(15m)·11번(15.5m). 12번이 15m ± 0.4로 앞뒤로 흔들린다
        var previous = new List<MarkAssignment> { new MarkAssignment(1, 10), new MarkAssignment(2, 11) };
        var fresh = new List<TargetInfo>();
        var result = new List<TargetInfo>();
        for (int tick = 0; tick < 10; tick++)
        {
            float wobble = tick % 2 == 0 ? -0.4f : 0.4f;
            List<TargetInfo> opponents = Wobbling(wobble);
            MarkRules.SelectTargets(opponents, -1, -1, Sign, 2, previous, 2f, result);
            Assert.IsFalse(Contains(result, 12), $"{tick}틱: 여유 2m 안이라 흔들리는 12번이 들어오지 않는다");
            Assert.IsTrue(Contains(result, 10) && Contains(result, 11), $"{tick}틱: 대상 그대로");

            MarkRules.SelectTargets(opponents, -1, -1, Sign, 2, System.Array.Empty<MarkAssignment>(), 2f, fresh);
            if (tick % 2 == 0) { Assert.IsTrue(Contains(fresh, 12), "대조: 여유가 없으면 14.6m인 틱에 12번이 11번을 밀어낸다"); }
        }

        // 여유를 넘게 가까워지면(11번 순위 거리 13.5m보다 안쪽) 들어온다
        var closer = new List<TargetInfo> { new TargetInfo(10, OwnGoalX + 15f, 0f), new TargetInfo(11, OwnGoalX + 15.5f, 0f), new TargetInfo(12, OwnGoalX + 12f, 1f) };
        MarkRules.SelectTargets(closer, -1, -1, Sign, 2, previous, 2f, result);
        Assert.AreEqual(12, result[0].PlayerId, "여유 넘게 가까우면 대상에 든다");
    }

    private static List<TargetInfo> Wobbling(float wobble)
    {
        return new List<TargetInfo>
        {
            new TargetInfo(10, OwnGoalX + 15f, 0f),
            new TargetInfo(11, OwnGoalX + 15.5f, 0f),
            new TargetInfo(12, OwnGoalX + 15f + wobble, 1f),
        };
    }

    private static bool Contains(List<TargetInfo> targets, int playerId)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i].PlayerId == playerId) { return true; }
        }
        return false;
    }

    [Test]
    public void 리시버는_패스_도착점에_두고_나머지는_그대로_복사한다()
    {
        var opponents = new[] { new TargetInfo(10, 5f, 1f), new TargetInfo(11, -30f, 4f) };
        var result = new List<TargetInfo>();
        MarkRules.WithReceiverAt(opponents, receiverId: 11, landingX: -45f, landingZ: 2f, result);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(5f, result[0].X, 1e-6f);
        Assert.AreEqual(-45f, result[1].X, 1e-6f, "리시버는 도착점");
        Assert.AreEqual(2f, result[1].Z, 1e-6f);
    }

    [Test]
    public void 교대_수는_직전_틱에_없던_쌍이고_막_발동한_틱은_0이다()
    {
        var previous = new[] { new MarkAssignment(1, 20), new MarkAssignment(2, 21) };
        var same = new[] { new MarkAssignment(1, 20), new MarkAssignment(2, 21) };
        var swapped = new[] { new MarkAssignment(2, 20), new MarkAssignment(3, 22) };
        Assert.AreEqual(0, MarkRules.NewPairs(previous, same));
        Assert.AreEqual(2, MarkRules.NewPairs(previous, swapped), "마커가 바뀐 20번 + 새 대상 22번");
        Assert.AreEqual(0, MarkRules.NewPairs(System.Array.Empty<MarkAssignment>(), swapped), "막 발동");
    }

    [Test]
    public void 골_쪽으로_달리는_상대의_마크_지점은_실제_상대보다_공_쪽으로_넘어가지_않는다()
    {
        // 상대가 골 + 20에서 골 쪽(−X)으로 6m/s, 지연 0.5초 → 본 자리 골 + 23. 거기서 1.5m만 가면 골 + 21.5로 상대보다 공 쪽이 된다.
        // 본 자리가 3m 뒤라 3m를 가서 상대와 나란히(골 + 20) 선다
        (float x, float z) run = MarkRules.MarkPoint(OwnGoalX + 20f, 0f, -6f, 0f, 0.5f, Sign, 1.5f);
        Assert.AreEqual(OwnGoalX + 20f, run.x, 1e-4f);
        Assert.LessOrEqual(run.x - OwnGoalX, 20f + 1e-4f, "실제 상대의 골 쪽(또는 나란히)");

        // 비스듬히 달려도 같다: (골 + 20, 10)에서 골 쪽으로 6m/s → 마크 지점의 골 거리 ≤ 상대의 골 거리
        float speed = 6f;
        float len = (float)System.Math.Sqrt(20f * 20f + 10f * 10f);
        (float x, float z) diag = MarkRules.MarkPoint(OwnGoalX + 20f, 10f, -speed * 20f / len, -speed * 10f / len, 0.5f, Sign, 1.5f);
        float diagDist = (float)System.Math.Sqrt((diag.x - OwnGoalX) * (diag.x - OwnGoalX) + diag.z * diag.z);
        Assert.LessOrEqual(diagDist, len + 1e-3f);

        // 지연이 짧아 본 자리가 1.5m 안쪽 뒤면 평소대로 본 자리에서 1.5m: 0.2초 → 본 자리 골 + 21.2 → 골 + 19.7
        (float x, float z) small = MarkRules.MarkPoint(OwnGoalX + 20f, 0f, -6f, 0f, 0.2f, Sign, 1.5f);
        Assert.AreEqual(OwnGoalX + 19.7f, small.x, 1e-4f);
    }

    [Test]
    public void 박스_앞_중앙_상대는_더_가까운_수미가_아니라_뒷줄_CB가_맡는다()
    {
        // 11-06 유저 Play "상대 공격수가 박스 근처 오면 센터백도 붙어야 하는데 수미가 붙어서 센터백이 안 붙음". 설계 1-2: 중앙 대상 마커 DM 49%·CB 27%.
        // 팀 0: CB 2명(깊이 10.5, z ±7) + DM(깊이 22, 뒷줄 밴드 밖). 팀 1: 소유자는 골에서 30m(박스 앞 전진 구역 밖, 압박 시작 0이라 압박자 없음), 대상 ST는 골에서 19m 중앙
        // DM이 대상에서 3m, CB는 11m: 최근접이면 DM이 맡았다. 뒷줄만 1차 후보라 CB가 맡고 DM은 지역 자리로
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerState cbLeft = sim.AddPlayer(new PlayerState(0, 0, Defender(50), OwnGoalX + 10.5f, -7f));
        PlayerState cbRight = sim.AddPlayer(new PlayerState(1, 0, Defender(50), OwnGoalX + 10.5f, 7f));
        PlayerState dm = sim.AddPlayer(new PlayerState(2, 0, Midfielder(false), OwnGoalX + 22f, 0f));
        sim.AddPlayer(new PlayerState(3, 1, Attacker(), OwnGoalX + 30f, 0f));
        sim.AddPlayer(new PlayerState(4, 1, Attacker(), OwnGoalX + 19f, 0f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 3, OwnGoalX + 30f, 0f);
        sim.Tick(Dt);

        Assert.IsTrue(cbLeft.IsBackLine && cbRight.IsBackLine, "전제: CB 둘은 뒷줄");
        Assert.IsFalse(dm.IsBackLine, "전제: DM(깊이 22)은 뒷줄 밴드(10.5 + 6) 밖");
        Assert.AreEqual(int.MaxValue, cbLeft.PressRank, "전제: 압박자 없음(전진 구역 밖·압박 시작 0)");
        Assert.IsTrue(cbLeft.IsMarker, "동률(둘 다 11m)은 id 작은 CB");
        Assert.IsFalse(cbRight.IsMarker);
        Assert.IsFalse(dm.IsMarker, "수미는 더 가까워도 안 붙는다");
    }

    [Test]
    public void 뒷줄이_모자라면_복귀_역할_수미만_이차로_맡고_아니면_남은_상대는_비운다()
    {
        // CB 한 명이 박스 앞 전진(압박 1순위)으로 빠져 자유 뒷줄 1명, 대상 2명. 수비 역할 tracking(marksAsSecond) 수미가 둘째 대상을 맡는다
        MatchSimulation sim = TwoTargetsOneFreeBack(Midfielder(true), out PlayerState stepOut, out PlayerState cover, out PlayerState dm);
        Assert.AreEqual(0, stepOut.PressRank, "전제: 공에 더 가까운 CB가 박스 앞 전진");
        Assert.IsTrue(cover.IsMarker, "남은 CB가 첫 대상");
        Assert.IsTrue(dm.IsMarker, "복귀 역할 수미가 둘째 대상");
        Assert.Less(dm.MarkZ, 0f, "둘째 대상(z −5)의 골 쪽");

        MatchSimulation plain = TwoTargetsOneFreeBack(Midfielder(false), out PlayerState stepOut2, out PlayerState cover2, out PlayerState dm2);
        Assert.AreEqual(0, stepOut2.PressRank);
        Assert.IsTrue(cover2.IsMarker);
        Assert.IsFalse(dm2.IsMarker, "2차 후보가 아니면 둘째 대상은 비운다(지역 자리)");
    }

    [Test]
    public void 마커_상한은_뒷줄_수라_뒷줄이_남아_있지_않아도_이차_후보는_상한을_넘겨_맡지_않는다()
    {
        // 뒷줄 2명(자유), 대상 3명, tracking 수미 1명. 상한 = 뒷줄 수 2라 대상은 골에 가까운 2명만 뽑히고 수미는 쓰이지 않는다(옛 상수 MaxMarkers 삭제, 11-06 D1)
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerState cbLeft = sim.AddPlayer(new PlayerState(0, 0, Defender(50), OwnGoalX + 10.5f, -7f));
        PlayerState cbRight = sim.AddPlayer(new PlayerState(1, 0, Defender(50), OwnGoalX + 10.5f, 7f));
        PlayerState dm = sim.AddPlayer(new PlayerState(2, 0, Midfielder(true), OwnGoalX + 22f, 0f));
        sim.AddPlayer(new PlayerState(3, 1, Attacker(), OwnGoalX + 30f, 0f));
        sim.AddPlayer(new PlayerState(4, 1, Attacker(), OwnGoalX + 19f, 0f));
        sim.AddPlayer(new PlayerState(5, 1, Attacker(), OwnGoalX + 20f, 6f));
        sim.AddPlayer(new PlayerState(6, 1, Attacker(), OwnGoalX + 20f, -6f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 3, OwnGoalX + 30f, 0f);
        sim.Tick(Dt);

        Assert.IsTrue(cbLeft.IsMarker);
        Assert.IsTrue(cbRight.IsMarker);
        Assert.IsFalse(dm.IsMarker, "상한(뒷줄 2) 밖 셋째 대상은 대상이 아니다");
    }

    [Test]
    public void 이차_후보는_뒷줄_바로_앞_한_줄만이라_복귀_역할_공격수는_내려와_맡지_않는다()
    {
        // 11-07 리뷰 🟡: ST 슬롯에 tracking을 주면 공격수가 2차 마커가 됐다. 둘째 줄(뒷줄 아닌 선수 중 가장 깊은 자리 + 6m)만 2차.
        // 팀 0: CB 2명(10.5) + DM(22, 둘째 줄) + tracking ST(수비 자리 35, 둘째 줄 밖). CB 한 명이 전진으로 빠져 자유 뒷줄 1, 대상 2. ST가 둘째 대상 옆에 있어도 안 맡는다
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerState stepOut = sim.AddPlayer(new PlayerState(0, 0, Defender(50), OwnGoalX + 10.5f, -7f));
        PlayerState cover = sim.AddPlayer(new PlayerState(1, 0, Defender(50), OwnGoalX + 10.5f, 7f));
        PlayerState dm = sim.AddPlayer(new PlayerState(2, 0, Midfielder(false), OwnGoalX + 22f, 0f));
        PlayerState st = sim.AddPlayer(new PlayerState(3, 0, TrackingStriker(), OwnGoalX + 16f, -5f, OwnGoalX + 35f, -5f));
        sim.AddPlayer(new PlayerState(4, 1, Attacker(), OwnGoalX + 18f, 0f));
        sim.AddPlayer(new PlayerState(5, 1, Attacker(), OwnGoalX + 14f, 5f));
        sim.AddPlayer(new PlayerState(6, 1, Attacker(), OwnGoalX + 14f, -5f));
        sim.Kickoff();
        st.X = OwnGoalX + 16f;   // 킥오프가 공격 시 자리로 돌려놓아도 실제 자리는 둘째 대상 옆 2m. 줄 판정은 수비 시 자리(35)라 바뀌지 않는다
        st.Z = -5f;
        sim.Ball = BallRules.Own(sim.Ball, 4, OwnGoalX + 18f, 0f);
        sim.Tick(Dt);

        Assert.AreEqual(0, stepOut.PressRank, "전제: CB 한 명이 전진");
        Assert.IsTrue(dm.IsSecondLine, "DM은 둘째 줄");
        Assert.IsFalse(st.IsSecondLine, "ST(35)는 둘째 줄 기준 22 + 6 밖");
        Assert.IsTrue(cover.IsMarker);
        Assert.IsFalse(st.IsMarker, "복귀 역할이라도 공격수는 2차 마커가 아니다");
        Assert.IsFalse(dm.IsMarker, "DM은 marksAsSecond가 아니라 안 맡는다 → 둘째 대상은 비운다");
    }

    [Test]
    public void 이차_마커가_맡던_상대는_뒷줄이_풀리면_뒷줄이_되찾는다()
    {
        // 전진으로 빠진 CB가 공이 구역 밖으로 가 풀리면, 1차(뒷줄) 배정이 먼저라 tracking 수미가 맡던 둘째 대상을 CB가 되찾고 수미는 지역 자리로
        MatchSimulation sim = TwoTargetsOneFreeBack(Midfielder(true), out PlayerState stepOut, out PlayerState cover, out PlayerState dm);
        Assert.IsTrue(dm.IsMarker, "전제: 수미가 둘째 대상을 맡고 있다");

        sim.Ball = BallRules.Own(sim.Ball, 3, OwnGoalX + 30f, 0f);   // 전진 구역(21.5m) 밖으로
        sim.Tick(Dt);
        Assert.AreEqual(int.MaxValue, stepOut.PressRank, "전진이 풀렸다");
        Assert.IsTrue(stepOut.IsMarker, "풀린 CB가 둘째 대상을 되찾는다(1차 배정이 먼저)");
        Assert.IsTrue(cover.IsMarker);
        Assert.IsFalse(dm.IsMarker, "수미는 지역 자리로");
    }

    [Test]
    public void 진영_교체_뒤에도_뒷줄_판정과_마커는_같다()
    {
        // 수비 시 자리는 하프타임 SwitchSides에서 x 부호만 뒤집혀 깊이가 불변이다. 전반 장면(CB가 맡고 DM은 안 맡음)을 하프타임을 지나 미러로 다시 놓아도 같다.
        // 진영 교체는 시뮬 경로(KickoffBy → HalfTimeTick)로 일으킨다: AttackSignOf도 같이 뒤집혀야 마크 발동(우리 골 40m)이 맞는 골을 본다
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerState cbLeft = sim.AddPlayer(new PlayerState(0, 0, Defender(50), OwnGoalX + 10.5f, -7f));
        PlayerState cbRight = sim.AddPlayer(new PlayerState(1, 0, Defender(50), OwnGoalX + 10.5f, 7f));
        PlayerState dm = sim.AddPlayer(new PlayerState(2, 0, Midfielder(true), OwnGoalX + 22f, 0f));
        PlayerState owner = sim.AddPlayer(new PlayerState(3, 1, Attacker(), OwnGoalX + 30f, 0f));
        PlayerState target = sim.AddPlayer(new PlayerState(4, 1, Attacker(), OwnGoalX + 19f, 0f));
        sim.KickoffBy(0);
        sim.Ball = BallRules.Own(sim.Ball, 3, OwnGoalX + 30f, 0f);
        sim.Tick(Dt);
        Assert.IsTrue(cbLeft.IsMarker && !dm.IsMarker, "전제: 전반");

        int ht = sim.HalfTimeTick;
        while (sim.TickCount < ht) { sim.Tick(Dt); }
        Assert.IsTrue(sim.SidesSwitched, "전제: 하프타임을 지났다");
        Assert.AreEqual(-OwnGoalX - 10.5f, cbLeft.DefendHomeX, 1e-4f, "수비 시 자리 x가 미러");

        PlayerState[] all = { cbLeft, cbRight, dm, owner, target };
        float[] mirroredX = { -(OwnGoalX + 10.5f), -(OwnGoalX + 10.5f), -(OwnGoalX + 22f), -(OwnGoalX + 30f), -(OwnGoalX + 19f) };
        for (int i = 0; i < all.Length; i++) { all[i].X = mirroredX[i]; }
        sim.Ball = BallRules.Own(sim.Ball, 3, owner.X, owner.Z);
        sim.Tick(Dt);

        Assert.IsTrue(cbLeft.IsBackLine && cbRight.IsBackLine, "뒷줄 그대로");
        Assert.IsTrue(dm.IsSecondLine && !dm.IsBackLine, "둘째 줄 그대로");
        Assert.IsTrue(cbLeft.IsMarker, "뒤집힌 진영에서도 CB가 맡는다");
        Assert.IsFalse(cbRight.IsMarker);
        Assert.IsFalse(dm.IsMarker);
    }

    private static PlayerStats TrackingStriker()
    {
        return new PlayerStats { RoleId = "ST", VariantId = "st_test", Speed = 50, PressRange = 12f, ShotBias = 1f, PassLength = 15f, Positioning = 50, MarksAsSecond = true };
    }

    // 팀 0: CB 2명(깊이 10.5, z ±7) + 미드필더(깊이 22). 팀 1: 소유자 골에서 18m 중앙(박스 앞 전진 구역 안), 대상 2명 (골 + 14, ±5)
    private static MatchSimulation TwoTargetsOneFreeBack(PlayerStats midfielder, out PlayerState stepOut, out PlayerState cover, out PlayerState dm)
    {
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        stepOut = sim.AddPlayer(new PlayerState(0, 0, Defender(50), OwnGoalX + 10.5f, -7f));
        cover = sim.AddPlayer(new PlayerState(1, 0, Defender(50), OwnGoalX + 10.5f, 7f));
        dm = sim.AddPlayer(new PlayerState(2, 0, midfielder, OwnGoalX + 22f, 0f));
        sim.AddPlayer(new PlayerState(3, 1, Attacker(), OwnGoalX + 18f, 0f));
        sim.AddPlayer(new PlayerState(4, 1, Attacker(), OwnGoalX + 14f, 5f));
        sim.AddPlayer(new PlayerState(5, 1, Attacker(), OwnGoalX + 14f, -5f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 3, OwnGoalX + 18f, 0f);
        sim.Tick(Dt);
        return sim;
    }

    private static PlayerStats Midfielder(bool marksAsSecond)
    {
        return new PlayerStats { RoleId = "DM", VariantId = "dm_test", Speed = 50, PressRange = 10f, ShotBias = 1f, PassLength = 20f, Positioning = 50, MarksAsSecond = marksAsSecond };
    }

    private static PlayerStats Defender(int positioning)
    {
        return new PlayerStats { RoleId = "CB", VariantId = "cb_test", Speed = 50, PressRange = 6f, ShotBias = 1f, PassLength = 20f, Positioning = positioning };
    }

    private static PlayerStats Attacker()
    {
        return new PlayerStats { RoleId = "ST", VariantId = "st_test", Speed = 50, PressRange = 12f, ShotBias = 1f, PassLength = 15f };
    }

    [Test]
    public void 시뮬은_압박자를_빼고_리시버에_가장_가까운_수비를_마커로_넣고_사거리_밖이면_끈다()
    {
        // 팀 1이 우리(팀 0) 골에서 20m 공을 쥐었다. 0번은 공 옆 CB(박스 앞 전진 = 압박 1순위), 1번은 리시버(골 + 12, z 6) 옆
        // 두 CB의 수비 자리 깊이 차는 5.5m(뒷줄 밴드 6m 안, 11-06 D1): 둘 다 뒷줄이라 0번이 전진 후보, 1번이 마커 후보다
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerState presser = sim.AddPlayer(new PlayerState(0, 0, Defender(50), OwnGoalX + 19f, 0f));
        PlayerState marker = sim.AddPlayer(new PlayerState(1, 0, Defender(50), OwnGoalX + 13.5f, 9f));
        sim.AddPlayer(new PlayerState(2, 1, Attacker(), OwnGoalX + 20f, 0f));
        sim.AddPlayer(new PlayerState(3, 1, Attacker(), OwnGoalX + 12f, 6f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 2, OwnGoalX + 20f, 0f);
        sim.Tick(Dt);

        Assert.AreEqual(0, presser.PressRank, "공 옆 선수는 압박");
        Assert.IsFalse(presser.IsMarker, "압박자는 마커가 아니다");
        Assert.IsTrue(marker.IsMarker);
        (float x, float z) expected = MarkRules.MarkPoint(OwnGoalX + 12f, 6f, 0f, 0f, MarkRules.ReactionDelay(50), Sign, MatchTuning.MarkDistance);
        float expectedX = PositionRules.AboveDefendFloor(expected.x, OwnGoalX + 20f, Sign);
        Assert.AreEqual(expectedX, marker.MarkX, 1e-3f, "리시버의 골 쪽 MarkDistance(1.5m), 공이 박스 밖이라 하한(10m) 위로");
        Assert.AreEqual(expected.z, marker.MarkZ, 1e-3f);

        var far = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerState idle = far.AddPlayer(new PlayerState(1, 0, Defender(50), OwnGoalX + 12f, 9f));
        far.AddPlayer(new PlayerState(2, 1, Attacker(), OwnGoalX + 45f, 0f));
        far.AddPlayer(new PlayerState(3, 1, Attacker(), OwnGoalX + 12f, 6f));
        far.Kickoff();
        far.Ball = BallRules.Own(far.Ball, 2, OwnGoalX + 45f, 0f);
        far.Tick(Dt);
        Assert.IsFalse(idle.IsMarker, "공이 골에서 45m면 마크 안 함");
    }
    [Test]
    public void 상대_패스가_날아가는_중이면_리시버를_도착점_기준으로_고르고_그_골_쪽에_선다()
    {
        // 팀 1 패서가 우리 골에서 38m(발동 구역 안), 리시버는 측면 (골 + 30, 30) = 42.4m(지금 자리는 사거리 밖). 리드 패스 도착점은 리시버 앞(골 쪽)이라 40m 안
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.PassOnce(2, 3), TestThreat.Real);
        PlayerState marker = sim.AddPlayer(new PlayerState(1, 0, Defender(50), OwnGoalX + 20f, 20f));
        PlayerState passer = sim.AddPlayer(new PlayerState(2, 1, Attacker(), OwnGoalX + 38f, 0f));
        PlayerState receiver = sim.AddPlayer(new PlayerState(3, 1, Attacker(), OwnGoalX + 30f, 30f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 2, OwnGoalX + 38f, 0f);
        sim.Tick(Dt);   // 패서가 찬다
        Assert.AreEqual(1, sim.PassCount, "패스가 나갔다");
        sim.Tick(Dt);   // 비행 중 스냅샷

        Assert.IsTrue(receiver.IsPassTarget);
        float landingX = receiver.PassTargetX;
        float landingZ = receiver.PassTargetZ;
        Assert.LessOrEqual(MathDistance(landingX - OwnGoalX, landingZ), MatchTuning.MaxShotRange, "전제: 도착점은 사거리 안");
        Assert.Greater(MathDistance(receiver.X - OwnGoalX, receiver.Z), MatchTuning.MaxShotRange, "전제: 리시버 지금 자리는 사거리 밖");
        Assert.IsTrue(marker.IsMarker, "도착점 기준이라 지금 대상이다");
        (float x, float z) expected = MarkRules.MarkPoint(landingX, landingZ, 0f, 0f, 0f, Sign, MatchTuning.MarkDistance);
        Assert.AreEqual(expected.x, marker.MarkX, 1e-3f, "도착점의 골 쪽");
        Assert.AreEqual(expected.z, marker.MarkZ, 1e-3f);
        Assert.IsFalse(passer.IsMarker);
    }

    [Test]
    public void GK_옆_상대를_맡아도_마커는_수비_하한에_서고_그_상대는_오프사이드_위치다()
    {
        // 11-06 유저 Play "공격수가 GK 옆에 있으니까 붙지도 않고 슛 찰 각이 생김". 하한 전엔 마크 지점이 상대의 골 쪽 1.5m라 CB가 GK 선(골에서 4.5m)까지 내려갔고
        // 오프사이드 기준선(골 쪽 2번째 상대)도 같이 내려가 GK 옆 공격수가 온사이드였다.
        // 팀 1이 우리 골에서 20m(박스 밖) 공을 쥐었다. 0번은 공 쪽(박스 앞 전진 = 압박 1순위, 깊이 13.5로 1번과 같은 뒷줄), 1번 마커는 GK 옆 상대(골 + 4.5, z 2) 근처
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        sim.AddPlayer(new PlayerState(0, 0, Defender(50), OwnGoalX + 13.5f, 0f));
        PlayerState marker = sim.AddPlayer(new PlayerState(1, 0, Defender(50), OwnGoalX + 9f, 4f));
        sim.AddPlayer(new PlayerState(2, 1, Attacker(), OwnGoalX + 20f, 0f));
        PlayerState lurker = sim.AddPlayer(new PlayerState(3, 1, Attacker(), OwnGoalX + 4.5f, 2f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 2, OwnGoalX + 20f, 0f);
        sim.Tick(Dt);

        Assert.IsTrue(marker.IsMarker, "전제: GK 옆 상대가 마크 대상");
        Assert.AreEqual(OwnGoalX + MatchTuning.DefendFloorBallOutsideBox, marker.MarkX, 1e-3f, "공이 박스 밖이면 박스 밖 하한(10m)에 선다");

        // 마커가 그 자리에 섰을 때 팀 1 기준 온사이드 선. 우리 GK는 편성 −48(골에서 4.5m)
        var defenders = new List<TargetInfo>
        {
            new TargetInfo(10, -48f, 0f),
            new TargetInfo(0, OwnGoalX + 13.5f, 0f),
            new TargetInfo(1, marker.MarkX, marker.MarkZ),
        };
        float line = OffsideRules.OnsideLine(defenders, -1, sim.Ball.X);
        Assert.IsTrue(OffsideRules.IsOffsidePosition(lurker.X, -1, line), "GK 옆 상대는 마커(골 쪽 2번째)보다 골 쪽이라 오프사이드 위치");

        // 순수 함수 성질: 상대가 우리 진영 어디에 있든, 공이 박스 안이든 밖이든, 하한을 거친 마크 지점은 GK 라인(스위퍼 최댓값 6.9m)보다 앞이다
        float keeperDepth = FieldBounds.HalfLength + PositionRules.KeeperLineX(-48f, 0f, Sign, 8f);
        float[] ballDepths = { 5f, 12f, 16.5f, 25f, 39f };
        for (int b = 0; b < ballDepths.Length; b++)
        {
            for (float tx = 0.5f; tx <= 40f; tx += 1.5f)
            {
                for (float tz = -20f; tz <= 20f; tz += 4f)
                {
                    (float x, float z) raw = MarkRules.MarkPoint(OwnGoalX + tx, tz, 0f, 0f, 0f, Sign, MatchTuning.MarkDistance);
                    float floored = PositionRules.AboveDefendFloor(raw.x, OwnGoalX + ballDepths[b], Sign);
                    Assert.GreaterOrEqual(floored - OwnGoalX, keeperDepth + 0.5f, $"공 깊이 {ballDepths[b]}, 상대 ({tx}, {tz})");
                }
            }
        }
    }

    private static float MathDistance(float dx, float dz)
    {
        return (float)System.Math.Sqrt(dx * dx + dz * dz);
    }
}
