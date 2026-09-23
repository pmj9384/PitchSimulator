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

    // 찬 뒤 자기 공을 쫓아가는 패서(실제 트리 ⑩ 자유 공 분기와 같은 움직임)
    private sealed class PassThenChase : BehaviorNode
    {
        private readonly int passer; private readonly int receiver; private bool done;
        public PassThenChase(int passer, int receiver) { this.passer = passer; this.receiver = receiver; }
        public override NodeState Tick(IPlayerContext ctx)
        {
            if (ctx.PlayerId != passer) { return NodeState.Success; }
            if (!done && ctx.OwnsBall) { ctx.Pass(receiver); done = true; return NodeState.Success; }
            ctx.MoveToward(ctx.BallX, ctx.BallZ);
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
        Assert.Greater(b.PassTargetX, 15f, "리드 패스: 리시버(15)보다 앞쪽 점");
        Assert.LessOrEqual(b.PassTargetX, 15f + MatchTuning.PassLeadMax, "리드 상한");
        Assert.IsFalse(a.IsPassTarget);

        for (int i = 0; i < 100 && sim.Ball.Phase != BallPhase.Owned; i++) { sim.Tick(Dt); }
        Assert.AreEqual(BallPhase.Owned, sim.Ball.Phase);
        Assert.AreEqual(1, sim.Ball.OwnerId, "리시버가 받음");
        Assert.AreEqual(0, sim.InterceptCount);
        Assert.IsFalse(b.IsPassTarget, "받고 나면 패스 대상 해제");
    }

    [Test]
    public void 짧은_패스는_살살_긴_패스는_세게_차서_둘_다_리시버에게_닿는다()
    {
        // 09-23 Play: 초속이 거리 무관 고정이라 5m 패스는 15m/s로 날아가 받는 순간 0으로 꺾이고, 먼 패스는 못 미쳐 멈췄다
        float Kick(float receiverX, out int ownerId)
        {
            var sim = new MatchSimulation(() => 0.5f, new PassOnce(0, 1));
            sim.AddPlayer(new PlayerState(0, 0, Mid(), 0f, 0f));
            sim.AddPlayer(new PlayerState(1, 0, Mid(), receiverX, 0f));
            sim.Kickoff();
            sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 0f);
            sim.Tick(Dt);
            float v0 = (float)System.Math.Sqrt(sim.Ball.VelX * sim.Ball.VelX + sim.Ball.VelZ * sim.Ball.VelZ);
            for (int i = 0; i < 300 && sim.Ball.Phase != BallPhase.Owned; i++) { sim.Tick(Dt); }
            ownerId = sim.Ball.OwnerId;
            return v0;
        }

        float shortKick = Kick(5f, out int shortOwner);
        float longKick = Kick(35f, out int longOwner);
        Assert.Less(shortKick, 12f, "5m(+리드)는 12m/s 아래");
        Assert.Greater(longKick, shortKick + 5f, "35m(+리드)는 확실히 세게");
        Assert.AreEqual(1, shortOwner, "짧은 패스를 리시버가 받음");
        Assert.AreEqual(1, longOwner, "긴 패스도 리시버가 받음(못 미쳐 멈추지 않는다)");
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
    public void 찬_선수가_자기_공을_쫓아가도_공이_앞서면_못_잡는다()
    {
        // 09-21 Play: 릴리스만 있으면 3틱째(공 0.9m, 패서 0.42m 따라옴)에 패서가 도로 잡아 0.15초마다 반복됐다
        var sim = new MatchSimulation(() => 0.5f, new PassThenChase(0, 1));
        sim.AddPlayer(new PlayerState(0, 0, Mid(), 0f, 0f));
        sim.AddPlayer(new PlayerState(1, 0, Mid(), 15f, 0f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 0f);

        for (int i = 0; i < 150 && !(sim.Ball.Phase == BallPhase.Owned && sim.Ball.OwnerId != 0); i++) { sim.Tick(Dt); }
        Assert.AreEqual(BallPhase.Owned, sim.Ball.Phase, "150틱 안에 누군가 받는다");
        Assert.AreEqual(1, sim.Ball.OwnerId, "쫓아간 패서가 아니라 리시버가 받는다");
        Assert.AreEqual(0, sim.InterceptCount);
    }

    [Test]
    public void 리드_목표에_못_미쳐_멈춘_패스는_끝난_것이라_리시버가_공으로_간다()
    {
        // 09-23 Play 잠금. 리시버 62m 앞(리드 목표는 그보다 앞): 초속 상한 22는 감속 4로 60.5m에서 멈춰 목표에 못 미친다(09-23 밤 킥 속도
        // 역산 뒤엔 상한을 넘는 거리로만 재현된다). 실전 트리로: 리시버가 목표점이 아니라 공을 잡아야 한다
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.Build());
        PlayerStats slow = Mid(); slow.Speed = 30;   // 리시버가 느려 공보다 먼저 목표에 못 감
        PlayerState passer = sim.AddPlayer(new PlayerState(0, 0, Mid(), -30f, 0f));
        PlayerState receiver = sim.AddPlayer(new PlayerState(1, 0, slow, 32f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, Mid(), 45f, 20f));   // 상대 1명(경로 밖)
        sim.SetTactics(0, new TeamTactics { PassRisk = new[] { 1, 1, 1 }, PressStart = new[] { 1, 1, 1 }, Width = new[] { 1, 1, 1 }, Tempo = new[] { 1, 1, 1 }, PassStyle = new[] { 2, 2, 2 }, Mentality = 1 });
        sim.SetTactics(1, new TeamTactics { PassRisk = new[] { 1, 1, 1 }, PressStart = new[] { 0, 0, 0 }, Width = new[] { 1, 1, 1 }, Tempo = new[] { 1, 1, 1 }, PassStyle = new[] { 1, 1, 1 }, Mentality = 1 });
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, passer.PlayerId, passer.X, passer.Z);

        int ticks = 0;
        while (ticks < 600 && !(sim.Ball.Phase == BallPhase.Owned && sim.Ball.OwnerId == receiver.PlayerId)) { sim.Tick(Dt); ticks++; }
        Assert.AreEqual(receiver.PlayerId, sim.Ball.OwnerId, $"12초 안에 리시버가 멈춘 공을 잡는다(공 {sim.Ball.Phase} ({sim.Ball.X:0.0},{sim.Ball.Z:0.0}), 리시버 ({receiver.X:0.0},{receiver.Z:0.0}))");
    }

    [Test]
    public void 발치에_붙은_상대는_등_뒤로_찬_패스를_그_자리에서_못_잡는다()
    {
        // 09-21 Play 잠금: 압박 상대가 소유자 발치(0.5m)에 서 있고 소유자는 반대쪽 아군에게 찬다.
        // 킥 릴리스 전엔 아무도 못 잡으므로 공은 발치를 떠나고, 상대는 경로 밖이라 끝까지 못 잡는다
        var sim = new MatchSimulation(() => 0.5f, new PassOnce(0, 1));
        sim.AddPlayer(new PlayerState(0, 0, Mid(), 0f, 0f));
        sim.AddPlayer(new PlayerState(1, 0, Mid(), -12f, 0f));    // 뒤쪽 아군(리시버)
        sim.AddPlayer(new PlayerState(2, 1, Mid(), 0.5f, 0f));    // 발치에 붙은 상대, 패스 축의 반대편
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 0f);

        sim.Tick(Dt);   // 킥
        sim.Tick(Dt);   // 공 0.3m 이동. 릴리스 전
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase, "발치 상대가 그 자리에서 잡으면 안 된다");

        for (int i = 0; i < 100 && sim.Ball.Phase != BallPhase.Owned; i++) { sim.Tick(Dt); }
        Assert.AreEqual(1, sim.Ball.OwnerId, "뒤쪽 아군이 받음");
        Assert.AreEqual(0, sim.InterceptCount);
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
    public void 비행_중_패스는_찬_팀_소유이고_가로채이면_턴오버로_역압박이_켜진다()
    {
        var sim = new MatchSimulation(() => 0.5f, new PassOnce(0, 1));
        sim.AddPlayer(new PlayerState(0, 0, Mid(), 0f, 0f));
        sim.AddPlayer(new PlayerState(1, 0, Mid(), 20f, 0f));
        sim.AddPlayer(new PlayerState(3, 0, Mid(), -10f, 5f));    // 뒤에 남은 아군 2명: 역압박 문턱(적극 = 2)을 채운다
        sim.AddPlayer(new PlayerState(4, 0, Mid(), -20f, -5f));
        sim.AddPlayer(new PlayerState(2, 1, Mid(), 10f, 0.3f));   // 경로 위 상대
        sim.SetTactics(0, new TeamTactics { CounterPress = 2, PassRisk = new[] { 1, 1, 1 }, PressStart = new[] { 1, 1, 1 }, Width = new[] { 1, 1, 1 }, Tempo = new[] { 1, 1, 1 }, PassStyle = new[] { 1, 1, 1 } });
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 0f);

        sim.Tick(Dt);
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase);
        Assert.AreEqual(0, sim.OwnerTeam(), "비행 중엔 찬 팀(0)의 공. -1이면 22명이 자유 공으로 본다");
        Assert.IsFalse(sim.IsCounterPressing(0), "아직 안 뺏김");

        for (int i = 0; i < 100 && sim.Ball.Phase != BallPhase.Owned; i++) { sim.Tick(Dt); }
        Assert.AreEqual(2, sim.Ball.OwnerId, "상대가 가로챔");
        Assert.AreEqual(1, sim.OwnerTeam());
        Assert.IsTrue(sim.IsCounterPressing(0), "가로채기 = 팀 전환이라 찬 팀의 역압박 창이 열린다");
        Assert.IsFalse(sim.IsCounterPressing(1));
    }

    [Test]
    public void 압박_거리_안_여럿이어도_팀에서_공에_가장_가까운_1명만_달려들고_나머지는_자리를_지킨다()
    {
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.Build());
        PlayerStats presser = Mid(); presser.PressRange = 20f;
        sim.AddPlayer(new PlayerState(0, 1, Mid(), 0f, 0f));                            // 상대 소유자
        PlayerState first = sim.AddPlayer(new PlayerState(1, 0, presser, -5f, 0f));     // 5m: 첫 압박자
        PlayerState second = sim.AddPlayer(new PlayerState(2, 0, presser, -10f, 0f));   // 10m: 압박 거리 안이지만 순위 2
        PlayerState third = sim.AddPlayer(new PlayerState(3, 0, presser, -15f, 0f));    // 15m: 순위 3
        sim.SetTactics(0, new TeamTactics { PassRisk = new[] { 1, 1, 1 }, PressStart = new[] { 1, 1, 1 }, Width = new[] { 1, 1, 1 }, Tempo = new[] { 1, 1, 1 }, PassStyle = new[] { 1, 1, 1 }, Mentality = 1 });
        sim.SetTactics(1, new TeamTactics { PassRisk = new[] { 1, 1, 1 }, PressStart = new[] { 1, 1, 1 }, Width = new[] { 1, 1, 1 }, Tempo = new[] { 1, 1, 1 }, PassStyle = new[] { 1, 1, 1 }, Mentality = 1 });
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 0f);

        for (int i = 0; i < 10; i++) { sim.Tick(Dt); }

        Assert.Greater(first.X, -4.5f, "첫 압박자는 공으로 달린다");
        Assert.AreEqual(-10f, second.X, 0.5f, "두 번째는 수비 자리(스폰 자리 + 슬라이드)에 남는다");
        Assert.AreEqual(-15f, third.X, 0.5f, "세 번째도");
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
        // 실전 트리 ①(GK 배급)로. 섞어(1)면 첫 배급은 짧게(CB), GK가 다시 잡으면 길게(ST). 09-23 R2 전엔 섞어가 짧게로 고정이었다
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.Build());
        PlayerStats gk = Mid(); gk.RoleId = "GK";
        PlayerState keeper = sim.AddPlayer(new PlayerState(0, 0, gk, -48f, 0f));
        PlayerState cb = sim.AddPlayer(new PlayerState(1, 0, Mid(), -36f, 7f));
        PlayerState st = sim.AddPlayer(new PlayerState(2, 0, Mid(), -8f, 0f));
        sim.SetTactics(0, new TeamTactics { GkDistribution = 1, PassRisk = new[] { 1, 1, 1 }, PressStart = new[] { 1, 1, 1 }, Width = new[] { 1, 1, 1 }, Tempo = new[] { 1, 1, 1 }, PassStyle = new[] { 1, 1, 1 } });
        sim.Kickoff();

        sim.Ball = BallRules.Own(sim.Ball, keeper.PlayerId, keeper.X, keeper.Z);
        sim.Tick(Dt);   // 배급 킥
        sim.Tick(Dt);   // 다음 스냅샷에 리시버 표시
        Assert.IsTrue(cb.IsPassTarget, "섞어: 첫 배급은 짧게(가까운 CB)");

        sim.Ball = BallRules.Own(sim.Ball, keeper.PlayerId, keeper.X, keeper.Z);
        sim.Tick(Dt);
        sim.Tick(Dt);
        Assert.IsTrue(st.IsPassTarget, "섞어: 다음 배급은 길게(가장 앞선 ST)");
    }
}
