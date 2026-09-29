using Game.Core.Data;
using Game.Core.Match;
using NUnit.Framework;

// 체력(09-29 전술 상성 ⑥). 전력 행동만 깎고 나머지 틱은 반감기로 회복, 문턱 아래에서만 속도가 준다(최저 0.9)
public class FatigueRulesTests
{
    [Test]
    public void 스태미나_스탯이_높을수록_덜_줄어든다()
    {
        Assert.AreEqual(1f, FatigueRules.DrainScale(50), 1e-5f);
        Assert.AreEqual(0.5f, FatigueRules.DrainScale(100), 1e-5f);
        Assert.AreEqual(1.5f, FatigueRules.DrainScale(0), 1e-5f);
    }

    [Test]
    public void 전력_행동은_단기_체력과_장기_상한을_깎고_체력은_상한을_넘지_않는다()
    {
        (float stamina, float cap) r = FatigueRules.Exert(1f, 1f, 50, 0.1f, 0.01f, 0.5f);
        Assert.AreEqual(0.9f, r.stamina, 1e-5f);
        Assert.AreEqual(0.99f, r.cap, 1e-5f);

        (float stamina, float cap) floor = FatigueRules.Exert(0.52f, 0.5f, 50, 0.0f, 0.1f, 0.5f);
        Assert.AreEqual(0.5f, floor.cap, 1e-5f, "상한은 바닥에서 멈춘다");
        Assert.LessOrEqual(floor.stamina, floor.cap, "체력은 상한 이하");
    }

    [Test]
    public void 회복은_반감기만큼_지나면_상한까지_남은_거리가_절반이_된다()
    {
        float s = 0.2f;
        for (int i = 0; i < 95; i++) { s = FatigueRules.Recover(s, 0.8f, 95f); }
        Assert.AreEqual(0.5f, s, 1e-3f, "0.2 → 0.8의 절반 지점");
        Assert.AreEqual(0.8f, FatigueRules.Recover(0.9f, 0.8f, 95f), 1e-5f, "상한 위면 상한으로");
    }

    [Test]
    public void 속도_배율은_문턱_아래에서만_최저까지_선형으로_준다()
    {
        Assert.AreEqual(1f, FatigueRules.Effort(0.3f, 0.3f, 0.9f), 1e-5f);
        Assert.AreEqual(0.95f, FatigueRules.Effort(0.15f, 0.3f, 0.9f), 1e-5f);
        Assert.AreEqual(0.9f, FatigueRules.Effort(0f, 0.3f, 0.9f), 1e-5f);
    }

    [Test]
    public void 체력이_바닥이면_시뮬에서_최고_속도의_최저_배율로_움직인다()
    {
        // speed 50 = 7m/s. 체력 0이면 7 × 0.9 = 6.3m/s → 한 틱(0.02초) 0.126m
        var stats = new PlayerStats { RoleId = "CM", VariantId = "cm_test", Speed = 50, Stamina = 50, PressRange = 8f };
        var sim = new MatchSimulation(() => 0.5f, new MoveRight());
        PlayerState p = sim.AddPlayer(new PlayerState(0, 0, stats, 0f, 10f));
        sim.Ball = BallState.FreeAt(40f, -30f);
        p.Stamina = 0f;
        p.StaminaCap = 0.5f;
        sim.Tick(0.02f);
        Assert.AreEqual(7f * MatchTuning.FatigueEffortMin * 0.02f, p.X, 1e-4f);
    }

    [Test]
    public void 시뮬에서_공을_모는_선수는_체력이_줄고_자리로_가는_선수는_회복한다()
    {
        // 전력 행동(공 몰기)만 깎는다. 같은 트리로 움직여도 공이 없는 선수는 회복 쪽
        var stats = new PlayerStats { RoleId = "CM", VariantId = "cm_test", Speed = 50, Stamina = 50, PressRange = 8f };
        var sim = new MatchSimulation(() => 0.5f, new MoveRight());
        PlayerState carrier = sim.AddPlayer(new PlayerState(0, 0, stats, 0f, 10f));
        PlayerState mate = sim.AddPlayer(new PlayerState(1, 0, stats, 0f, -10f));
        sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 10f);
        mate.Stamina = 0.5f;
        for (int i = 0; i < 10; i++) { sim.Tick(0.02f); }
        Assert.Less(carrier.Stamina, 1f, "공 몰기 = 전력 행동");
        Assert.Less(carrier.StaminaCap, 1f, "장기 상한도 조금 내려간다");
        Assert.Greater(mate.Stamina, 0.5f, "자리 이동 = 회복");
    }

    [Test]
    public void 하프타임에는_단기_체력이_상한까지_찬다()
    {
        var stats = new PlayerStats { RoleId = "CM", VariantId = "cm_test", Speed = 50, Stamina = 50, PressRange = 8f };
        var sim = new MatchSimulation(() => 0.5f, new PassFlightTestsHelper.NoOp());
        PlayerState p = sim.AddPlayer(new PlayerState(0, 0, stats, -10f, 0f));
        sim.AddPlayer(new PlayerState(1, 1, stats, 10f, 0f));
        sim.KickoffBy(0);
        int halfTime = MatchClock.HalfTimeTick(sim.Added);
        while (sim.TickCount < halfTime - 1) { sim.Tick(0.02f); }
        p.Stamina = 0.1f;
        p.StaminaCap = 0.6f;
        sim.Tick(0.02f);
        Assert.AreEqual(halfTime, sim.TickCount);
        Assert.AreEqual(0.6f, p.Stamina, 1e-5f, "하프타임 휴식 = 상한까지");
    }

    // 오른쪽으로만 가는 가짜 트리(전력 행동이 아니라 체력은 회복 쪽)
    private sealed class MoveRight : Game.Core.AI.BehaviorNode
    {
        public override Game.Core.AI.NodeState Tick(Game.Core.AI.IPlayerContext ctx)
        {
            ctx.MoveToward(ctx.X + 10f, ctx.Z);
            return Game.Core.AI.NodeState.Success;
        }
    }
}
