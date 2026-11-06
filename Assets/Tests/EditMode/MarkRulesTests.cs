using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Placement;
using NUnit.Framework;

// MarkRules 검증(11-06 S3 ①). ①발동 = 공이 우리 골 중심 40m 안 ②대상 = 골에 가까운 상대 필드 선수(소유자·GK 제외) 상한까지
// ③배정 = 대상마다 가장 가까운 마커, 동률 id, 히스테리시스(대상·마커 둘 다) ④마크 지점 = 상대의 골 쪽 MarkDistance(1.5m, 반응 지연만큼 지난 자리 기준, 실제 상대보다 공 쪽으로는 안 감)
// ⑤반응 지연 = positioning ⑥패스 비행 중 리시버는 도착점 기준(선정·배정·지점 모두) ⑦교대 계측 ⑧시뮬의 지점은 수비 하한 위(GK 선으로 안 내려감, 11-06). 순수 함수 테스트의 거리 인자는 읽기 쉽게 2m를 직접 준다
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
        // 팀 1이 우리(팀 0) 골에서 20m 공을 쥐었다. 0번은 공 옆 CB(박스 앞 전진 = 압박 1순위), 1번은 리시버(골 + 12, z 6) 옆 3m
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerState presser = sim.AddPlayer(new PlayerState(0, 0, Defender(50), OwnGoalX + 19f, 0f));
        PlayerState marker = sim.AddPlayer(new PlayerState(1, 0, Defender(50), OwnGoalX + 12f, 9f));
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
        // 팀 1이 우리 골에서 20m(박스 밖) 공을 쥐었다. 0번은 공 옆(압박), 1번 마커는 GK 옆 상대(골 + 4.5, z 2) 근처
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        sim.AddPlayer(new PlayerState(0, 0, Defender(50), OwnGoalX + 19f, 0f));
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
            new TargetInfo(0, OwnGoalX + 19f, 0f),
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
