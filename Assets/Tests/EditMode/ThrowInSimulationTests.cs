using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Placement;
using NUnit.Framework;

// 스로인 실행(IFAB 15조, 11-17 v1 기능 1). 판정(누가·어디서)은 RestartRulesTests, 여기선 시뮬이 그 판정대로 공을 놓고 거르고 기다리는지 본다.
// 팀0 선수가 터치라인 밖으로 차서 마지막 터치를 만들고, 나머지는 실제 트리로 돌린다
public class ThrowInSimulationTests
{
    private const float Dt = 0.02f;
    private const int OwnerId = 1;
    private const int ThrowerId = 4;
    private static readonly float SpotZ = FieldBounds.HalfWidth - MatchTuning.ThrowInInset;

    private static PlayerStats Field()
    {
        return new PlayerStats { RoleId = "CM", VariantId = "cm_test", Speed = 60, Stamina = 60, Pass = 60, Shot = 40, Tackle = 40, Positioning = 50, PushUp = 10f, PressRange = 8f, ShotBias = 0.8f, PassLength = 15f };
    }

    private static PlayerStats Keeper()
    {
        return new PlayerStats { RoleId = "GK", VariantId = "gk_standard", Speed = 30, Stamina = 30, Pass = 30, Shot = 5, Tackle = 10, Positioning = 15, Reflexes = 80, Handling = 60, Diving = 40, PressRange = 3f, ShotBias = 0.9f, PassLength = 25f };
    }

    // 소유자가 처음 한 번 터치라인 밖(z = 45)으로 찬다. 그 뒤로는 실제 트리
    private sealed class KickOutOnce : BehaviorNode
    {
        private bool done;

        public override NodeState Tick(IPlayerContext ctx)
        {
            if (done || ctx.PlayerId != OwnerId || !ctx.OwnsBall) { return NodeState.Failure; }
            ctx.Pass(2, ctx.X, 45f);
            done = true;
            return NodeState.Success;
        }
    }

    private static MatchSimulation Build(out PlayerState thrower)
    {
        return Build(() => 0.5f, out thrower);
    }

    private static MatchSimulation Build(System.Func<float> roll, out PlayerState thrower)
    {
        var tree = new SelectorNode(new KickOutOnce(), PlayerTreeBuilder.Build());
        var sim = new MatchSimulation(roll, tree, TestThreat.Real);
        sim.AddPlayer(new PlayerState(0, 0, Keeper(), -48f, 0f));
        sim.AddPlayer(new PlayerState(OwnerId, 0, Field(), 0f, 30f));
        sim.AddPlayer(new PlayerState(2, 0, Field(), -10f, 20f));
        sim.AddPlayer(new PlayerState(3, 1, Keeper(), 48f, 0f));
        thrower = sim.AddPlayer(new PlayerState(ThrowerId, 1, Field(), 6f, 30f));
        sim.AddPlayer(new PlayerState(5, 1, Field(), 15f, 15f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, OwnerId, 0f, 30f);
        return sim;
    }

    // 공이 스로인 자리에 멈춰 놓일 때까지 돈다
    private static void RunUntilOut(MatchSimulation sim)
    {
        for (int i = 0; i < 200; i++)
        {
            sim.Tick(Dt);
            if (sim.Ball.Phase == BallPhase.Free && System.Math.Abs(sim.Ball.Z - SpotZ) < 1e-4f && sim.Ball.VelZ == 0f) { return; }
        }
        Assert.Fail("공이 터치라인 밖으로 안 나갔다");
    }

    // 던질 때까지 돈다(최대 15초). 던진 킥 보고를 돌려준다
    private static KickReport RunUntilThrown(MatchSimulation sim)
    {
        var kicks = new List<KickReport>();
        sim.BallKicked += kicks.Add;
        for (int i = 0; i < 750 && kicks.Count == 0; i++) { sim.Tick(Dt); }
        sim.BallKicked -= kicks.Add;
        Assert.AreEqual(1, kicks.Count, "15초 안에 던진다");
        return kicks[0];
    }

    private static float DistanceToSpot(PlayerState p, float spotX)
    {
        float dx = p.X - spotX;
        float dz = p.Z - SpotZ;
        return (float)System.Math.Sqrt(dx * dx + dz * dz);
    }

    [Test]
    public void 팀0이_밖으로_찬_공은_팀1이_잡아_2초_들고_15초_안에_던진다()
    {
        MatchSimulation sim = Build(out PlayerState thrower);
        var owners = new List<PossessionReport>();
        var kicks = new List<KickReport>();
        sim.PossessionChanged += owners.Add;
        sim.BallKicked += kicks.Add;

        RunUntilOut(sim);
        float spotX = sim.Ball.X;
        owners.Clear();
        kicks.Clear();

        bool heldChecked = false;
        for (int i = 0; i < 750 && kicks.Count == 0; i++)
        {
            sim.Tick(Dt);
            if (!heldChecked && owners.Count > 0)
            {
                heldChecked = true;
                // 잡은 틱에 대기를 걸고 같은 틱 실행에서 1틱 줄인다. 반올림 1틱까지 여유 2
                Assert.GreaterOrEqual(sim.HoldUpTicksLeft, (int)(MatchTuning.ThrowInSetupSeconds / Dt) - 2, "잡자마자 던지지 않고 2초 든다");
                Assert.IsTrue(thrower.IsThrowInTaker);
            }
        }

        Assert.AreEqual(1, owners[0].NewOwnerTeam, "마지막에 닿은 팀의 상대가 잡는다");
        Assert.AreEqual(ThrowerId, owners[0].NewOwnerId, "재개 팀의 가장 가까운 필드 선수");
        Assert.AreEqual(1, kicks.Count, "15초 안에 던진다");
        Assert.AreEqual(ThrowerId, kicks[0].KickerId);
        Assert.IsFalse(kicks[0].IsShot);
        Assert.Less(System.Math.Abs(thrower.X - spotX), MatchTuning.CaptureRadius + 1f, "나간 자리에서 던진다");
        sim.Tick(Dt);   // 스냅샷은 틱 앞에서 채운다. 던진 틱의 표시는 다음 틱에 갱신된다
        Assert.IsFalse(thrower.IsThrowInTaker, "던지고 나면 표시가 풀린다");
    }

    // 화면이 킥 대신 던지기 모션을 틀 근거(11-17 유저 Play "스로인은 공을 던져야 하는데 발로 차는 느낌")
    [Test]
    public void 스로인_패스만_킥_보고에_던지기로_표시된다()
    {
        MatchSimulation sim = Build(out PlayerState _);
        var kicks = new List<KickReport>();
        sim.BallKicked += kicks.Add;
        RunUntilOut(sim);
        sim.BallKicked -= kicks.Add;
        Assert.AreEqual(1, kicks.Count, "전제: 밖으로 찬 첫 킥 하나");
        Assert.IsFalse(kicks[0].IsThrowIn, "터치라인 밖으로 찬 일반 패스는 던지기가 아니다");

        KickReport thrown = RunUntilThrown(sim);
        Assert.AreEqual(ThrowerId, thrown.KickerId);
        Assert.IsTrue(thrown.IsThrowIn, "던지는 선수의 패스는 던지기");
    }

    [Test]
    public void 재개_중_마지막에_닿은_팀은_공을_못_잡고_자리_2m_밖에_머문다()
    {
        MatchSimulation sim = Build(out PlayerState thrower);
        RunUntilOut(sim);
        float spotX = sim.Ball.X;
        PlayerState kicker = sim.Players[OwnerId];
        thrower.X = 30f;   // 재개 팀 선수를 멀리 두어 팀0 선수가 먼저 공에 닿는 상황을 만든다
        thrower.Z = 0f;

        for (int i = 0; i < 200 && sim.Ball.Phase != BallPhase.Owned; i++)
        {
            sim.Tick(Dt);
            Assert.GreaterOrEqual(DistanceToSpot(kicker, spotX), MatchTuning.ThrowInOpponentDistance - 1e-3f, "상대는 2m 밖");
        }
        Assert.AreEqual(BallPhase.Owned, sim.Ball.Phase, "재개 팀 선수가 걸어와 잡는다");
        Assert.AreEqual(1, sim.OwnerTeam());
    }

    [Test]
    public void 재개_팀_GK는_공_위에_있어도_던지지_않는다()
    {
        MatchSimulation sim = Build(out PlayerState thrower);
        RunUntilOut(sim);
        PlayerState keeper = sim.Players[3];
        keeper.X = sim.Ball.X;
        keeper.Z = sim.Ball.Z;
        thrower.X = 20f;
        thrower.Z = 20f;

        var owners = new List<PossessionReport>();
        sim.PossessionChanged += owners.Add;
        for (int i = 0; i < 500 && owners.Count == 0; i++) { sim.Tick(Dt); }

        Assert.AreEqual(1, owners.Count);
        Assert.AreNotEqual(3, owners[0].NewOwnerId, "GK는 후보가 아니다");
        Assert.AreEqual(1, owners[0].NewOwnerTeam);
    }

    [Test]
    public void 던진_선수는_다른_선수가_닿기_전엔_다시_못_잡는다()
    {
        MatchSimulation sim = Build(out PlayerState thrower);
        RunUntilOut(sim);
        RunUntilThrown(sim);
        // 던진 직후 다른 필드 선수를 30m 밖으로 보낸다. 안 보내면 받은 선수가 다시 차서 재접촉 금지가 정상으로 풀린 뒤를 보게 된다
        MoveOtherFieldPlayersAway(sim, thrower, 30f);
        // 공이 던진 선수 반경을 벗어난 다음 틱까지 돌려 찬 선수 가드(lastKickerId)를 풀어 둔다. 남는 막이는 재접촉 금지뿐이다
        for (int i = 0; i < 50 && DistanceToBall(sim, thrower) <= MatchTuning.CaptureRadius; i++) { sim.Tick(Dt); }
        sim.Tick(Dt);
        Assert.AreNotEqual(BallPhase.Owned, sim.Ball.Phase, "전제: 아직 아무도 안 받았다");

        // 공이 튕겨 던진 선수 발밑(잡기 반경 안)에 멈췄다. 가드가 없으면 던진 선수가 바로 잡는다
        sim.Ball = BallState.FreeAt(thrower.X + 0.5f, thrower.Z);
        var owners = new List<PossessionReport>();
        sim.PossessionChanged += owners.Add;
        for (int i = 0; i < 750 && owners.Count == 0; i++) { sim.Tick(Dt); }

        Assert.AreEqual(1, owners.Count, "누군가는 잡는다(교착 없음)");
        Assert.AreNotEqual(ThrowerId, owners[0].NewOwnerId, "IFAB 15조: 다른 선수가 닿기 전 재접촉 금지");
    }

    [Test]
    public void 던진_선수는_루즈볼_담당에서_빠져_같은_팀_동료가_주우러_온다()
    {
        // 던진 선수가 가장 가까워도 담당으로 뽑히면 공 위에 서서 못 잡고, 동료는 담당이 아니라 안 와서 상대가 줍는다(11-17 리뷰)
        MatchSimulation sim = Build(out PlayerState thrower);
        RunUntilOut(sim);
        RunUntilThrown(sim);

        sim.Ball = BallState.FreeAt(thrower.X, thrower.Z - 3f);
        PlayerState mate = sim.Players[5];
        mate.X = thrower.X;
        mate.Z = thrower.Z - 9f;     // 공에서 6m: 던진 선수(3m) 다음
        PlayerState opponent = sim.Players[2];
        opponent.X = thrower.X;
        opponent.Z = thrower.Z - 13f;   // 공에서 10m
        sim.Players[OwnerId].X = thrower.X > 0f ? thrower.X - 40f : thrower.X + 40f;
        var owners = new List<PossessionReport>();
        sim.PossessionChanged += owners.Add;
        for (int i = 0; i < 750 && owners.Count == 0; i++) { sim.Tick(Dt); }

        Assert.AreEqual(1, owners.Count);
        Assert.AreEqual(mate.PlayerId, owners[0].NewOwnerId, "던진 선수 다음으로 가까운 동료가 담당");
    }

    private static void MoveOtherFieldPlayersAway(MatchSimulation sim, PlayerState thrower, float distance)
    {
        for (int i = 0; i < sim.Players.Count; i++)
        {
            PlayerState p = sim.Players[i];
            if (p.PlayerId == ThrowerId || p.IsGoalkeeper) { continue; }
            p.X = thrower.X > 0f ? thrower.X - distance : thrower.X + distance;
            p.Z = 0f;
        }
    }

    private static float DistanceToBall(MatchSimulation sim, PlayerState p)
    {
        float dx = p.X - sim.Ball.X;
        float dz = p.Z - sim.Ball.Z;
        return (float)System.Math.Sqrt(dx * dx + dz * dz);
    }

    [Test]
    public void 스로인_대기_중_킥오프가_오면_재개_상태가_지워진다()
    {
        MatchSimulation sim = Build(out PlayerState thrower);
        RunUntilOut(sim);
        thrower.X = 30f;
        thrower.Z = 0f;

        sim.Kickoff();   // 하프타임 리셋과 같은 길(KickoffBy → Kickoff)
        PlayerState teamZero = sim.Players[2];
        teamZero.X = 0.3f;
        teamZero.Z = 0f;
        sim.Tick(Dt);

        Assert.AreEqual(BallPhase.Owned, sim.Ball.Phase);
        Assert.AreEqual(0, sim.OwnerTeam(), "재개 팀 거르기가 남아 있으면 팀0은 중앙 공을 못 잡는다");
    }

    [Test]
    public void 같은_주사위_수열이면_스로인까지_같은_경기다()
    {
        // 주사위가 상수면 소비 순서가 바뀌어도 같은 값이 나와 못 잡는다. 수열로 두 번 돌려 스로인과 그 뒤 킥 순서가 같은지 본다
        List<(int tick, int kicker)> a = Record();
        List<(int tick, int kicker)> b = Record();
        Assert.Greater(a.Count, 2, "전제: 밖으로 차기·스로인·그 뒤 킥이 있었다");
        Assert.AreEqual(ThrowerId, a[1].kicker, "두 번째 킥이 스로인");
        CollectionAssert.AreEqual(a, b);
    }

    private static List<(int tick, int kicker)> Record()
    {
        int k = 0;
        MatchSimulation sim = Build(() => (k++ * 0.618034f) % 1f, out _);
        var kicks = new List<(int tick, int kicker)>();
        int tick = 0;
        sim.BallKicked += r => kicks.Add((tick, r.KickerId));
        for (tick = 0; tick < 1500; tick++) { sim.Tick(Dt); }
        return kicks;
    }

    [Test]
    public void 대기_중인_던지는_선수에겐_2m_링의_상대도_태클하지_못한다()
    {
        MatchSimulation sim = Build(out PlayerState thrower);
        RunUntilOut(sim);
        for (int i = 0; i < 200 && !thrower.IsThrowInTaker; i++) { sim.Tick(Dt); }
        Assert.IsTrue(thrower.IsThrowInTaker, "전제: 잡았다");

        PlayerState marker = sim.Players[OwnerId];
        int before = sim.TackleAttemptCount;
        for (int i = 0; i < 60; i++)
        {
            marker.X = thrower.X;      // 매 틱 던지는 선수 바로 옆에 붙인다(밀기 뒤 자리 2m 링, 선수와는 태클 사거리 안)
            marker.Z = thrower.Z - 1.2f;
            sim.Tick(Dt);
            if (!thrower.IsThrowInTaker) { break; }
        }
        Assert.AreEqual(before, sim.TackleAttemptCount, "손에 든 공은 경합 대상이 아니다");
        Assert.AreEqual(1, sim.OwnerTeam());
    }

    [Test]
    public void 던진_공이_아무도_안_닿고_다시_나가면_새_스로인이고_던진_팀이_마지막_터치다()
    {
        MatchSimulation sim = Build(out PlayerState thrower);
        RunUntilOut(sim);
        RunUntilThrown(sim);

        sim.Ball = BallRules.Kick(BallState.FreeAt(thrower.X + 5f, SpotZ + 0.5f), 0f, 1f, 5f);   // 던진 공이 그대로 라인 밖으로 굴러간다(마지막 터치 = 던진 팀1)
        RunUntilOut(sim);

        Assert.AreEqual(0, sim.ThrowInTeam, "이번엔 팀0이 던진다");
        var owners = new List<PossessionReport>();
        sim.PossessionChanged += owners.Add;
        for (int i = 0; i < 750 && owners.Count == 0; i++) { sim.Tick(Dt); }
        Assert.AreEqual(0, owners[0].NewOwnerTeam);
    }

    [Test]
    public void 재개_팀에_필드_선수가_없으면_누구나_줍는_자유_공이다()
    {
        // 리트머스·소인원 테스트 시뮬: 팀1은 GK뿐. 재개 팀을 세우면 아무도 못 잡아 영원히 멈춘다
        var tree = new SelectorNode(new KickOutOnce(), PlayerTreeBuilder.Build());
        var sim = new MatchSimulation(() => 0.5f, tree, TestThreat.Real);
        sim.AddPlayer(new PlayerState(OwnerId, 0, Field(), 0f, 30f));
        sim.AddPlayer(new PlayerState(2, 0, Field(), -10f, 20f));
        sim.AddPlayer(new PlayerState(3, 1, Keeper(), 48f, 0f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, OwnerId, 0f, 30f);
        RunUntilOut(sim);

        Assert.AreEqual(RestartRules.NoTeam, sim.ThrowInTeam);
        for (int i = 0; i < 750 && sim.Ball.Phase != BallPhase.Owned; i++) { sim.Tick(Dt); }
        Assert.AreEqual(0, sim.OwnerTeam(), "예전처럼 가까운 팀0이 줍는다");
    }

    [Test]
    public void 던지는_선수가_아닌_선수가_공을_가지면_스로인은_끝난다()
    {
        // 지금은 잡기 거르기·태클 면제로 안 생기는 길이다. 생기면 상대를 옛 자리 밖으로 계속 밀게 되니 소유 변경에서 끝낸다
        MatchSimulation sim = Build(out _);
        RunUntilOut(sim);
        Assert.AreEqual(1, sim.ThrowInTeam, "전제: 재개 중");

        PlayerState other = sim.Players[2];
        sim.Ball = BallRules.Own(sim.Ball, other.PlayerId, other.X, other.Z);
        sim.Tick(Dt);

        Assert.AreEqual(RestartRules.NoTeam, sim.ThrowInTeam);
    }

    [Test]
    public void 던진_공을_받은_선수에겐_2초_대기가_이어지지_않는다()
    {
        MatchSimulation sim = Build(out _);
        RunUntilOut(sim);
        RunUntilThrown(sim);
        var owners = new List<PossessionReport>();
        sim.PossessionChanged += owners.Add;
        for (int i = 0; i < 500 && owners.Count == 0; i++) { sim.Tick(Dt); }

        Assert.AreEqual(1, owners.Count, "전제: 누군가 받았다");
        Assert.AreNotEqual(ThrowerId, owners[0].NewOwnerId);
        Assert.Less(sim.HoldUpTicksLeft, (int)(MatchTuning.ThrowInSetupSeconds / Dt) - 2, "받은 선수는 개인 볼 끌기만(테스트 스탯 0)");
    }
}
