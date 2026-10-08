using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Placement;
using NUnit.Framework;

// 경기 루프 검증(엔진 없음). 리트머스: ST 1 vs GK 1, 슛 10회의 결과가 각각 MatchRules.Resolve(p_i, roll_i)와 같고
// 같은 주사위 수열이면 같은 경기가 나온다. 그 외 잡기·라인 아웃·GK 출격 한계.
// 시뮬 테스트 공용 가짜 트리: 트리 대신 의도를 직접 넣는다(아무 의도 없음·한 번 슛·한 번 패스·공으로 이동).
// PassFlightTests도 여기 것을 쓴다(09-29 리뷰: PassOnce·ShootOnce가 두 파일에 복사돼 있던 것을 하나로 모음)
internal static class PassFlightTestsHelper
{
    internal sealed class NoOp : BehaviorNode
    {
        public override NodeState Tick(IPlayerContext ctx)
        {
            return NodeState.Success;
        }
    }

    // 공을 가지면 한 번 슛
    internal sealed class ShootOnce : BehaviorNode
    {
        private bool done;

        public override NodeState Tick(IPlayerContext ctx)
        {
            if (!done && ctx.OwnsBall) { ctx.Shoot(); done = true; }
            return NodeState.Success;
        }
    }

    // 켜진 뒤 공을 가진 선수가 한 번 슛(하프타임 직전 슛 테스트, 09-30). 켜기 전엔 아무 의도도 없다
    internal sealed class ShootWhenArmed : BehaviorNode
    {
        public bool Armed;
        private bool done;

        public override NodeState Tick(IPlayerContext ctx)
        {
            if (!Armed || done || !ctx.OwnsBall) { return NodeState.Success; }
            ctx.Shoot();
            done = true;
            return NodeState.Success;
        }
    }

    // 지정 선수가 공을 가지면 한 번 지정 동료에게 패스
    internal sealed class PassOnce : BehaviorNode
    {
        private readonly int passer;
        private readonly int receiver;
        private bool done;

        public PassOnce(int passer, int receiver)
        {
            this.passer = passer;
            this.receiver = receiver;
        }

        public override NodeState Tick(IPlayerContext ctx)
        {
            if (!done && ctx.PlayerId == passer && ctx.OwnsBall) { ctx.Pass(receiver); done = true; }
            return NodeState.Success;
        }
    }

    // 공을 안 가진 선수는 공으로
    internal sealed class ChaseBall : BehaviorNode
    {
        public override NodeState Tick(IPlayerContext ctx)
        {
            if (!ctx.OwnsBall) { ctx.MoveToward(ctx.BallX, ctx.BallZ); }
            return NodeState.Success;
        }
    }
}

public class MatchSimulationTests
{
    private const float Dt = 0.02f;

    private static PlayerStats Striker()
    {
        return new PlayerStats { RoleId = "ST", VariantId = "st_poacher", Speed = 60, Stamina = 45, Pass = 40, Shot = 90, Tackle = 20, Positioning = 45, PushUp = 25f, PressRange = 8f, ShotBias = 0.3f, PassLength = 15f };
    }

    private static PlayerStats Keeper()
    {
        return new PlayerStats { RoleId = "GK", VariantId = "gk_standard", Speed = 30, Stamina = 30, Pass = 30, Shot = 5, Tackle = 10, Positioning = 15, Reflexes = 80, Handling = 60, Diving = 40, PressRange = 3f, ShotBias = 0.9f, PassLength = 25f };
    }

    // 박스 앞 전진(09-29 D1)·슛 블록(D3) 테스트용 CB. 스탯은 GK 값을 빌리고 역할만 CB, 압박 거리 6m
    private static PlayerStats CenterBack()
    {
        PlayerStats cb = Keeper();
        cb.RoleId = "CB";
        cb.VariantId = "cb_test";
        cb.PressRange = 6f;
        return cb;
    }

    // 주사위 수열. 다 쓰면 예외: 슛이 예상보다 많이 굴리면 테스트가 알아챈다
    private sealed class RollQueue
    {
        private readonly Queue<float> rolls;
        public RollQueue(IEnumerable<float> values) { rolls = new Queue<float>(values); }
        public float Next() { return rolls.Dequeue(); }
        public int Remaining => rolls.Count;
    }

    private static MatchSimulation LitmusMatch(RollQueue rolls)
    {
        var sim = new MatchSimulation(rolls.Next, PlayerTreeBuilder.BuildLitmus()) { ResetAfterEveryShot = true };
        sim.AddPlayer(new PlayerState(playerId: 0, team: 0, Striker(), x: -10f, z: 0f));   // StageComposition 1스테이지와 같은 자리
        sim.AddPlayer(new PlayerState(playerId: 1, team: 1, Keeper(), x: 50f, z: 0f));
        sim.Kickoff();
        return sim;
    }

    private static List<ShotReport> RunShots(MatchSimulation sim, int shots, int maxTicks = 20000)
    {
        var reports = new List<ShotReport>();
        sim.ShotResolved += r => reports.Add(r);
        int ticks = 0;
        while (reports.Count < shots && ticks < maxTicks)
        {
            sim.Tick(Dt);
            ticks++;
        }
        Assert.AreEqual(shots, reports.Count, $"{maxTicks}틱 안에 슛 {shots}회가 나와야 한다");
        return reports;
    }

    [Test]
    public void 리트머스_슛_10회_결과는_각각_Resolve_p_roll과_같다()
    {
        // 슛마다 골 주사위 1 + 조준 주사위 1(09-18 슛 오차), 골이 아니고 골문 안이면 캐치 주사위 1. 30개면 넉넉하다.
        // 조준 roll은 0.5(정중앙)로 고정해 골문 안이 되게 한다(오차 자체는 ShotDirectionTests가 검증)
        float[] sequence = { 0.10f,0.5f, 0.50f,0.5f,0.50f, 0.90f,0.5f,0.20f, 0.35f,0.5f,0.70f, 0.05f,0.5f, 0.60f,0.5f,0.99f, 0.40f,0.5f,0.30f, 0.80f,0.5f,0.15f, 0.55f,0.5f,0.25f, 0.65f,0.5f,0.45f, 0.75f,0.5f,0.33f, 0.66f,0.5f,0.11f };
        var rolls = new RollQueue(sequence);
        MatchSimulation sim = LitmusMatch(rolls);

        List<ShotReport> reports = RunShots(sim, 10);

        int cursor = 0;
        int goals = 0;
        float expectedGoals = 0f;
        foreach (ShotReport r in reports)
        {
            Assert.AreEqual(0, r.ShooterId, "쏘는 건 ST뿐");
            expectedGoals += r.Probability;
            float goalRoll = sequence[cursor++];
            float aimRoll = sequence[cursor++];
            Assert.AreEqual(0.5f, aimRoll, "테스트 수열의 조준 roll 자리");
            if (MatchRules.Resolve(r.Probability, goalRoll))
            {
                Assert.AreEqual(ShotOutcome.Goal, r.Outcome);
                goals++;
                continue;
            }

            float catchRoll = sequence[cursor++];
            ShotOutcome expected = MatchRules.Resolve(MatchRules.CatchProbability(60), catchRoll) ? ShotOutcome.Caught : ShotOutcome.Parried;
            Assert.AreEqual(expected, r.Outcome, "골이 아니면 GK가 정면에서 받는다: 캐치 아니면 튕김");
        }

        Assert.AreEqual(goals, sim.HomeGoals, "득점 합 = Goal 결과 수");
        Assert.AreEqual(0, sim.AwayGoals);
        Assert.AreEqual(sequence.Length - cursor, rolls.Remaining, "주사위는 슛 판정에만 쓴다");
        Assert.That(expectedGoals, Is.InRange(1.5f, 4.5f), "ST(shot 90) 대 GK(80/40) 10회 기대 골. 판당 2~4골 축(스펙 §0)의 감을 잡는 값");
        // 트리는 상대 GK를 모르므로 중립 GK(50)로 "확률 ≥ shotBias 0.3"을 판단하고, 보고된 p는 실제 GK(80·40 → 60)로 낮아진다.
        // 로짓 -0.2만큼이라 0.3 문턱이 약 0.25로 내려온다. 그보다 훨씬 가까이 가서 쏘지도(0.5 이상) 않는다
        Assert.That(reports[0].Probability, Is.InRange(0.22f, 0.5f), "shotBias 0.3(중립 GK 기준) 넘자마자 쏜다");
    }

    [Test]
    public void 같은_주사위_수열이면_같은_경기다()
    {
        float[] sequence = { 0.10f,0.5f, 0.50f,0.5f,0.50f, 0.90f,0.5f,0.20f, 0.35f,0.5f,0.70f, 0.05f,0.5f, 0.60f,0.5f,0.99f, 0.40f,0.5f,0.30f, 0.80f,0.5f,0.15f, 0.55f,0.5f,0.25f, 0.65f,0.5f,0.45f, 0.75f,0.5f,0.33f, 0.66f,0.5f,0.11f };
        List<ShotReport> a = RunShots(LitmusMatch(new RollQueue(sequence)), 10);
        List<ShotReport> b = RunShots(LitmusMatch(new RollQueue(sequence)), 10);

        for (int i = 0; i < a.Count; i++)
        {
            Assert.AreEqual(a[i].Outcome, b[i].Outcome, $"{i}번째 결과");
            Assert.AreEqual(a[i].Probability, b[i].Probability, $"{i}번째 확률");
        }
    }

    [Test]
    public void 킥오프_뒤_가장_가까운_선수가_공을_잡고_드리블하면_공이_따라간다()
    {
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.BuildLitmus());
        PlayerState st = sim.AddPlayer(new PlayerState(0, 0, Striker(), -10f, 0f));
        sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        sim.Kickoff();

        for (int i = 0; i < 150; i++) { sim.Tick(Dt); }   // 3초: 10m 달려가 잡고 드리블 시작

        Assert.AreEqual(BallPhase.Owned, sim.Ball.Phase);
        Assert.AreEqual(0, sim.Ball.OwnerId);
        Assert.AreEqual(st.X, sim.Ball.X, 1e-4f, "소유 중 공은 소유자 발에 있다");
        Assert.Greater(st.X, 0f, "잡은 뒤 상대 골 쪽으로 전진");
    }

    [Test]
    public void 슛_한_틱_이동이_잡기_반경보다_짧다()
    {
        // 골라인 판정이 세이브 판정보다 먼저 도는 Tick 순서가 안전한 조건. 이게 깨지면 골라인 앞 GK가 세이브를 놓친다
        Assert.Less(MatchTuning.ShotSpeed * Dt, MatchTuning.CaptureRadius);
    }

    [Test]
    public void 골라인에_못_미치고_멈춘_슛은_빗나감으로_마감되고_상대_GK가_공을_갖는다()
    {
        PlayerStats farShooter = Striker();
        farShooter.ShotBias = 0f;   // 확률 0이어도 쏜다(다이얼 하한이 0)
        var sim = new MatchSimulation(() => 0.99f, PlayerTreeBuilder.BuildLitmus());
        PlayerState st = sim.AddPlayer(new PlayerState(0, 0, farShooter, -45f, 0f));   // 골라인까지 97.5m > 정지 거리 78m
        sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, st.PlayerId, st.X, st.Z);   // 공을 ST 발에 직접 놓는다(킥오프 공은 중앙이라 달려가 잡으면 거리가 짧아짐)
        var reports = new List<ShotReport>();
        sim.ShotResolved += r => reports.Add(r);

        for (int i = 0; i < 1200; i++) { sim.Tick(Dt); if (reports.Count > 0) { break; } }   // 바로 쏘고 멈출 때까지(6.25초 = 313틱)

        Assert.AreEqual(1, reports.Count);
        Assert.AreEqual(ShotOutcome.Missed, reports[0].Outcome);
        Assert.AreEqual(0f, reports[0].Probability, "40m 밖은 확률 0");
        Assert.AreEqual(BallPhase.Owned, sim.Ball.Phase, "골킥: 수비 팀 GK 소유(09-23)");
        Assert.AreEqual(1, sim.Ball.OwnerId, "상대 GK");
    }

    private static PlayerStats Defender(int tackle)
    {
        return new PlayerStats { RoleId = "CB", VariantId = "cb_centreback", Speed = 40, Stamina = 50, Pass = 40, Shot = 20, Tackle = tackle, Positioning = 60, PressRange = 6f, ShotBias = 0.8f, PassLength = 20f };
    }

    [Test]
    public void 태클_확률은_tackle_스탯에_단조_증가하고_기본_확률_아래다()
    {
        Assert.Less(MatchRules.TackleProbability(20), MatchRules.TackleProbability(50));
        Assert.Less(MatchRules.TackleProbability(50), MatchRules.TackleProbability(80));
        Assert.Less(MatchRules.TackleProbability(100), MatchTuning.TackleBaseChance);
        Assert.AreEqual(MatchTuning.TackleBaseChance * 0.5f, MatchRules.TackleProbability(50), 1e-5f, "50이면 기본의 절반");
    }

    [Test]
    public void 붙은_상대는_면역이_끝난_뒤_태클해_성공하면_공을_뺏고_Turnover가_난다()
    {
        // 주사위 0 = 항상 성공. 소유자(팀0)는 가만히, 태클러(팀1, tackle 100)가 0.8m에 붙어 있다
        var sim = new MatchSimulation(() => 0f, new PassFlightTestsHelper.ChaseBall());   // 자유 공이 되면 둘 다 쫓는다
        sim.AddPlayer(new PlayerState(0, 0, Striker(), 0f, 0f));
        sim.AddPlayer(new PlayerState(1, 1, Defender(100), 0.8f, 0f));
        var kinds = new List<PossessionChange>();
        sim.PossessionChanged += r => kinds.Add(r.Kind);
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 0f);

        for (int i = 0; i < MatchTuning.PossessionImmunityTicks - 1; i++) { sim.Tick(Dt); }
        Assert.AreEqual(0, sim.TackleAttemptCount, "면역 중엔 시도 없음");
        Assert.AreEqual(0, sim.Ball.OwnerId);

        for (int i = 0; i < 100 && sim.TurnoverCount == 0; i++) { sim.Tick(Dt); }
        Assert.AreEqual(1, sim.TackleSuccessCount, "면역이 끝나면 태클이 성공한다(주사위 0)");
        Assert.AreEqual(1, sim.Ball.OwnerId, "태클러가 공을 가진다(깔끔한 탈취)");
        Assert.AreEqual(1, sim.TurnoverCount);
        Assert.IsTrue(kinds.Contains(PossessionChange.Turnover));
    }

    [Test]
    public void 빗나간_슛_뒤_골킥이면_박스_안_상대는_박스_밖으로_나간다()
    {
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.ShootOnce());   // 0.99 = 골 안 됨·조준은 빗나감
        PlayerState st = sim.AddPlayer(new PlayerState(0, 0, Striker(), 44f, 0f));
        sim.AddPlayer(new PlayerState(1, 1, Keeper(), 48f, 0f));
        sim.AddPlayer(new PlayerState(2, 0, Striker(), 46f, 10f));   // 박스 안(깊이 6.5m, z 10)
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 44f, 0f);
        for (int i = 0; i < 300 && !(sim.Ball.Phase == BallPhase.Owned && sim.Ball.OwnerId == 1); i++) { sim.Tick(Dt); }
        Assert.AreEqual(1, sim.Ball.OwnerId, "GK 소유(골킥)");
        Assert.LessOrEqual(sim.Players[2].X, 35f + 1e-3f, "박스 밖(x ≤ 35)으로");
        Assert.LessOrEqual(st.X, 35f + 1e-3f);
    }

    [Test]
    public void 공을_잡은_GK는_태클당하지_않는다()
    {
        // 규칙 12조. 주사위 0 = 시도하면 항상 성공인데도 GK 소유 중엔 시도 자체가 없어야 한다
        var sim = new MatchSimulation(() => 0f, new PassFlightTestsHelper.ChaseBall());
        sim.AddPlayer(new PlayerState(0, 0, Keeper(), -48f, 0f));
        sim.AddPlayer(new PlayerState(1, 1, Defender(100), -47.2f, 0f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, -48f, 0f);
        for (int i = 0; i < 100; i++) { sim.Tick(Dt); }
        Assert.AreEqual(0, sim.TackleAttemptCount);
        Assert.AreEqual(0, sim.Ball.OwnerId, "GK가 그대로 갖는다");
    }

    [Test]
    public void 태클에_실패하면_태클러는_정지하고_쿨다운_동안_재시도하지_않는다()
    {
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.ChaseBall());   // 주사위 0.99 = 항상 실패
        sim.AddPlayer(new PlayerState(0, 0, Striker(), 0f, 0f));
        PlayerState t = sim.AddPlayer(new PlayerState(1, 1, Defender(80), 0.8f, 0f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 0f);

        for (int i = 0; i < MatchTuning.PossessionImmunityTicks + 2; i++) { sim.Tick(Dt); }
        Assert.AreEqual(1, sim.TackleAttemptCount, "면역 끝나면 한 번 시도");
        Assert.AreEqual(0, sim.TackleSuccessCount);
        Assert.Greater(t.FrozenTicks, MatchTuning.TackleFailFreezeTicks - 5, "실패 → 정지");
        float x = t.X;
        for (int i = 0; i < 10; i++) { sim.Tick(Dt); }
        Assert.AreEqual(x, t.X, 1e-5f, "정지 중엔 공을 쫓아도 안 움직인다");
        Assert.AreEqual(1, sim.TackleAttemptCount, "쿨다운 중 재시도 없음");
    }

    [Test]
    public void 킥오프는_지정_팀의_중앙_최근접_필드_플레이어가_공을_갖고_골_뒤엔_실점_팀이_킥오프한다()
    {
        // 팀0 ST(-8,0)·GK(-48,0), 팀1 ST(8,0)·GK(48,0). 주사위 0 = 슛은 항상 골
        var sim = new MatchSimulation(() => 0f, new PassFlightTestsHelper.NoOp());
        sim.AddPlayer(new PlayerState(0, 0, Keeper(), -48f, 0f));
        sim.AddPlayer(new PlayerState(1, 0, Striker(), -8f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, Keeper(), 48f, 0f));
        sim.AddPlayer(new PlayerState(3, 1, Striker(), 8f, 0f));

        sim.KickoffBy(1);
        Assert.AreEqual(BallPhase.Owned, sim.Ball.Phase);
        Assert.AreEqual(3, sim.Ball.OwnerId, "팀1 ST가 킥오프(GK 제외)");

        sim.KickoffBy(0);
        Assert.AreEqual(1, sim.Ball.OwnerId, "팀0 ST");

        // 팀0이 골을 넣으면 팀1이 킥오프
        var shooter = new MatchSimulation(() => 0f, new PassFlightTestsHelper.ShootOnce());
        shooter.AddPlayer(new PlayerState(0, 0, Striker(), 40f, 0f));
        shooter.AddPlayer(new PlayerState(1, 1, Keeper(), 48f, 0f));
        shooter.AddPlayer(new PlayerState(2, 1, Striker(), 8f, 0f));
        shooter.Kickoff();
        shooter.Ball = BallRules.Own(shooter.Ball, 0, 40f, 0f);
        for (int i = 0; i < 300 && shooter.HomeGoals == 0; i++) { shooter.Tick(Dt); }
        Assert.AreEqual(1, shooter.HomeGoals);
        Assert.AreEqual(2, shooter.Ball.OwnerId, "실점한 팀1의 ST가 킥오프");
    }

    [Test]
    public void 세이브_뒤_튕긴_공은_멈추기_전에도_잡을_수_있다()
    {
        // 09-21 리뷰: 파링(8m/s, 감속 4)은 슛도 패스도 아닌 비행이라 잡기 분기가 없어 2초(100틱) 동안 아무도 못 잡았다
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.BuildLitmus());
        sim.AddPlayer(new PlayerState(0, 0, Striker(), 3f, 0f));   // 튕긴 공의 경로 위 3m 앞
        sim.Kickoff();
        sim.Ball = BallRules.Kick(BallState.FreeAt(0f, 0f), 1f, 0f, MatchTuning.ParrySpeed);

        int ticks = 0;
        while (sim.Ball.Phase != BallPhase.Owned && ticks < 100) { sim.Tick(Dt); ticks++; }
        Assert.AreEqual(0, sim.Ball.OwnerId, "경로 위 선수가 잡는다");
        Assert.Less(ticks, 100, "공이 멈추기(100틱) 전에 잡아야 한다");
    }

    [Test]
    public void GK는_박스_밖_자유_공을_쫓지_않는다()
    {
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.BuildLitmus());
        PlayerState gk = sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        sim.Kickoff();   // 공은 중앙(0,0), GK 박스는 x ≥ 36

        for (int i = 0; i < 100; i++) { sim.Tick(Dt); }

        Assert.AreEqual(50f, gk.X, "자리 유지");
        Assert.AreEqual(BallPhase.Free, sim.Ball.Phase);
    }

    [Test]
    public void 라인_밖으로_나간_공은_골킥_스로인_자리로_돌아온다()
    {
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.BuildLitmus());
        sim.AddPlayer(new PlayerState(0, 0, Striker(), -40f, -30f));   // 아무도 공 근처에 없게
        sim.Kickoff();

        // 슛이 아닌 굴림으로 터치라인 밖까지 보낸다: Kick은 시뮬 내부라 공 상태를 직접 만든다
        sim.Ball = BallRules.Kick(BallState.FreeAt(0f, 33.9f), 0f, 1f, 5f);
        sim.Tick(Dt);   // 33.9 + 5·0.02 = 34.0 → 아직 안 → 한 틱 더
        sim.Tick(Dt);

        Assert.AreEqual(BallPhase.Free, sim.Ball.Phase);
        Assert.AreEqual(FieldBounds.HalfWidth - MatchTuning.ThrowInInset, sim.Ball.Z, 1e-4f, "스로인 자리");
    }

    [Test]
    public void 킥오프는_키커가_센터_마크에_서고_상대는_센터서클_밖이며_첫_패스는_옆_뒤_동료에게_간다()
    {
        // IFAB 8조(09-29). 팀1 ST는 1-톱 편성처럼 서클 안(6m)에 서 있다가 밖으로 밀려나야 한다
        var sim = new MatchSimulation(() => 0.99f, PlayerTreeBuilder.Build());
        sim.AddPlayer(new PlayerState(0, 0, Keeper(), -48f, 0f));
        PlayerState st1 = sim.AddPlayer(new PlayerState(1, 0, Striker(), -8f, -6f));
        PlayerState st2 = sim.AddPlayer(new PlayerState(2, 0, Striker(), -8f, 6f));
        sim.AddPlayer(new PlayerState(3, 1, Keeper(), 48f, 0f));
        PlayerState opp = sim.AddPlayer(new PlayerState(4, 1, Striker(), 6f, 0f));

        sim.KickoffBy(0);
        Assert.AreEqual(BallPhase.Owned, sim.Ball.Phase);
        Assert.AreEqual(1, sim.Ball.OwnerId, "등거리면 PlayerId 작은 ST");
        Assert.AreEqual(0f, sim.Ball.X, 1e-4f, "공은 센터 마크");
        Assert.AreEqual(0f, sim.Ball.Z, 1e-4f);
        Assert.AreEqual(0f, st1.X, 1e-4f, "키커가 공 위에 선다");
        float oppDist = (float)System.Math.Sqrt(opp.X * opp.X + opp.Z * opp.Z);
        Assert.GreaterOrEqual(oppDist, FieldBounds.CenterCircleRadius - 1e-4f, "상대는 9.15m 밖");
        Assert.Greater(opp.X, 0f, "상대는 자기 진영");

        for (int i = 0; i < 100 && sim.PassCount == 0; i++) { sim.Tick(Dt); }
        Assert.AreEqual(1, sim.PassCount, "첫 행동은 패스");
        sim.Tick(Dt);
        Assert.IsTrue(st2.IsPassTarget, "옆·뒤 동료(ST2)에게");
        Assert.IsFalse(st1.IsKickoffTaker, "첫 킥 뒤엔 키커 표시가 풀린다");
    }

    [Test]
    public void 돌파_의도는_소유_때_드리블_성향으로_한_번_굴리고_패스하면_풀린다()
    {
        // 09-29. 주사위 0 → 성향이 0보다 크면 의도가 선다. 우리 진영 서드·드리블 성향 0이면 굴리지 않는다
        PlayerStats winger = Striker(); winger.Dribble = 0.9f;
        var sim = new MatchSimulation(() => 0f, new PassFlightTestsHelper.NoOp());
        PlayerState w = sim.AddPlayer(new PlayerState(0, 0, winger, 0f, 20f));
        PlayerState mate = sim.AddPlayer(new PlayerState(1, 0, Striker(), 10f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, Keeper(), 48f, 0f));
        sim.Kickoff();
        sim.Ball = BallState.FreeAt(0.3f, 20f);
        sim.Tick(Dt);
        Assert.AreEqual(0, sim.Ball.OwnerId);
        Assert.IsTrue(w.WantsTakeOn, "중원에서 잡음 + 성향 0.9 + 주사위 0");
        Assert.AreEqual(1, sim.TakeOnCount);

        var own = new MatchSimulation(() => 0f, new PassFlightTestsHelper.NoOp());
        PlayerState back = own.AddPlayer(new PlayerState(0, 0, winger, -30f, 20f));
        own.AddPlayer(new PlayerState(1, 1, Keeper(), 48f, 0f));
        own.Kickoff();
        own.Ball = BallState.FreeAt(-29.7f, 20f);
        own.Tick(Dt);
        Assert.AreEqual(0, own.Ball.OwnerId);
        Assert.IsFalse(back.WantsTakeOn, "우리 진영 서드에선 굴리지 않는다");

        var pass = new MatchSimulation(() => 0f, new PassFlightTestsHelper.PassOnce(0, 1));
        PlayerState passer = pass.AddPlayer(new PlayerState(0, 0, winger, 0f, 20f));
        pass.AddPlayer(new PlayerState(1, 0, Striker(), 10f, 0f));
        pass.AddPlayer(new PlayerState(2, 1, Keeper(), 48f, 0f));
        pass.Kickoff();
        pass.Ball = BallState.FreeAt(0.3f, 20f);
        for (int i = 0; i < 60 && pass.PassCount == 0; i++) { pass.Tick(Dt); }
        pass.Tick(Dt);
        Assert.AreEqual(1, pass.PassCount);
        Assert.IsFalse(passer.WantsTakeOn, "패스하면 돌파 의도가 풀린다");
    }

    [Test]
    public void 공이_박스_앞에_오면_압박_거리_밖이어도_가장_가까운_센터백이_압박_1순위가_된다()
    {
        // 09-29 수비 D1. 팀 0 CB(압박 거리 6m)가 공에서 9m. 공은 팀 0 골라인에서 18m(전진 구역 안), 팀 1이 소유
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp());
        PlayerState near = sim.AddPlayer(new PlayerState(0, 0, CenterBack(), -43.5f, 0f));
        PlayerState far = sim.AddPlayer(new PlayerState(1, 0, CenterBack(), -45f, 15f));
        sim.AddPlayer(new PlayerState(2, 1, Striker(), -34.5f, 0f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 2, -34.5f, 0f);
        sim.Tick(Dt);
        Assert.AreEqual(0, near.PressRank, "가장 가까운 CB가 전진");
        Assert.AreEqual(int.MaxValue, far.PressRank, "다른 CB는 후보가 아니다(압박 거리 밖)");

        var outside = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp());
        PlayerState cbOut = outside.AddPlayer(new PlayerState(0, 0, CenterBack(), -30f, 0f));
        outside.AddPlayer(new PlayerState(2, 1, Striker(), -21f, 0f));
        outside.Kickoff();
        outside.Ball = BallRules.Own(outside.Ball, 2, -21f, 0f);
        outside.Tick(Dt);
        Assert.AreEqual(int.MaxValue, cbOut.PressRank, "골라인에서 31.5m면 전진 구역 밖이라 9m 떨어진 CB는 후보가 아니다");
    }

    [Test]
    public void 막힌_슛은_자유_공이_되고_슈터가_바로_되잡지_못한다()
    {
        // 09-29 수비 D3 + 리뷰 🔴: 슈터 (40,0), 수비수 1.5m 정면. 주사위 0 = 골 판정 성공이어도 블록(0 < 0.3)이 먼저
        var sim = new MatchSimulation(() => 0f, new PassFlightTestsHelper.ShootOnce());
        sim.AddPlayer(new PlayerState(0, 0, Striker(), 40f, 0f));
        sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, CenterBack(), 41.5f, -0.3f));   // 주사위 0이면 조준이 -Z 쪽(약 -12.6°)이라 그 쪽에 선다
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 40f, 0f);
        var outcomes = new List<ShotOutcome>();
        sim.ShotResolved += r => outcomes.Add(r.Outcome);
        sim.Tick(Dt);
        Assert.AreEqual(1, outcomes.Count);
        Assert.AreEqual(ShotOutcome.Blocked, outcomes[0]);
        Assert.AreEqual(0, sim.HomeGoals);
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase, "튕긴 공은 누구 것도 아닌 비행");
        sim.Tick(Dt);
        Assert.AreNotEqual(0, sim.Ball.OwnerId, "슈터는 바로 못 잡는다");
    }

    [Test]
    public void 팀_1도_박스_앞에서_가장_가까운_센터백이_압박_1순위가_된다()
    {
        // 09-30 리뷰: D1 대칭. 팀 1 골라인은 x = +52.5. 공은 거기서 18m, 팀 0이 소유
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp());
        sim.AddPlayer(new PlayerState(0, 0, Striker(), 34.5f, 0f));
        PlayerState near = sim.AddPlayer(new PlayerState(1, 1, CenterBack(), 43.5f, 0f));
        PlayerState far = sim.AddPlayer(new PlayerState(2, 1, CenterBack(), 45f, 15f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 34.5f, 0f);
        sim.Tick(Dt);
        Assert.AreEqual(0, near.PressRank, "가장 가까운 CB가 전진");
        Assert.AreEqual(int.MaxValue, far.PressRank, "다른 CB는 후보가 아니다");
    }

    [Test]
    public void 팀_1_슈터의_슛도_앞의_수비수가_막는다()
    {
        // 09-30 리뷰: D3 대칭. 팀 1 슈터 (-40,0)가 -X 골로 슛. 블록 판정과 튕김 방향(+X)만 본다.
        // 튕김을 스냅샷 좌표로 계산하는 것(블로커가 먼저 움직인 틱)은 여기서 검증되지 않는다: ShootOnce가 블로커를 움직이지 않는다
        var sim = new MatchSimulation(() => 0f, new PassFlightTestsHelper.ShootOnce());
        sim.AddPlayer(new PlayerState(0, 0, CenterBack(), -41.5f, -0.3f));
        sim.AddPlayer(new PlayerState(1, 0, Keeper(), -50f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, Striker(), -40f, 0f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 2, -40f, 0f);
        var outcomes = new List<ShotOutcome>();
        sim.ShotResolved += r => outcomes.Add(r.Outcome);
        sim.Tick(Dt);
        Assert.AreEqual(1, outcomes.Count);
        Assert.AreEqual(ShotOutcome.Blocked, outcomes[0]);
        Assert.Greater(sim.Ball.VelX, 0f, "슛 반대 방향(+X)으로 튕긴다");
    }

    [Test]
    public void 태클에_실패해_얼어_있는_수비수는_슛을_막지_못한다()
    {
        // 09-30 리뷰: 블록 후보도 압박·루즈볼 후보처럼 얼어 있는 선수를 뺀다. 위치는 막힌 슛 테스트와 같다
        var sim = new MatchSimulation(() => 0f, new PassFlightTestsHelper.ShootOnce());
        sim.AddPlayer(new PlayerState(0, 0, Striker(), 40f, 0f));
        sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        PlayerState frozen = sim.AddPlayer(new PlayerState(2, 1, CenterBack(), 41.5f, -0.3f));
        sim.Kickoff();
        frozen.FrozenTicks = MatchTuning.TackleFailFreezeTicks;
        sim.Ball = BallRules.Own(sim.Ball, 0, 40f, 0f);
        var outcomes = new List<ShotOutcome>();
        sim.ShotResolved += r => outcomes.Add(r.Outcome);
        sim.Tick(Dt);
        CollectionAssert.DoesNotContain(outcomes, ShotOutcome.Blocked);
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase, "슛은 그대로 날아간다");
        Assert.Greater(sim.Ball.VelX, 0f, "골 쪽(+X)으로");
    }

    // 하프타임 한 틱 전에 슛을 쏘게 해 두고 하프타임 틱까지 돌린다. 슈터 (20,0)에서 골까지 32.5m라 공은 1초 넘게 난다
    private static (MatchSimulation sim, List<ShotOutcome> outcomes) ShotAtHalfTime(float roll)
    {
        var tree = new PassFlightTestsHelper.ShootWhenArmed();
        var sim = new MatchSimulation(() => roll, tree);
        PlayerState shooter = sim.AddPlayer(new PlayerState(0, 0, Striker(), -10f, 0f));
        sim.AddPlayer(new PlayerState(1, 1, Keeper(), 52f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, Striker(), 30f, 25f));   // 후반 킥오프를 받을 팀 1 필드 선수. 슛 라인에서 멀리
        sim.KickoffBy(0);
        int halfTimeTick = MatchClock.HalfTimeTick(AddedTime.None);
        for (int i = 0; i < halfTimeTick - 2; i++) { sim.Tick(Dt); }

        shooter.X = 20f;
        shooter.Z = 0f;
        sim.Ball = BallRules.Own(sim.Ball, 0, 20f, 0f);
        tree.Armed = true;
        var outcomes = new List<ShotOutcome>();
        sim.ShotResolved += r => outcomes.Add(r.Outcome);
        sim.Tick(Dt);   // 하프타임 한 틱 전: 슛
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase, "하프타임 직전에 슛이 날아가는 중");
        Assert.IsEmpty(outcomes);
        sim.Tick(Dt);   // 하프타임 틱
        Assert.IsTrue(sim.SidesSwitched, "하프타임이 지났다");
        return (sim, outcomes);
    }

    [Test]
    public void 하프타임_휘슬_순간_날아가던_골이_될_슛은_골로_남는다()
    {
        // 09-29 재검증 ②: 하프타임 리셋이 비행 중인 슛을 Finish 없이 지워 골이 사라졌다. 결과는 찬 순간 정해져 있으니 그 결과로 마감한다
        (MatchSimulation sim, List<ShotOutcome> outcomes) = ShotAtHalfTime(0f);   // 주사위 0 = 골 판정 성공, 조준은 골문 안
        CollectionAssert.AreEqual(new[] { ShotOutcome.Goal }, outcomes);
        Assert.AreEqual(1, sim.HomeGoals);
        Assert.AreEqual(1, sim.OwnerTeam(), "후반 킥오프는 전반에 킥오프하지 않은 팀 1(실점 팀 규칙보다 하프타임 규칙이 먼저)");
    }

    [Test]
    public void 하프타임_휘슬_순간_날아가던_골이_아닌_슛은_빗나감으로_마감한다()
    {
        // 세이브가 될 슛이었을 수도 있지만 GK에 닿기 전이라 빗나감으로 센다. 슛 수와 결과 수가 어긋나지 않게 결과는 반드시 하나 낸다
        (MatchSimulation sim, List<ShotOutcome> outcomes) = ShotAtHalfTime(0.99f);   // 주사위 0.99 = 골 판정 실패
        CollectionAssert.AreEqual(new[] { ShotOutcome.Missed }, outcomes);
        Assert.AreEqual(0, sim.HomeGoals);
    }

    [Test]
    public void 킥오프_전에_바꾼_역할_값은_선수에게_다시_준다()
    {
        // 09-30 개인 전술: 선수는 세팅 국면(GameReady)에 스폰되므로, 그 화면에서 바꾼 역할·지시는 킥오프 때 다시 넣는다. 자리가 바뀌는 교체는 막는다
        PlayerState striker = new PlayerState(0, 0, Striker(), 10f, 0f);
        PlayerStats changed = Striker();
        changed.PressRange = 14f;
        striker.ReplaceStats(changed);
        Assert.AreEqual(14f, striker.Stats.PressRange);
        Assert.Throws<System.InvalidOperationException>(() => striker.ReplaceStats(Keeper()), "필드 선수를 골키퍼 값으로 바꿀 수 없다");
    }
}
