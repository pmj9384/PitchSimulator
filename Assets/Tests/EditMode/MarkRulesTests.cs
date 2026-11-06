using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Placement;
using NUnit.Framework;

// MarkRules 검증(11-06 S3 ①). ①발동 = 공이 우리 골 중심 40m 안 ②대상 = 골에 가까운 상대 필드 선수(소유자·GK 제외) 상한까지
// ③배정 = 대상마다 가장 가까운 마커, 동률 id, 히스테리시스 ④마크 지점 = 상대의 골 쪽 2m(반응 지연만큼 지난 자리 기준) ⑤반응 지연 = positioning
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
        MarkRules.SelectTargets(opponents, ownerId: 12, keeperId: 14, Sign, max: 3, result);

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
        Assert.AreEqual(expected.x, marker.MarkX, 1e-3f, "리시버의 골 쪽 2m");
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
}
