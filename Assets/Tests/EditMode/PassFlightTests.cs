using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 시뮬 실행부(09-18 #16): ①Pass 의도 → 공 비행 → 리시버가 잡음(IsPassTarget 스냅샷) ②비행 중 상대가 가로챔
// ③슛 조준이 골문 밖이면 Missed ④볼 끌기가 킥을 대기시킴 ⑤GK 배급 대상
public class PassFlightTests
{
    private const float Dt = 0.02f;

    private static PlayerStats Mid(float holdUp = 0f)
    {
        return new PlayerStats { RoleId = "CM", VariantId = "cm_central", Speed = 50, Stamina = 50, Pass = 50, Shot = 50, Tackle = 50, Positioning = 50, ShotBias = 0.3f, PassLength = 15f, PressRange = 8f, HoldUp = holdUp };
    }

    // 트리 대신 의도를 직접 주입하는 가짜 트리: 첫 틱에 지정 선수에게 Pass 의도
    private sealed class PassOnce : BehaviorNode
    {
        private readonly int passer; private readonly int receiver; private bool done;
        public PassOnce(int passer, int receiver) { this.passer = passer; this.receiver = receiver; }
        public override NodeState Tick(IPlayerContext ctx)
        {
            if (!done && ctx.PlayerId == passer && ctx.OwnsBall) { ctx.Pass(receiver); done = true; }
            return NodeState.Success;
        }
    }

    private sealed class ShootOnce : BehaviorNode
    {
        private bool done;
        public override NodeState Tick(IPlayerContext ctx)
        {
            if (!done && ctx.OwnsBall) { ctx.Shoot(); done = true; }
            return NodeState.Success;
        }
    }

    [Test]
    public void 패스는_리시버에게_날아가고_리시버가_잡는다()
    {
        var sim = new MatchSimulation(() => 0.5f, new PassOnce(passer: 0, receiver: 1));
        PlayerState a = sim.AddPlayer(new PlayerState(0, 0, Mid(), 0f, 0f));
        PlayerState b = sim.AddPlayer(new PlayerState(1, 0, Mid(), 15f, 0f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 0f);

        sim.Tick(Dt);   // 스냅샷 → 의도 → 실행(Pass): 공 비행 시작
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase);
        Assert.AreEqual(1, sim.PassCount);

        sim.Tick(Dt);   // 다음 틱 스냅샷에서 리시버가 IsPassTarget
        Assert.IsTrue(b.IsPassTarget);
        Assert.AreEqual(15f, b.PassTargetX, 1e-4f);
        Assert.IsFalse(a.IsPassTarget);

        for (int i = 0; i < 100 && sim.Ball.Phase != BallPhase.Owned; i++) { sim.Tick(Dt); }
        Assert.AreEqual(BallPhase.Owned, sim.Ball.Phase);
        Assert.AreEqual(1, sim.Ball.OwnerId, "리시버가 받음");
        Assert.AreEqual(0, sim.InterceptCount);
        Assert.IsFalse(b.IsPassTarget, "받고 나면 패스 대상 해제");
    }

    [Test]
    public void 찬_선수는_공이_발치를_벗어나기_전엔_자기_공을_도로_잡지_않는다()
    {
        var sim = new MatchSimulation(() => 0.5f, new PassOnce(0, 1));
        sim.AddPlayer(new PlayerState(0, 0, Mid(), 0f, 0f));
        sim.AddPlayer(new PlayerState(1, 0, Mid(), 15f, 0f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 0f);

        sim.Tick(Dt);   // 킥
        sim.Tick(Dt);   // 공은 0.3m 이동, 아직 패서 반경 안
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase, "패서가 도로 잡으면 안 된다");
    }

    [Test]
    public void 경로_위_상대가_비행_중인_패스를_가로챈다()
    {
        var sim = new MatchSimulation(() => 0.5f, new PassOnce(0, 1));
        sim.AddPlayer(new PlayerState(0, 0, Mid(), 0f, 0f));
        sim.AddPlayer(new PlayerState(1, 0, Mid(), 20f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, Mid(), 10f, 0.3f));   // 경로 위, 잡기 반경 안
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 0f);

        for (int i = 0; i < 100 && !(sim.Ball.Phase == BallPhase.Owned && sim.Ball.OwnerId != 0); i++) { sim.Tick(Dt); }
        Assert.AreEqual(2, sim.Ball.OwnerId, "상대가 가로챔");
        Assert.AreEqual(1, sim.InterceptCount);
    }

    [Test]
    public void 조준이_골문_밖이면_GK가_있어도_빗나감이다()
    {
        // roll 0.0 → 조준 Z = -반폭. shot 30이면 반폭 4.95 > 3.66이라 골문 밖
        var sim = new MatchSimulation(() => 0f, new ShootOnce());
        PlayerStats weak = Mid(); weak.Shot = 30;
        sim.AddPlayer(new PlayerState(0, 0, weak, 40f, 0f));
        PlayerStats gk = Mid(); gk.RoleId = "GK"; gk.Reflexes = 50; gk.Diving = 50; gk.Handling = 50;
        sim.AddPlayer(new PlayerState(1, 1, gk, 50f, 0f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 40f, 0f);
        var reports = new List<ShotReport>();
        sim.ShotResolved += r => reports.Add(r);

        for (int i = 0; i < 200 && reports.Count == 0; i++) { sim.Tick(Dt); }
        Assert.AreEqual(1, reports.Count);
        Assert.AreEqual(ShotOutcome.Missed, reports[0].Outcome);
    }

    [Test]
    public void 볼_끌기는_킥_의도를_대기_틱만큼_미룬다()
    {
        var sim = new MatchSimulation(() => 0.5f, new PassOnce(0, 1));
        sim.AddPlayer(new PlayerState(0, 0, Mid(holdUp: 0.5f), 0f, 0f));   // 0.5초 = 25틱
        sim.AddPlayer(new PlayerState(1, 0, Mid(), 15f, 0f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 0f);

        sim.Tick(Dt);   // 소유 감지 → 대기 25틱 시작. 이 틱의 Pass 의도는 무시
        Assert.AreEqual(BallPhase.Owned, sim.Ball.Phase, "아직 안 참");
        Assert.AreEqual(0, sim.PassCount);
    }

    [Test]
    public void GK_배급은_팀_설정을_따르고_섞어는_교대한다()
    {
        var sim = new MatchSimulation(() => 0.5f, new ShootOnce());
        PlayerStats gk = Mid(); gk.RoleId = "GK";
        PlayerState keeper = sim.AddPlayer(new PlayerState(0, 0, gk, -48f, 0f));
        sim.AddPlayer(new PlayerState(1, 0, Mid(), -36f, 7f));
        sim.AddPlayer(new PlayerState(2, 0, Mid(), -8f, 0f));
        sim.SetTactics(0, new TeamTactics { GkDistribution = 1 });

        Assert.AreEqual(1, sim.KeeperDistributionTarget(keeper), "섞어: 짧게");
        Assert.AreEqual(2, sim.KeeperDistributionTarget(keeper), "섞어: 길게");
        sim.SetTactics(0, new TeamTactics { GkDistribution = 2 });
        Assert.AreEqual(2, sim.KeeperDistributionTarget(keeper), "길게");
    }
}
