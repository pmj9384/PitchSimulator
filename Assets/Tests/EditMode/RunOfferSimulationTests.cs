using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 사전 런의 시뮬 쪽(11-03 ⑤, 설계초안-침투-스루 3절 C 실패 시나리오). 실제 트리로 돌린다:
// ①볼 끌기(HoldUp)가 창을 먹지 않는다 ②패스 착지 = 런 목표점 ③킥 순간 러너는 온사이드 ④비행 중엔 제안이 없다 ⑤동시 런 상한 ⑥같은 시드 같은 결과
public class RunOfferSimulationTests
{
    private const float Dt = 0.02f;

    private static PlayerStats Carrier(float holdUp)
    {
        return new PlayerStats { RoleId = "CM", VariantId = "cm_central", Speed = 50, Stamina = 50, Pass = 50, Shot = 50, Tackle = 50, Positioning = 50, ShotBias = 1f, PassLength = 25f, PressRange = 8f, HoldUp = holdUp, Dribble = 0f };
    }

    private static PlayerStats Winger(int runStyle)
    {
        return new PlayerStats { RoleId = "W", VariantId = "w_winger", Speed = 80, Stamina = 50, Pass = 50, Shot = 50, Tackle = 50, Positioning = 50, ShotBias = 1f, PassLength = 15f, PressRange = 8f, Dribble = 0f, RunStyle = runStyle };
    }

    private static PlayerStats Defender(bool keeper)
    {
        return new PlayerStats { RoleId = keeper ? "GK" : "CB", VariantId = keeper ? "gk_standard" : "cb_centreback", Speed = 40, Stamina = 50, Pass = 40, Shot = 20, Tackle = 80, Positioning = 70, PressRange = 6f, ShotBias = 0.8f, PassLength = 20f, GkRushRadius = keeper ? 12f : 0f, Reflexes = keeper ? 80 : 0 };
    }

    private sealed class Scene
    {
        public MatchSimulation Sim = null!;
        public PlayerState Carrier = null!;
        public PlayerState Runner = null!;
        public readonly List<string> Trace = new List<string>();
        public int KickTick = -1;
        public int KickReceiver = -1;
        public float KickLandingX;
        public float KickLandingZ;
        public float RunnerSpeedAtKick;
        public bool RunnerOffsideAtKick;
        public RunOffer OfferAtKick = RunOffer.None;
        public int FirstOfferTick = -1;
        public bool KickWasRun;

        private PlayerState KickerOf(int playerId)
        {
            foreach (PlayerState p in Sim.Players)
            {
                if (p.PlayerId == playerId) { return p; }
            }
            throw new System.InvalidOperationException("킥한 선수가 명부에 없다: " + playerId);
        }

        public void OnKicked(KickReport report)
        {
            if (report.IsShot || KickTick >= 0) { return; }
            PlayerState passer = KickerOf(report.KickerId);
            KickTick = Sim.TickCount;
            KickReceiver = passer.PassReceiverId;
            KickLandingX = passer.PassLandingX;
            KickLandingZ = passer.PassLandingZ;
            KickWasRun = passer.PassIsRun;
            RunnerSpeedAtKick = (float)System.Math.Sqrt(Runner.VelX * Runner.VelX + Runner.VelZ * Runner.VelZ);
            float line = OffsideRules.OnsideLine(passer.Opponents, passer.AttackSign, passer.X);
            RunnerOffsideAtKick = OffsideRules.IsOffsidePosition(Runner.X, passer.AttackSign, line);
            foreach (RunOffer offer in passer.RunOffers)
            {
                if (offer.RunnerId == Runner.PlayerId) { OfferAtKick = offer; }
            }
        }
    }

    // 팀0: 운반자 CM(20,0), 러너 W(12,10, 뒤공간, 운반자보다 8m 뒤). 팀1: GK(50,0)와 수비 둘(30, ±28). 가운데 길이 비어 있다
    private static Scene Build(float holdUp, int runStyle = RunRules.StyleBehind, float carrierX = 20f)
    {
        var scene = new Scene();
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.Build(), TestThreat.Real);
        var tactics = new TeamTactics { PassRisk = new[] { 1, 1, 1 }, PressStart = new[] { 0, 0, 0 }, Width = new[] { 1, 1, 1 }, Tempo = new[] { 1, 1, 1 }, Mentality = 1, PassStyle = new[] { 1, 1, 1 } };
        sim.SetTactics(0, tactics);
        sim.SetTactics(1, tactics);
        scene.Carrier = sim.AddPlayer(new PlayerState(0, 0, Carrier(holdUp), carrierX, 0f, carrierX, 0f));
        scene.Runner = sim.AddPlayer(new PlayerState(1, 0, Winger(runStyle), 12f, 10f, 12f, 10f));
        sim.AddPlayer(new PlayerState(10, 1, Defender(keeper: true), 50f, 0f, 50f, 0f));
        sim.AddPlayer(new PlayerState(11, 1, Defender(keeper: false), 30f, -28f, 30f, -28f));
        sim.AddPlayer(new PlayerState(12, 1, Defender(keeper: false), 30f, 28f, 30f, 28f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, carrierX, 0f);
        scene.Sim = sim;
        sim.BallKicked += scene.OnKicked;
        return scene;
    }

    private static void RunUntilKick(Scene scene, int maxTicks)
    {
        for (int i = 0; i < maxTicks && scene.KickTick < 0; i++)
        {
            scene.Sim.Tick(Dt);
            if (scene.FirstOfferTick < 0 && scene.Carrier.RunOffers.Count > 0) { scene.FirstOfferTick = scene.Sim.TickCount; }
        }
    }

    [Test]
    public void 볼_끌기가_0점5초여도_러너가_달리는_중에_패스가_나가고_착지는_런_목표점이다()
    {
        Scene scene = Build(holdUp: 0.5f);
        RunUntilKick(scene, 400);

        Assert.GreaterOrEqual(scene.KickTick, 0, "8초 안에 패스가 나간다");
        Assert.AreEqual(1, scene.KickReceiver, "러너에게");
        Assert.GreaterOrEqual(scene.FirstOfferTick, 0, "패스 전에 제안이 먼저 있었다(사전 런)");
        Assert.Less(scene.FirstOfferTick, scene.KickTick, "제안이 킥보다 먼저");
        float secondsAfterOffer = (scene.KickTick - scene.FirstOfferTick) * Dt;
        Assert.LessOrEqual(secondsAfterOffer, 2.5f, "런 시작 뒤 SkillCorner p75(2.1초) 근처에 패스");
        Assert.Greater(scene.RunnerSpeedAtKick, 0.5f * 11.2f, "킥 순간 러너는 질주 중");
        Assert.AreEqual(scene.OfferAtKick.X, scene.KickLandingX, 1e-3f, "착지점 = 러너가 달리던 목표점(같은 함수)");
        Assert.AreEqual(scene.OfferAtKick.Z, scene.KickLandingZ, 1e-3f);
    }

    [Test]
    public void 런_제안_러너에게_찬_패스는_수신자_스냅샷이_런_패스이고_감속_없는_이동이다()
    {
        Scene scene = Build(holdUp: 0.5f);
        RunUntilKick(scene, 400);
        Assert.GreaterOrEqual(scene.KickTick, 0);
        Assert.AreEqual(1, scene.KickReceiver);
        Assert.IsTrue(scene.KickWasRun, "트리가 PassRun으로 찼다(PassChoice.IsRun)");

        scene.Sim.Tick(Dt);   // 다음 틱 스냅샷에 수신자 표시 + 트리가 ⑥에서 수신 이동을 고른다
        Assert.IsTrue(scene.Runner.IsPassTarget);
        Assert.IsTrue(scene.Runner.IsRunPass, "수신자 스냅샷의 IsRunPass");
        Assert.AreEqual(MoveMode.Run, scene.Runner.MoveMode, "Seek(감속 없음). Arrive가 아니다");
    }

    [Test]
    public void 킥_순간_러너는_온사이드다_선_너머는_킥_뒤_마중이_맡는다()
    {
        Scene scene = Build(holdUp: 0.5f);
        RunUntilKick(scene, 400);
        Assert.GreaterOrEqual(scene.KickTick, 0);
        Assert.IsFalse(scene.RunnerOffsideAtKick);
        Assert.GreaterOrEqual(scene.KickLandingX, 30f - MatchTuning.RunBehindMargin, "착지는 수비 라인(30) 근처·뒤(RunBehindMargin 안)");
    }

    [Test]
    public void 패스가_나가면_비행_중에는_제안이_없다()
    {
        Scene scene = Build(holdUp: 0.5f);
        RunUntilKick(scene, 400);
        Assert.GreaterOrEqual(scene.KickTick, 0);
        scene.Sim.Tick(Dt);
        Assert.AreEqual(BallPhase.Flight, scene.Sim.Ball.Phase);
        Assert.AreEqual(0, scene.Carrier.RunOffers.Count, "비행 중엔 제안이 없다(⑥ 마중이 이어받는다)");
        Assert.AreEqual(0, scene.Runner.RunOffers.Count);
    }

    private static void RunUntilOffer(Scene scene, int maxTicks)
    {
        for (int i = 0; i < maxTicks && scene.Carrier.RunOffers.Count == 0; i++) { scene.Sim.Tick(Dt); }
        Assert.AreEqual(1, scene.Carrier.RunOffers.Count, "제안이 생겼다");
        Assert.AreEqual(BallPhase.Owned, scene.Sim.Ball.Phase);
    }

    [Test]
    public void 운반자가_바뀌면_옛_운반자의_제안은_남지_않는다()
    {
        Scene scene = Build(holdUp: 0.5f);
        RunUntilOffer(scene, 400);

        // 러너가 공을 넘겨받아 운반자가 된다. 새 운반자 자신은 제안의 대상이 될 수 없고, 옛 제안은 버려진다
        scene.Sim.Ball = BallRules.Own(scene.Sim.Ball, scene.Runner.PlayerId, scene.Runner.X, scene.Runner.Z);
        scene.Sim.Tick(Dt);
        Assert.AreEqual(0, scene.Runner.RunOffers.Count, "운반자가 바뀐 틱에 옛 제안이 사라진다");
    }

    [Test]
    public void 공이_자유_공이_되면_제안이_사라진다()
    {
        Scene scene = Build(holdUp: 0.5f);
        RunUntilOffer(scene, 400);

        scene.Sim.Ball = BallState.FreeAt(-40f, 25f);   // 아무도 못 잡는 먼 자리
        scene.Sim.Tick(Dt);
        Assert.AreEqual(0, scene.Carrier.RunOffers.Count);
    }

    // 킥오프·하프타임이 제안을 직접 비우지 않는다: 둘 다 공을 Free로 두거나(Kickoff) 킥오프 선수에게 쥐여(KickoffBy)
    // 다음 틱 UpdateRunOffersOf의 가드(Phase != Owned, kickoffTakerId)가 비운다. 그 불변식을 여기서 잠근다
    [Test]
    public void 킥오프를_하면_다음_틱에_제안이_비고_날아오던_런_패스_표시도_꺼진다()
    {
        Scene scene = Build(holdUp: 0.5f);
        RunUntilOffer(scene, 400);
        scene.Sim.Kickoff();
        scene.Sim.Tick(Dt);
        Assert.AreEqual(0, scene.Carrier.RunOffers.Count, "공이 Free로 돌아가 제안이 없다");

        Scene flight = Build(holdUp: 0.5f);
        RunUntilKick(flight, 400);
        Assert.IsTrue(flight.KickWasRun);
        flight.Sim.Kickoff();
        flight.Sim.Tick(Dt);
        Assert.IsFalse(flight.Runner.IsPassTarget);
        Assert.IsFalse(flight.Runner.IsRunPass, "킥오프로 passInFlight가 꺼지면 런 패스 표시도 같이 꺼진다");
    }

    // 킥오프 선수가 운반자인 동안은 제안이 없다(UpdateRunOffersOf의 kickoffTakerId 가드). 운반자(CM)가 중앙에 가깝고 러너(W)가 침투 성향이라
    // 가드가 없으면 첫 주기 틱에 W의 제안이 생긴다. 0 = 시작 킥오프, 1 = 후반 킥오프(하프타임이 팀 0에게 킥오프를 준다)
    [TestCase(0)]
    [TestCase(1)]
    public void 킥오프_선수가_공을_쥔_동안은_시작과_하프타임_모두_제안이_없다(int firstKickoffTeam)
    {
        Scene scene = Build(holdUp: 0.5f, carrierX: 5f);
        scene.Sim.KickoffBy(firstKickoffTeam);
        int halfTime = MatchClock.HalfTimeTick(scene.Sim.Added);
        int observed = 0;
        for (int i = 0; i < halfTime + 120; i++)
        {
            scene.Sim.Tick(Dt);
            if (!scene.Carrier.IsKickoffTaker) { continue; }
            observed++;
            Assert.AreEqual(0, scene.Carrier.RunOffers.Count, $"{scene.Sim.TickCount}틱: 킥오프 선수가 쥔 동안");
        }
        Assert.Greater(observed, 0, "킥오프 선수 구간을 실제로 지났다");
    }

    [Test]
    public void 침투_성향이_없으면_제안이_없고_패스도_러너_목표점으로_가지_않는다()
    {
        Scene scene = Build(holdUp: 0.5f, runStyle: RunRules.StyleNone);
        for (int i = 0; i < 200; i++)
        {
            scene.Sim.Tick(Dt);
            Assert.AreEqual(0, scene.Carrier.RunOffers.Count, "성향 없음 = 지금까지와 같은 자리 규칙");
        }
    }

    [Test]
    public void 동시_런은_팀당_상한_이하이고_제안은_갱신_주기_틱에서만_생긴다()
    {
        Scene scene = Build(holdUp: 0.5f);
        int previous = 0;
        for (int i = 0; i < 400; i++)
        {
            scene.Sim.Tick(Dt);
            int count = scene.Carrier.RunOffers.Count;
            Assert.LessOrEqual(count, MatchTuning.MaxRunners);
            if (count > previous) { Assert.AreEqual(0, scene.Sim.TickCount % MatchTuning.RunPlanInterval, "새 제안은 주기 틱에서만"); }
            previous = count;
        }
    }

    [Test]
    public void 같은_시드는_같은_런과_같은_패스를_낸다()
    {
        Scene a = Build(holdUp: 0.5f);
        Scene b = Build(holdUp: 0.5f);
        RunUntilKick(a, 400);
        RunUntilKick(b, 400);
        Assert.AreEqual(a.KickTick, b.KickTick);
        Assert.AreEqual(a.KickLandingX, b.KickLandingX);
        Assert.AreEqual(a.FirstOfferTick, b.FirstOfferTick);
        Assert.AreEqual(a.Runner.X, b.Runner.X);
    }

    // 판정/실행 계약 장면의 트리: 패서는 공을 쥔 첫 틱에 지정한 점으로 런 패스를 차고, 러너는 찰 때까지 그 점으로 전력으로 달리며(판정의 "달리고 있다면" 가정)
    // 패스가 날아오면 실제 트리의 수신(PlayerTreeBuilder.ReceivePass)과 같이 런 패스는 감속 없이, 아니면 도착점에서 멈춰 받는다
    private sealed class RunPassScript : BehaviorNode
    {
        private readonly float spotX;
        private readonly float spotZ;

        public RunPassScript(float spotX, float spotZ)
        {
            this.spotX = spotX;
            this.spotZ = spotZ;
        }

        public override NodeState Tick(IPlayerContext ctx)
        {
            if (ctx.PlayerId == 0)
            {
                if (ctx.OwnsBall) { ctx.PassRun(1, spotX, spotZ); }
                return NodeState.Success;
            }
            if (!ctx.IsPassTarget) { ctx.RunToward(spotX, spotZ); }
            else if (ctx.IsRunPass) { ctx.RunToward(ctx.PassTargetX, ctx.PassTargetZ); }
            else { ctx.ArriveToward(ctx.PassTargetX, ctx.PassTargetZ); }
            return NodeState.Success;
        }
    }

    private const float ContractSpotX = 35f;
    private static readonly float ContractAverage = MatchTuning.PassAverageSpeed[1];   // 템포 1(균형). 시뮬이 같은 값으로 찬다

    private static RunCandidate ContractRunner(float x, float z)
    {
        float top = MatchRules.SpeedMps(80);
        float dx = ContractSpotX - x;
        float dz = 0f - z;
        float length = (float)System.Math.Sqrt(dx * dx + dz * dz);
        return new RunCandidate(1, x, z, dx / length * top, dz / length * top, top, RunRules.StyleBehind);
    }

    private static bool RunnerOwnsAfterRunPass(float x, float z)
    {
        RunCandidate candidate = ContractRunner(x, z);
        var sim = new MatchSimulation(() => 0.5f, new RunPassScript(ContractSpotX, 0f), TestThreat.Real);
        sim.AddPlayer(new PlayerState(0, 0, Carrier(0f), 0f, 0f));
        PlayerState runner = sim.AddPlayer(new PlayerState(1, 0, Winger(RunRules.StyleBehind), x, z));
        sim.SetTactics(0, new TeamTactics { PassRisk = new[] { 1, 1, 1 }, PressStart = new[] { 0, 0, 0 }, Width = new[] { 1, 1, 1 }, Tempo = new[] { 1, 1, 1 }, Mentality = 1, PassStyle = new[] { 1, 1, 1 } });
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 0f);
        runner.VelX = candidate.VelX;   // Kickoff가 속도를 0으로 되돌리므로 그 뒤에 준다
        runner.VelZ = candidate.VelZ;
        for (int i = 0; i < 250; i++)
        {
            sim.Tick(Dt);
            if (sim.Ball.Phase == BallPhase.Owned && sim.Ball.OwnerId == 1) { return true; }
        }
        return false;
    }

    // 판정(SpotRisks.now, 상대 없음 = 러너 도달만 본다)이 "가능"인 출발 위치에서 같은 패스를 시뮬로 굴리면 러너가 실제로 소유한다
    [Test]
    public void 판정이_도달_가능이라_찬_런_패스는_시뮬에서_러너가_소유한다()
    {
        float allow = 0.5f;
        var none = new List<TargetInfo>();
        int judgedFeasible = 0;
        var failures = new List<string>();
        foreach (float z in new[] { 0f, 4f, 8f })
        {
            for (float x = 14f; x <= 30f; x += 1f)
            {
                var risks = RunRules.SpotRisks(0f, 0f, ContractSpotX, 0f, ContractAverage, allow, none, ContractRunner(x, z));
                if (risks.now > allow) { continue; }
                judgedFeasible++;
                if (!RunnerOwnsAfterRunPass(x, z)) { failures.Add($"({x},{z})"); }
            }
        }
        Assert.GreaterOrEqual(judgedFeasible, 3, "판정이 가능이라 한 출발 위치가 있다(검사가 비어 있지 않다)");
        Assert.IsEmpty(failures, "판정은 가능인데 실행에서 못 받은 위치: " + string.Join(" ", failures));
    }
}
