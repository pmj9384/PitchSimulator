using System.Collections.Generic;
using Game.Core.Match;
using NUnit.Framework;

// 사전 런 판정(11-03 ⑤, 설계초안-침투-스루 3절 C). 시작 조건 = "지금은 위험하지만 달리면 가능". 시간 창·상태 없음
public class RunRulesTests
{
    private const float Average = 12.2f;
    private const float Allow = 0.5f;
    private const float WingerSpeed = 11.2f;   // speed 80

    // 상대 GK(50,0)와 수비 둘(30,±28): 수비 라인(공 제외) = 30, 가운데 길은 비어 있다
    private static List<TargetInfo> OpenDefense()
    {
        return new List<TargetInfo> { new TargetInfo(100, 50f, 0f), new TargetInfo(101, 30f, -28f), new TargetInfo(102, 30f, 28f) };
    }

    // 기본 z = 0은 일부러다: 수비 둘(z ±28) 사이 가운데 길로 달리는 가장 단순한 배치. 옆 윙어는 z 인자로 준다
    private static RunCandidate Winger(float velX = 0f, int style = RunRules.StyleBehind, int id = 1, float x = 15f, float z = 0f)
    {
        return new RunCandidate(id, x, z, velX, 0f, WingerSpeed, style);
    }

    private static RunOffer Find(List<TargetInfo> opponents, params RunCandidate[] runners)
    {
        return RunRules.FindOffer(10f, 0f, +1, Average, Allow, false, opponents, runners, new List<RunOffer>(), 99, TestThreat.Real, 1, 15f, 1);
    }

    private const float PassDistance = 30f;

    // 패서 (0,0) → 목표 (30,0) 땅볼과 공 도착 시각
    private static BallState PassBall(out float ballTime)
    {
        float kick = PassRules.KickSpeed(PassDistance, Average, MatchTuning.BallDeceleration, MatchTuning.PassSpeedMax);
        float disc = kick * kick - 2f * MatchTuning.BallDeceleration * PassDistance;
        ballTime = (kick - (float)System.Math.Sqrt(disc)) / MatchTuning.BallDeceleration;
        return BallRules.Kick(BallState.FreeAt(0f, 0f), PassDistance, 0f, kick);
    }

    private static RunCandidate Runner(float x, float z, float velX = 0f, float velZ = 0f, float effort = 1f)
    {
        return new RunCandidate(1, x, z, velX, velZ, 7f, RunRules.StyleBehind, effort);
    }

    // Seek로 받으면 러너가 공과 만나는 창이 좁다(일찍 닿으면 지나쳐 가고 늦으면 공이 먼저 지나간다). 그래서 아래 테스트는 창의 모양을 성질로 잠그고 특정 거리 값에는 기대지 않는다
    [Test]
    public void 목표점에_서_있는_러너는_공을_받고_멀리_있는_러너는_못_받는다()
    {
        BallState ball = PassBall(out float ballTime);
        Assert.IsTrue(RunRules.RunnerMeetsBall(Runner(PassDistance, 0f), 0f, 0f, PassDistance, 0f, ball, ballTime - 0.1f));
        Assert.IsFalse(RunRules.RunnerMeetsBall(Runner(PassDistance, 40f), 0f, 0f, PassDistance, 0f, ball, ballTime - 0.1f), "40m 옆은 공이 먼저 지나간다");
    }

    [Test]
    public void 감속_없이_목표를_일찍_지나치는_러너는_공이_도착할_때_거기_없어_못_받는다()
    {
        // 판정 롤아웃이 Seek(실행의 런 패스 수신 모드)이라서 필요한 성질. Arrive였다면 목표에서 멈춰 받는다(MovementRulesTests의 Arrive 대 Seek 대비)
        BallState ball = PassBall(out float ballTime);
        RunCandidate early = Runner(PassDistance, 2f, velZ: -7f);   // 공보다 훨씬 일찍 목표에 닿는다
        Assert.IsFalse(RunRules.RunnerMeetsBall(early, early.VelX, early.VelZ, PassDistance, 0f, ball, ballTime - 0.1f), "Seek: 지나쳐 간다");
    }

    [Test]
    public void 정지_출발은_못_닿아도_이미_달리는_러너는_닿는_거리가_있다()
    {
        BallState ball = PassBall(out float ballTime);
        float deadline = ballTime - 0.1f;
        bool found = false;
        for (float offset = 3f; offset <= 40f; offset += 0.25f)
        {
            RunCandidate runner = Runner(PassDistance, offset);
            bool stand = RunRules.RunnerMeetsBall(runner, 0f, 0f, PassDistance, 0f, ball, deadline);
            bool run = RunRules.RunnerMeetsBall(runner, 0f, -7f, PassDistance, 0f, ball, deadline);
            if (!stand && run) { found = true; }
        }
        Assert.IsTrue(found, "가속 손실이 없는 러너가 닿는 거리");
    }

    [Test]
    public void 여유를_두면_닿는_러너가_줄기만_하고_늘지_않는다()
    {
        BallState ball = PassBall(out float ballTime);
        bool strictlyFewer = false;
        for (float offset = 0f; offset <= 40f; offset += 0.25f)
        {
            RunCandidate runner = Runner(PassDistance, offset);
            bool loose = RunRules.RunnerMeetsBall(runner, 0f, 0f, PassDistance, 0f, ball, ballTime);
            bool strict = RunRules.RunnerMeetsBall(runner, 0f, 0f, PassDistance, 0f, ball, ballTime - 0.3f);
            Assert.IsFalse(strict && !loose, $"offset {offset}: 여유 0.3초에 닿는데 여유 0에 못 닿을 수는 없다");
            if (loose && !strict) { strictlyFewer = true; }
        }
        Assert.IsTrue(strictlyFewer, "여유가 판정을 바꾸는 러너가 있다");
    }

    [Test]
    public void 피로로_최고_속도가_깎이면_판정이_달라진다()
    {
        BallState ball = PassBall(out float ballTime);
        float deadline = ballTime - 0.1f;
        bool differs = false;
        for (float offset = 3f; offset <= 40f; offset += 0.25f)
        {
            bool fresh = RunRules.RunnerMeetsBall(Runner(PassDistance, offset), 0f, 0f, PassDistance, 0f, ball, deadline);
            bool tired = RunRules.RunnerMeetsBall(Runner(PassDistance, offset, effort: 0.6f), 0f, 0f, PassDistance, 0f, ball, deadline);
            if (fresh != tired) { differs = true; }
        }
        Assert.IsTrue(differs, "Effort가 이동식에 들어간다");
    }

    [Test]
    public void 정지한_빠른_러너는_지금은_위험하고_달리면_가능하다()
    {
        (float now, float run) risks = RunRules.SpotRisks(10f, 0f, 45f, 0f, Average, Allow, OpenDefense(), Winger());
        Assert.Greater(risks.now, Allow, "서 있으면 공이 먼저 지나간다");
        Assert.LessOrEqual(risks.run, Allow, "달리고 있으면 착지점에서 만난다");
    }

    [Test]
    public void 이미_달리는_러너는_지금도_가능하다()
    {
        // RunArrivalMargin(0.1초)보다 훨씬 넉넉한 구간을 잡는다: 달려오는 러너가 닿는 x 범위가 여유 0.1초가 만드는 차이(약 1m)보다 넓다
        int feasible = 0;
        for (float x = 5f; x <= 30f; x += 0.5f)
        {
            (float now, float run) risks = RunRules.SpotRisks(10f, 0f, 45f, 0f, Average, Allow, OpenDefense(), Winger(velX: WingerSpeed, x: x));
            if (risks.now <= Allow) { feasible++; }
        }
        Assert.GreaterOrEqual(feasible, 6, "3m 이상 폭으로 지금도 가능한 출발 위치가 있다");
    }

    [Test]
    public void 옆_20m의_윙어는_Seek_창_밖이라_달려도_공을_못_만난다_현재_한계()
    {
        // 알려진 한계(설계 A1): Seek로 받으면 공과 만나는 창이 좁아, 한참 옆에서 오는 러너는 목표를 일찍 지나치거나 늦게 닿는다.
        // 이 테스트는 지금의 약점을 잠근다. 수신 방식을 바꿔 이 한계가 풀리면 일부러 이 단언을 뒤집는다
        foreach (float z in new[] { 20f, -20f })
        {
            (float now, float run) risks = RunRules.SpotRisks(10f, 0f, 45f, 0f, Average, Allow, OpenDefense(), Winger(z: z));
            Assert.AreEqual(1f, risks.run, 1e-5f, $"z={z}: 달려도 만나지 못한다");
        }
    }

    [Test]
    public void 공이_여유보다_빨리_오면_목표점에_서_있어도_도달_불가다()
    {
        // deadline < 0: 공 도착 − 여유가 이미 지났다. 러너가 목표점 위에 있어도 "먼저 자리를 잡고 있어야 한다"를 못 채운다
        BallState ball = PassBall(out float ballTime);
        Assert.IsFalse(RunRules.RunnerMeetsBall(Runner(PassDistance, 0f), 0f, 0f, PassDistance, 0f, ball, -0.1f));
        Assert.IsTrue(RunRules.RunnerMeetsBall(Runner(PassDistance, 0f), 0f, 0f, PassDistance, 0f, ball, ballTime - 0.1f), "같은 러너가 여유 안이면 받는다(대조)");
    }

    [Test]
    public void 패서_자리로_가는_패스와_최고_속도_0_러너는_위험_1이다()
    {
        // 거리 0은 찰 방향이 없어 패스가 아니다. MaxSpeed 0은 움직일 수 없다. 둘 다 위험 1(도달 불가)로 닫아 0 나누기·영벡터 킥을 피한다
        (float now, float run) sameSpot = RunRules.SpotRisks(10f, 0f, 10f, 0f, Average, Allow, OpenDefense(), Winger());
        Assert.AreEqual(1f, sameSpot.now, 1e-5f);
        Assert.AreEqual(1f, sameSpot.run, 1e-5f);

        var frozen = new RunCandidate(2, 15f, 0f, 0f, 0f, 0f, RunRules.StyleBehind);
        BallState ball = PassBall(out float ballTime);
        Assert.IsFalse(RunRules.RunnerMeetsBall(frozen, 0f, 0f, PassDistance, 0f, ball, ballTime));
        (float now, float run) stuck = RunRules.SpotRisks(10f, 0f, 45f, 0f, Average, Allow, OpenDefense(), frozen);
        Assert.AreEqual(1f, stuck.now, 1e-5f);
        Assert.AreEqual(1f, stuck.run, 1e-5f);
    }

    [Test]
    public void 느린_러너는_달려도_공을_못_따라가_위험_1이다()
    {
        var slow = new RunCandidate(2, 15f, 0f, 0f, 0f, 5f, RunRules.StyleBehind);
        (float now, float run) risks = RunRules.SpotRisks(10f, 0f, 45f, 0f, Average, Allow, OpenDefense(), slow);
        Assert.AreEqual(1f, risks.run, 1e-5f);
        Assert.AreEqual(1f, risks.now, 1e-5f);
    }

    [Test]
    public void 상대가_패스_길에_있으면_러너가_빨라도_위험하다()
    {
        // 패스 길(10,0)→(45,0) 위 (28,0.5)에 수비. 땅볼 가로채기 판정(LandingRisk)이 그대로 들어온다
        List<TargetInfo> blocked = OpenDefense();
        blocked.Add(new TargetInfo(103, 28f, 0.5f));
        (float now, float run) risks = RunRules.SpotRisks(10f, 0f, 45f, 0f, Average, Allow, blocked, Winger());
        Assert.Greater(risks.run, Allow);
    }

    [Test]
    public void 뒤공간_러너는_수비_라인_뒤_목표를_받고_위험은_지금_값이다()
    {
        RunOffer offer = Find(OpenDefense(), Winger());
        Assert.AreEqual(1, offer.RunnerId);
        Assert.GreaterOrEqual(offer.X, 30f - MatchTuning.RunBehindMargin, "목표는 수비 라인(공 제외) 근처·너머");
        Assert.Greater(offer.Risk, Allow, "시작 조건: 지금은 허용치 밖");
    }

    [Test]
    public void 이미_패스가_되는_러너는_런_제안이_없다()
    {
        Assert.AreEqual(RunOffer.NoRunner, Find(OpenDefense(), Winger(velX: WingerSpeed)).RunnerId);
    }

    [Test]
    public void 달려도_안_되는_러너와_침투_성향이_없는_선수는_제안이_없다()
    {
        var slow = new RunCandidate(2, 15f, 0f, 0f, 0f, 2f, RunRules.StyleBehind);
        Assert.AreEqual(RunOffer.NoRunner, Find(OpenDefense(), slow).RunnerId, "공이 먼저 지나가거나 따라가지 못한다");
        Assert.AreEqual(RunOffer.NoRunner, Find(OpenDefense(), Winger(style: RunRules.StyleNone)).RunnerId);
        Assert.AreEqual(RunOffer.NoRunner, Find(OpenDefense(), Winger(style: RunRules.StyleLate)).RunnerId, "박스에 늦게 도착은 ⑤에서 값만 있다");
    }

    [Test]
    public void 상대가_2명_미만이면_수비_라인이_없어_뒤공간_제안이_없다()
    {
        Assert.AreEqual(RunOffer.NoRunner, Find(new List<TargetInfo> { new TargetInfo(100, 50f, 0f) }, Winger()).RunnerId);
    }

    [Test]
    public void 오프사이드_위치의_러너와_운반자_본인과_이미_제안을_받은_선수는_건너뛴다()
    {
        Assert.AreEqual(RunOffer.NoRunner, Find(OpenDefense(), Winger(x: 31f)).RunnerId, "선(30) 너머 러너");
        Assert.AreEqual(RunOffer.NoRunner, RunRules.FindOffer(10f, 0f, +1, Average, Allow, false, OpenDefense(), new[] { Winger(id: 99) }, new List<RunOffer>(), 99, TestThreat.Real, 1, 15f, 1).RunnerId, "운반자");
        var existing = new List<RunOffer> { new RunOffer(1, 40f, 0f, 0.9f) };
        Assert.AreEqual(RunOffer.NoRunner, RunRules.FindOffer(10f, 0f, +1, Average, Allow, false, OpenDefense(), new[] { Winger() }, existing, 99, TestThreat.Real, 1, 15f, 1).RunnerId, "이미 제안을 받은 선수");
    }

    [Test]
    public void 후보_러너가_여럿이면_패서가_높게_값_매길_목표의_러너가_이긴다_동률은_PlayerId_작은_쪽()
    {
        // 같은 모양의 두 러너는 z만 좌우 대칭: 격자가 폭 대칭이라 값이 같아 PlayerId 작은 쪽
        RunOffer tie = Find(OpenDefense(), Winger(id: 7, z: 5f), Winger(id: 3, z: -5f));
        Assert.AreEqual(3, tie.RunnerId);

        // "값이 큰 목표의 러너가 이긴다"는 지금 잠글 수 없다(11-05 재검증): 제안의 Risk는 "지금 패스하면"의 위험이라 늘 1이고,
        // FindOffer가 그 값으로 성공 확률을 재서 모든 후보의 값이 같아진다. 그래서 러너는 사실상 PlayerId로 고른다.
        // 값 비교를 "달리고 있을 때"의 위험으로 고치는 태스크가 장부에 있다. 고치면 TestThreat.CenterLane으로 중앙 러너가 이기는 단언을 넣는다
        RunOffer offer = Find(OpenDefense(), Winger(id: 7, z: 2f));
        Assert.AreEqual(1f, offer.Risk, 1e-6f, "지금 이 상태: 제안의 위험은 1(위 주석)");
    }

    [Test]
    public void 팀1은_공격_방향이_반대라도_같은_제안이_나온다()
    {
        // 팀0 시나리오를 X만 뒤집은 팀1(−X)
        var opp = new List<TargetInfo> { new TargetInfo(100, -50f, 0f), new TargetInfo(101, -30f, -28f), new TargetInfo(102, -30f, 28f) };
        var runner = new RunCandidate(1, -15f, 0f, 0f, 0f, WingerSpeed, RunRules.StyleBehind);
        RunOffer offer = RunRules.FindOffer(-10f, 0f, -1, Average, Allow, false, opp, new[] { runner }, new List<RunOffer>(), 99, TestThreat.Real, 1, 15f, 1);
        RunOffer mirrored = Find(OpenDefense(), Winger());
        Assert.AreEqual(mirrored.RunnerId, offer.RunnerId);
        Assert.AreEqual(-mirrored.X, offer.X, 1e-3f);
        Assert.AreEqual(mirrored.Z, offer.Z, 1e-3f);
    }

    [Test]
    public void 내려와_받기는_압박받는_운반자일_때만_패서_앞_지원_거리에_선다()
    {
        var shortRunner = new RunCandidate(3, 12f, 5f, 0f, 0f, 7f, RunRules.StyleShort);
        RunOffer unpressed = RunRules.FindOffer(10f, 0f, +1, Average, Allow, false, OpenDefense(), new[] { shortRunner }, new List<RunOffer>(), 99, TestThreat.Real, 1, 15f, 1);
        Assert.AreEqual(RunOffer.NoRunner, unpressed.RunnerId, "압박이 없으면 내려올 이유가 없다");

        RunOffer pressed = RunRules.FindOffer(10f, 0f, +1, Average, Allow, true, OpenDefense(), new[] { shortRunner }, new List<RunOffer>(), 99, TestThreat.Real, 1, 15f, 1);
        Assert.AreEqual(3, pressed.RunnerId);
        Assert.AreEqual(10f + MatchTuning.RunSupportDistance, pressed.X, 1e-4f);
        Assert.AreEqual(5f, pressed.Z, 1e-4f, "자기 레인 유지");
    }

    [Test]
    public void 이미_있는_제안은_달리면_가능한_동안_유지되고_위험은_지금_값으로_갱신된다()
    {
        var offer = new RunOffer(1, 45f, 0f, 1f);
        RunOffer kept = RunRules.Reevaluate(offer, 10f, 0f, Average, Allow, OpenDefense(), Winger(velX: WingerSpeed));
        Assert.AreEqual(1, kept.RunnerId);
        Assert.LessOrEqual(kept.Risk, Allow, "러너가 붙었으니 위험이 내려갔다");

        List<TargetInfo> blocked = OpenDefense();
        blocked.Add(new TargetInfo(103, 28f, 0.5f));
        Assert.AreEqual(RunOffer.NoRunner, RunRules.Reevaluate(offer, 10f, 0f, Average, Allow, blocked, Winger()).RunnerId, "길이 막히면 버린다");
    }

    [Test]
    public void 방향_후보는_정면부터_좌우_번갈아_바깥으로_간다()
    {
        Assert.AreEqual(0f, RunRules.DirectionAngle(0), 1e-5f);
        Assert.AreEqual(MatchTuning.RunAngleDeg, RunRules.DirectionAngle(1), 1e-4f, "방향 3개면 ±RunAngleDeg");
        Assert.AreEqual(-MatchTuning.RunAngleDeg, RunRules.DirectionAngle(2), 1e-4f);
    }

    [Test]
    public void 목표점은_필드_안으로_잘리고_팀1은_X가_반대다()
    {
        (float x, float z) far = RunRules.SpotAt(40f, 30f, +1, 30f, 40f);
        Assert.LessOrEqual(far.x, Game.Core.Placement.FieldBounds.HalfLength - Game.Core.Placement.FieldBounds.EdgeMargin + 1e-4f);
        Assert.LessOrEqual(far.z, Game.Core.Placement.FieldBounds.HalfWidth - MatchTuning.TouchlineMargin + 1e-4f);

        (float x, float z) straight = RunRules.SpotAt(0f, 0f, +1, 10f, 0f);
        Assert.AreEqual(10f, straight.x, 1e-4f);
        Assert.AreEqual(0f, straight.z, 1e-4f);
        Assert.AreEqual(-10f, RunRules.SpotAt(0f, 0f, -1, 10f, 0f).x, 1e-4f);
    }
}
