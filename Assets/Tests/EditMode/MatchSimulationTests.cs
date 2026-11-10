using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Placement;
using Game.Core.Tactics;
using NUnit.Framework;

// 경기 루프 검증(엔진 없음). 리트머스: ST 1 vs GK 1, 슛 10회의 결과가 각각 MatchRules.Resolve(p_i, roll_i)와 같고
// 같은 주사위 수열이면 같은 경기가 나온다. 그 외 잡기·라인 아웃·GK 출격 한계.
// 시뮬 테스트 공용 가짜 트리: 트리 대신 의도를 직접 넣는다(아무 의도 없음·한 번 슛·한 번 패스·공으로 이동).
// PassFlightTests도 여기 것을 쓴다(09-29 리뷰: PassOnce·ShootOnce가 두 파일에 복사돼 있던 것을 하나로 모음)
internal static class PassFlightTestsHelper
{
    // 받는 선수 앞 긴 리드 착지점으로 패스(트리의 역습·킥오프 패스와 같은 식). 도우미 노드가 착지점을 직접 안 정해도 되게
    internal static void PassWithLead(IPlayerContext ctx, int receiverId)
    {
        for (int i = 0; i < ctx.Teammates.Count; i++)
        {
            TargetInfo m = ctx.Teammates[i];
            if (m.PlayerId != receiverId) { continue; }
            float average = MatchTuning.PassAverageSpeed[ctx.Tactics.Tempo[(int)ctx.BallThird]];
            (float x, float z) landing = PassRules.LandingPoint(ctx.X, ctx.Z, m.X, m.Z, ctx.AttackSign, average, MatchTuning.PassLeadMax);
            ctx.Pass(receiverId, landing.x, landing.z);
            return;
        }
    }

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
            if (!done && ctx.OwnsBall)
            {
                ctx.Shoot();
                done = true;
            }
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
            if (!done && ctx.PlayerId == passer && ctx.OwnsBall)
            {
                PassWithLead(ctx, receiver);
                done = true;
            }
            return NodeState.Success;
        }
    }

    // 공을 안 가진 선수는 공으로
    internal sealed class ChaseBall : BehaviorNode
    {
        public override NodeState Tick(IPlayerContext ctx)
        {
            if (!ctx.OwnsBall) { ctx.ArriveToward(ctx.BallX, ctx.BallZ); }
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

    // 라인 앞 전진(09-29 D1 박스 앞 → 11-09 D2-b)·슛 블록(D3) 테스트용 CB. 스탯은 GK 값을 빌리고 역할만 CB, 압박 거리 6m
    private static PlayerStats CenterBack()
    {
        PlayerStats cb = Keeper();
        cb.RoleId = "CB";
        cb.VariantId = "cb_test";
        cb.PressRange = 6f;
        return cb;
    }

    // 뒷줄 일반화(11-06 D1) 테스트용. 역할 이름만 다르고 값은 CB와 같다: 전진 후보는 이름이 아니라 수비 자리 깊이로 갈린다
    private static PlayerStats FullBack()
    {
        PlayerStats fb = CenterBack();
        fb.RoleId = "FB";
        fb.VariantId = "fb_test";
        return fb;
    }

    private static PlayerStats Midfielder()
    {
        PlayerStats dm = CenterBack();
        dm.RoleId = "DM";
        dm.VariantId = "dm_test";
        return dm;
    }

    private static MatchSimulation LitmusMatch(RollQueue rolls)
    {
        var sim = new MatchSimulation(rolls.Next, PlayerTreeBuilder.BuildLitmus(), TestThreat.Real) { ResetAfterEveryShot = true };
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
        // 슛마다 조준 주사위 2(11-09 B2 정규 오차) + 골 주사위 1(순서: 블록 → 조준 → 골. 블로커가 없어 블록 주사위는 안 쓴다), 골이 아니고 골문 안이면 캐치 주사위 1. 40개면 넉넉하다.
        // 조준 roll은 (0.5, 0.5) = 표준정규 −1.18로 고정: 구석 목표 3.16에서 σ(12m·shot 90 ≈ 1.1m)만큼 안쪽이라 골문 안이고 GK(z 0)가 닿는다(오차 자체는 ShotDirectionTests가 검증)
        // p는 유효 슛 조건부 골 확률(11-09 B3, 14m 안팎·shot 90 대 GK 60이면 0.35 근처)이라, 골 roll은 그 띠를 피해 0.15 이하(골) 또는 0.8 이상(아님)으로만 둔다. 골 5·세이브 5
        float[] sequence = { 0.5f,0.5f,0.10f, 0.5f,0.5f,0.95f,0.50f, 0.5f,0.5f,0.90f,0.20f, 0.5f,0.5f,0.05f, 0.5f,0.5f,0.99f,0.70f, 0.5f,0.5f,0.15f, 0.5f,0.5f,0.85f,0.99f, 0.5f,0.5f,0.12f, 0.5f,0.5f,0.80f,0.30f, 0.5f,0.5f,0.08f, 0.5f,0.5f,0.5f,0.5f };
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
            float aimRoll1 = sequence[cursor++];
            float aimRoll2 = sequence[cursor++];
            float goalRoll = sequence[cursor++];
            Assert.AreEqual(0.5f, aimRoll1, "테스트 수열의 조준 roll 자리");
            Assert.AreEqual(0.5f, aimRoll2, "테스트 수열의 조준 roll 자리");
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
        Assert.That(expectedGoals, Is.InRange(2.5f, 6.5f), "ST(shot 90) 대 GK(80/40) 10회 기대 골(유효 슛 조건부 골 확률, 11-09 B3)");
        // 트리는 상대 GK를 모르므로 중립 GK(50)로 "확률 ≥ shotBias 0.3"을 판단하고, 보고된 p는 실제 GK(80·40 → 60)로 낮아진다.
        // 보고된 p는 xG가 아니라 유효 슛 조건부 골 확률(B3: 거리 로지스틱 + 스탯 차 × 0.5)이라 0.3~0.45 근처. 그보다 훨씬 가까이 가서 쏘지도(0.7 이상) 않는다
        Assert.That(reports[0].Probability, Is.InRange(0.3f, 0.7f), "shotBias 0.3(중립 GK 기준) 넘자마자 쏜다. 보고된 p는 유효 슛 조건부 골 확률");
    }

    // ── GK 슛 대응(10-23 유저 "안 들어가면 골대 안으로 가면 안 된다")
    // 노골로 정해진 유효 슛은 세이브로 끝난다. GK가 못 닿을 자리면 찬 순간 조준을 GK 닿는 범위로 당긴다(11-09 B3. 옛 PushWide는 포스트 밖으로 밀어 빗나감으로 만들었다)
    [Test]
    public void 세이브로_정해진_슛은_GK가_닿는_범위로_조준이_당겨진다()
    {
        // GK 자리 z +3 → 조준 목표는 반대쪽 구석 −3.16(B2), 조준 roll (0, ·) → 오차 0이라 정확히 −3.16, 골 roll 0.99(노골 = 세이브). 가로 6m는 다이빙 8m/s로도 못 간다
        // 리트머스 트리엔 GK 이동 노드가 없어 접촉 세이브 대신 골라인 세이브로 끝난다(접촉까지 보려면 전체 트리, 이동은 다른 GK 테스트가 본다)
        var sim = new MatchSimulation(new RollQueue(new[] { 0f, 0.5f, 0.99f, 0.5f, 0.5f, 0.5f }).Next, PlayerTreeBuilder.BuildLitmus(), TestThreat.Real) { ResetAfterEveryShot = true };
        PlayerState striker = sim.AddPlayer(new PlayerState(0, 0, Striker(), -10f, 0f));
        sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 3f));
        float aimZ = float.NaN;
        sim.BallKicked += k =>
        {
            if (!k.IsShot) { return; }
            aimZ = striker.Z + k.DirZ;
        };
        sim.Kickoff();

        List<ShotReport> reports = RunShots(sim, 1);

        Assert.IsTrue(reports[0].Outcome == ShotOutcome.Caught || reports[0].Outcome == ShotOutcome.Parried, "세이브로 정해진 슛은 빗나감이 아니라 세이브");
        Assert.IsTrue(MatchRules.IsOnTarget(aimZ), "당긴 조준도 골문 안");
        Assert.Greater(aimZ, -(FieldBounds.GoalHalfWidth - MatchTuning.AimInset) + 0.5f, "구석 −3.16에서 GK(+3) 쪽으로 당겨졌다");
    }

    [Test]
    public void 골문_밖_조준은_GK_접촉_없이_빗나감이고_PushWide가_없다()
    {
        // GK z 0 → 목표 +3.16, 조준 roll (0.99, 0.99) = 표준정규 +3.02 → 3σ 바깥(골문 밖), 골 roll 0(골문 안이었다면 골). 공은 조준 그대로 날아간다(옛 PushWide의 포스트 밖 0.5m 고정점이 아님)
        var sim = new MatchSimulation(new RollQueue(new[] { 0.99f, 0.99f, 0f, 0.5f, 0.5f, 0.5f }).Next, PlayerTreeBuilder.BuildLitmus(), TestThreat.Real) { ResetAfterEveryShot = true };
        PlayerState striker = sim.AddPlayer(new PlayerState(0, 0, Striker(), -10f, 0f));
        PlayerState keeper = sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        float aimZ = float.NaN;
        float expectedZ = float.NaN;
        sim.BallKicked += k =>
        {
            if (!k.IsShot) { return; }
            aimZ = striker.Z + k.DirZ;
            float corner = FieldBounds.GoalHalfWidth - MatchTuning.AimInset;
            float dx = FieldBounds.HalfLength - striker.X;
            float dz = corner - striker.Z;
            float sigma = MatchRules.AimSigmaMeters((float)System.Math.Sqrt(dx * dx + dz * dz), MatchRules.AimSigmaDeg(striker.Stats.Shot), pressed: false);
            expectedZ = MatchRules.ShotAimZ(+1f, sigma, 0.99f, 0.99f);
        };
        sim.Kickoff();

        List<ShotReport> reports = RunShots(sim, 1);

        Assert.AreEqual(ShotOutcome.Missed, reports[0].Outcome, "골 주사위가 골이어도 골문 밖이면 빗나감");
        Assert.IsFalse(MatchRules.IsOnTarget(aimZ));
        Assert.AreEqual(expectedZ, aimZ, 1e-3f, "공은 조준(구석 + 정규 오차) 그대로");
        Assert.AreEqual(0f, keeper.Z, 1e-3f, "GK는 제자리(골라인 당김 없음)");
    }

    [Test]
    public void 노골_유효_슛을_GK가_닿을_수_있으면_접촉이_없어도_골라인에서_세이브로_끝난다()
    {
        // GK z 0(중앙) → 목표 +3.16, 조준 roll (0.5, 0.5) = −1.18σ(12m·shot 90 σ ≈ 1.1) → z 약 1.7(골문 안), 골 roll 0.99(노골).
        // GK가 가로 1.7m(라인에선 더 짧다)를 다이빙 8m/s로 반응 지연 뒤에도 닿는다 → 유효 슛 유지. 리트머스 트리엔 GK 이동 노드가 없어 접촉(0.8m)은 안 생기고 골라인에서 세이브
        var sim = new MatchSimulation(new RollQueue(new[] { 0.5f, 0.5f, 0.99f, 0.5f, 0.5f, 0.5f }).Next, PlayerTreeBuilder.BuildLitmus(), TestThreat.Real) { ResetAfterEveryShot = true };
        sim.AddPlayer(new PlayerState(0, 0, Striker(), -10f, 0f));
        PlayerState keeper = sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        sim.Kickoff();

        List<ShotReport> reports = RunShots(sim, 1);

        Assert.AreNotEqual(ShotOutcome.Missed, reports[0].Outcome, "골문 안 노골은 Missed가 아니다");
        Assert.AreNotEqual(ShotOutcome.Goal, reports[0].Outcome);
        Assert.IsTrue(reports[0].Outcome == ShotOutcome.Caught || reports[0].Outcome == ShotOutcome.Parried);
        // GK·공의 당겨진 자리는 ResetAfterEveryShot의 킥오프가 바로 되돌려 여기선 못 본다. 라인 안쪽(발 반경)으로 들이는 효과는 러너 probe로 확인(10-24: 라인 밖에 두면 배급이 "나간 공"이 돼 골 1.37 → 2.00)
    }

    [Test]
    public void 포스트_근처_빗나감에도_GK는_반응_차례가_오고_세이브는_없다()
    {
        // 11-09 B4-b(유저 "GK 멀뚱"). GK z 0 → 목표 +3.16, 조준 roll (0.393, 0) = 표준정규 +1.0 → 3.16 + σ(1.1~1.5m)라 포스트(3.66) 밖 1.5m 안, 골 roll 0(골문 안이었다면 골)
        var sim = new MatchSimulation(new RollQueue(new[] { 0.393f, 0f, 0f, 0.5f, 0.5f, 0.5f }).Next, PlayerTreeBuilder.BuildLitmus(), TestThreat.Real) { ResetAfterEveryShot = true };
        PlayerState striker = sim.AddPlayer(new PlayerState(0, 0, Striker(), -10f, 0f));
        PlayerState keeper = sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        float aimZ = float.NaN;
        float shotX = 0f;
        float shotZ = 0f;
        sim.BallKicked += k =>
        {
            if (!k.IsShot) { return; }
            shotX = striker.X;
            shotZ = striker.Z;
            aimZ = striker.Z + k.DirZ;
        };
        var reports = new List<ShotReport>();
        sim.ShotResolved += r => reports.Add(r);
        sim.Kickoff();
        for (int i = 0; i < 20000 && sim.Ball.Phase != BallPhase.Flight; i++) { sim.Tick(Dt); }
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase, "슛이 떠야 한다");
        Assert.Greater(aimZ, FieldBounds.GoalHalfWidth, "골문 밖");
        Assert.LessOrEqual(aimZ, FieldBounds.GoalHalfWidth + MatchTuning.NearMissReact, "근소 빗나감(포스트 밖 1.5m 안)");
        sim.Tick(Dt);   // 대기·반응 스냅샷은 슛이 뜬 다음 틱에 채운다
        Assert.IsTrue(keeper.WaitingForShot, "근소 빗나감도 반응 대기가 있다");

        for (int i = 2; i < MatchTuning.KeeperReactionTicks; i++) { sim.Tick(Dt); }
        sim.Tick(Dt);
        Assert.IsTrue(keeper.IncomingShot, "반응 지연(노골 1배) 뒤 차례");
        float ballAtLine = MatchRules.BallZAtLine(shotX, shotZ, FieldBounds.HalfLength, aimZ, PositionRules.KeeperLineX(keeper.DefendHomeX, keeper.DefendHomeZ, keeper.AttackSign, keeper.Stats.LineHeight));
        float clearance = MatchTuning.CaptureRadius + MatchTuning.NearMissKeeperClearance;
        float expected = MatchRules.NearMissKeeperZ(ballAtLine, shotX, shotZ, FieldBounds.HalfLength, aimZ, clearance);
        Assert.AreEqual(expected, keeper.IncomingShotTargetZ, 1e-3f, "목표는 공 선에서 수직으로 발 반경 + 여유만큼 골 안쪽(11-10, 비스듬한 슛은 각도로 나눔)");
        Assert.Less(keeper.IncomingShotTargetZ, ballAtLine - clearance + 1e-4f, "가로로는 최소 그만큼 비킨다");

        for (int i = 0; i < 400 && reports.Count == 0; i++) { sim.Tick(Dt); }
        Assert.AreEqual(1, reports.Count);
        Assert.AreEqual(ShotOutcome.Missed, reports[0].Outcome, "근소 빗나감에 세이브는 없다");
    }

    [Test]
    public void 근소_빗나감에_몸을_던진_GK와_공_선의_간격은_발_반경보다_넓다()
    {
        // 11-10 재검증 🟡: 조준을 포스트로 잘라 그 교차점으로 가면 포스트 밖 1m 안 빗나감은 공이 GK 발 반경 안을 지나갔다(판정은 빗나감, 화면은 몸 통과).
        // 전체 트리(⑨b)라 GK가 실제로 움직인다. GK z 0 → 목표 +3.16, 조준 roll (0.09, 0) = 표준정규 +0.43 → 약 4.0(포스트 밖 0.35m), 골 roll 0(골문 안이었다면 골)
        var sim = new MatchSimulation(new RollQueue(new[] { 0.09f, 0f, 0f, 0.5f, 0.5f, 0.5f }).Next, PlayerTreeBuilder.Build(), TestThreat.Real) { ResetAfterEveryShot = true };
        PlayerState striker = sim.AddPlayer(new PlayerState(0, 0, Striker(), 30f, 0f));
        PlayerState keeper = sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        float aimZ = float.NaN;
        float shotX = 0f;
        float shotZ = 0f;
        float lastKeeperX = 0f;
        float lastKeeperZ = 0f;
        float closest = float.MaxValue;
        bool reacted = false;
        sim.BallKicked += k =>
        {
            if (!k.IsShot) { return; }
            shotX = striker.X;
            shotZ = striker.Z;
            aimZ = striker.Z + k.DirZ;
        };
        var reports = new List<ShotReport>();
        sim.ShotResolved += r => reports.Add(r);
        sim.Kickoff();
        for (int i = 0; i < 20000 && sim.Ball.Phase != BallPhase.Flight; i++) { sim.Tick(Dt); }
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase, "슛이 떠야 한다");
        Assert.Greater(aimZ, FieldBounds.GoalHalfWidth, "전제: 골문 밖");
        Assert.LessOrEqual(aimZ, FieldBounds.GoalHalfWidth + 0.5f, "전제: 포스트 밖 0.5m 안(옛 규칙이면 공 선이 GK 발 반경 안을 지나는 띠)");

        for (int i = 0; i < 400 && reports.Count == 0; i++)
        {
            sim.Tick(Dt);
            if (reports.Count > 0) { break; }
            if (keeper.IncomingShot) { reacted = true; }
            lastKeeperX = keeper.X;
            lastKeeperZ = keeper.Z;
            float dx = sim.Ball.X - keeper.X;
            float dz = sim.Ball.Z - keeper.Z;
            closest = System.Math.Min(closest, (float)System.Math.Sqrt(dx * dx + dz * dz));
        }

        Assert.AreEqual(ShotOutcome.Missed, reports[0].Outcome, "판정은 빗나감 그대로");
        Assert.IsTrue(reacted, "GK는 반응해 몸을 던진다");
        Assert.Greater(lastKeeperZ, 0.5f, "GK는 공 쪽으로 움직였다(제자리 아님)");
        float ballLineZ = MatchRules.BallZAtLine(shotX, shotZ, FieldBounds.HalfLength, aimZ, lastKeeperX);
        Assert.Greater(ballLineZ - lastKeeperZ, MatchTuning.CaptureRadius, "GK 최종 자리와 공 선의 가로 간격 > 발 반경");
        Assert.Greater(closest, MatchTuning.CaptureRadius, "비행 내내 공이 GK 발 반경 안을 지나지 않는다");
    }

    [Test]
    public void 포스트_밖_1_5m를_넘는_빗나감엔_GK가_반응하지_않는다()
    {
        // 조준 roll (0.99, 0.99) = +3.02σ → 포스트 밖 1.5m 너머. 대기·반응 모두 없다
        var sim = new MatchSimulation(new RollQueue(new[] { 0.99f, 0.99f, 0f, 0.5f, 0.5f, 0.5f }).Next, PlayerTreeBuilder.BuildLitmus(), TestThreat.Real) { ResetAfterEveryShot = true };
        PlayerState striker = sim.AddPlayer(new PlayerState(0, 0, Striker(), -10f, 0f));
        PlayerState keeper = sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        float aimZ = float.NaN;
        sim.BallKicked += k =>
        {
            if (!k.IsShot) { return; }
            aimZ = striker.Z + k.DirZ;
        };
        sim.Kickoff();
        for (int i = 0; i < 20000 && sim.Ball.Phase != BallPhase.Flight; i++) { sim.Tick(Dt); }
        Assert.Greater(aimZ, FieldBounds.GoalHalfWidth + MatchTuning.NearMissReact);
        for (int i = 0; i <= MatchTuning.KeeperReactionTicks; i++)
        {
            Assert.IsFalse(keeper.WaitingForShot, $"{i}틱: 대기 없음");
            Assert.IsFalse(keeper.IncomingShot, $"{i}틱: 반응 없음");
            sim.Tick(Dt);
        }
    }

    [Test]
    public void GK는_슛이_뜬_뒤_반응_지연이_지나야_조준점으로_움직일_차례가_된다()
    {
        // 조준 roll (0.5, 0.5) → 구석 목표 3.16 − 1.18σ(골문 안), 골 roll 0.99(노골)
        var sim = new MatchSimulation(new RollQueue(new[] { 0.5f, 0.5f, 0.99f, 0.5f, 0.5f, 0.5f }).Next, PlayerTreeBuilder.BuildLitmus(), TestThreat.Real) { ResetAfterEveryShot = true };
        PlayerState striker = sim.AddPlayer(new PlayerState(0, 0, Striker(), -10f, 0f));
        PlayerState keeper = sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        float shotX = 0f;
        float shotZ = 0f;
        sim.BallKicked += k =>
        {
            if (!k.IsShot) { return; }
            shotX = striker.X;
            shotZ = striker.Z;
        };
        sim.Kickoff();
        for (int i = 0; i < 20000 && sim.Ball.Phase != BallPhase.Flight; i++) { sim.Tick(Dt); }
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase, "슛이 떠야 한다");
        Assert.IsFalse(keeper.IncomingShot, "뜬 틱엔 아직");
        for (int i = 1; i < MatchTuning.KeeperReactionTicks; i++) { sim.Tick(Dt); Assert.IsFalse(keeper.IncomingShot, $"{i}틱: 반응 지연 안"); }
        sim.Tick(Dt);
        Assert.IsTrue(keeper.IncomingShot, "반응 지연이 지나면 차례");
        float corner = FieldBounds.GoalHalfWidth - MatchTuning.AimInset;
        float targetDistance = (float)System.Math.Sqrt((FieldBounds.HalfLength - shotX) * (FieldBounds.HalfLength - shotX) + (corner - shotZ) * (corner - shotZ));
        float aimZ = MatchRules.ShotAimZ(+1f, MatchRules.AimSigmaMeters(targetDistance, MatchRules.AimSigmaDeg(90), pressed: false), 0.5f, 0.5f);
        Assert.AreEqual(MatchRules.BallZAtLine(shotX, shotZ, FieldBounds.HalfLength, aimZ, keeper.X), keeper.IncomingShotTargetZ, 1e-3f, "GK 목표 = 조준점(구석 + 정규 오차)이 GK 라인(x 50)을 지나는 z(10-25)");
    }

    [Test]
    public void GK는_슛에_반응하는_동안_스탯이_아니라_다이빙_속도로_조준점까지_간다()
    {
        // 전체 트리(⑨b). GK z 0 → 목표 +3.16, 조준 roll (0, ·) → 오차 0이라 정확히 +3.16, 골 roll 0.99(노골). 반응 지연이 지난 첫 틱에 GK가 움직인 거리 = 8m/s × 0.02 = 0.16m(스탯 30이면 0.084)
        var sim = new MatchSimulation(new RollQueue(new[] { 0f, 0.5f, 0.99f, 0.5f, 0.5f, 0.5f }).Next, PlayerTreeBuilder.Build(), TestThreat.Real) { ResetAfterEveryShot = true };
        sim.AddPlayer(new PlayerState(0, 0, Striker(), 30f, 0f));
        PlayerState keeper = sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        sim.Kickoff();
        for (int i = 0; i < 20000 && !keeper.IncomingShot; i++) { sim.Tick(Dt); }
        Assert.IsTrue(keeper.IncomingShot, "반응 차례가 와야 한다");
        float beforeX = keeper.X;
        float beforeZ = keeper.Z;
        sim.Tick(Dt);
        float moved = (float)System.Math.Sqrt((keeper.X - beforeX) * (keeper.X - beforeX) + (keeper.Z - beforeZ) * (keeper.Z - beforeZ));   // 자기 x(라인)까지 같이 가므로 대각선
        Assert.AreEqual(MatchTuning.KeeperDiveSpeed * Dt, moved, 1e-3f, "한 틱 이동 = 다이빙 속도");
        Assert.Greater(keeper.Z, beforeZ, "조준점(+3.16 구석) 쪽으로");
    }

    [Test]
    public void 골로_정해진_슛은_GK_자리에서_먼_구석으로_조준되고_GK는_버스트_없이_간다()
    {
        // 조준 roll (0.9, 0.5) = 표준정규 −2.15 → 구석 목표 3.16에서 2.15σ 안쪽(중앙 근처), 골 roll 0.05(골). GK z 0 → 파생 간격만큼 밀린다. 반응 ×2(20틱) 뒤 한 틱 이동 ≤ 스탯 30(4.2m/s) × 0.02 = 0.084(가속을 타서 더 짧다)
        var sim = new MatchSimulation(new RollQueue(new[] { 0.9f, 0.5f, 0.05f, 0.5f, 0.5f, 0.5f }).Next, PlayerTreeBuilder.Build(), TestThreat.Real) { ResetAfterEveryShot = true };
        PlayerState striker = sim.AddPlayer(new PlayerState(0, 0, Striker(), 30f, 0f));
        PlayerState keeper = sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        float shotX = 0f;
        float shotZ = 0f;
        sim.BallKicked += k =>
        {
            // 전체 트리라 슈터가 몰고 들어간 뒤 쏜다. 찬 자리를 받아 둔다
            if (!k.IsShot) { return; }
            shotX = striker.X;
            shotZ = striker.Z;
        };
        sim.Kickoff();
        for (int i = 0; i < 20000 && sim.Ball.Phase != BallPhase.Flight; i++) { sim.Tick(Dt); }
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase, "슛이 떠야 한다");
        for (int i = 1; i < MatchTuning.KeeperReactionTicks * 2; i++) { sim.Tick(Dt); Assert.IsFalse(keeper.IncomingShot, $"{i}틱: 골로 정해진 슛은 반응이 두 배 늦다"); }
        sim.Tick(Dt);
        Assert.IsTrue(keeper.IncomingShot, "반응 ×2가 지나면 차례");
        // 비행 시간은 밀기 전 조준점(구석 +3.16 − 2.15σ, GK 중앙이라 +쪽 구석) 기준
        float corner = FieldBounds.GoalHalfWidth - MatchTuning.AimInset;
        float targetDistance = (float)System.Math.Sqrt((FieldBounds.HalfLength - shotX) * (FieldBounds.HalfLength - shotX) + (corner - shotZ) * (corner - shotZ));
        float rawAimZ = MatchRules.ShotAimZ(+1f, MatchRules.AimSigmaMeters(targetDistance, MatchRules.AimSigmaDeg(90), pressed: false), 0.9f, 0.5f);
        float shotDistance = (float)System.Math.Sqrt((FieldBounds.HalfLength - shotX) * (FieldBounds.HalfLength - shotX) + (rawAimZ - shotZ) * (rawAimZ - shotZ));
        float fraction = MatchRules.LineFraction(shotX, FieldBounds.HalfLength, 50f);   // GK 라인 x 50까지의 비율
        float flightToLine = MatchRules.ShotFlightTime(shotDistance * fraction, MatchTuning.ShotSpeed, MatchTuning.BallDeceleration);   // 라인 교차점까지(감속 반영)
        float gapAtLine = MatchRules.GoalAimGap(MatchRules.SpeedMps(keeper.Stats.Speed), flightToLine, MatchTuning.KeeperReactionTicks * 2 * Dt);
        Assert.AreEqual(gapAtLine, System.Math.Abs(keeper.IncomingShotTargetZ), 1e-3f, "GK 목표(라인 교차점)가 GK에서 파생 간격만큼 먼 쪽(10-25: 스탯 속도 × 라인까지 남은 시간 + 발 반경 + 여유). 좌우는 오차 부호를 따른다");
        float beforeX = keeper.X;
        float beforeZ = keeper.Z;
        sim.Tick(Dt);
        float moved = (float)System.Math.Sqrt((keeper.X - beforeX) * (keeper.X - beforeX) + (keeper.Z - beforeZ) * (keeper.Z - beforeZ));
        // 10-29 가속: 버스트가 없으면 스탯 속도·가속을 탄다. 한 틱 이동은 스탯 속도 한 틱 이하이고, 다이빙 속도 한 틱보다 확실히 짧다
        Assert.LessOrEqual(moved, MatchRules.SpeedMps(keeper.Stats.Speed) * Dt + 1e-4f, "골엔 스탯 속도 이하(버스트 없음)");
        Assert.Less(moved, MatchTuning.KeeperDiveSpeed * Dt * 0.5f, "다이빙 버스트가 아니다");
        Assert.Greater(moved, 0f, "그래도 움직이긴 한다(스탯 가속)");
    }

    [Test]
    public void 유효_슛이_날아오는_동안_GK는_반응_차례_전엔_제자리다()
    {
        // 10-25 GK 간격 probe: 골의 56%가 마감 때 GK가 공 0.8m 안이었다. GK가 반응 ×2 전에도 자리 규칙(공 z 추적)으로 움직여
        // 간격 파생의 전제("지연 뒤에만 움직임")와 달랐다. GK z 1.5 → 목표 −3.16, 조준 roll (0.5, 0) = +1.18σ → 안쪽(골문 안, 유효), 골 roll 0.05 → 반응 ×2 = 20틱 동안 제자리
        var sim = new MatchSimulation(new RollQueue(new[] { 0.5f, 0f, 0.05f, 0.5f, 0.5f, 0.5f }).Next, PlayerTreeBuilder.Build(), TestThreat.Real) { ResetAfterEveryShot = true };
        sim.AddPlayer(new PlayerState(0, 0, Striker(), 30f, 0f));
        PlayerState keeper = sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 1.5f));   // 공 z 0과 다른 자리: 추적 슬라이드가 있었다면 움직인다
        sim.Kickoff();
        for (int i = 0; i < 20000 && sim.Ball.Phase != BallPhase.Flight; i++) { sim.Tick(Dt); }
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase, "슛이 떠야 한다");
        float holdX = keeper.X;
        float holdZ = keeper.Z;
        for (int i = 1; i < MatchTuning.KeeperReactionTicks * 2; i++)
        {
            sim.Tick(Dt);
            Assert.AreEqual(holdX, keeper.X, 1e-4f, $"{i}틱: 반응 전엔 x 그대로");
            Assert.AreEqual(holdZ, keeper.Z, 1e-4f, $"{i}틱: 반응 전엔 z 그대로");
        }
        sim.Tick(Dt);
        Assert.IsTrue(keeper.IncomingShot, "반응 ×2가 지나면 차례");
        sim.Tick(Dt);
        Assert.AreNotEqual(holdZ, keeper.Z, "차례가 오면 조준점 쪽으로 움직인다");
    }

    [Test]
    public void 노골_유효_슛도_GK는_반응_차례_전엔_제자리고_차례는_반응_1배다()
    {
        // GK z 1.5 → 목표 −3.16, 조준 roll (0.5, 0) = +1.18σ → 안쪽(골문 안), 골 roll 0.99(노골). 세이브 당김 폭(KeeperLateralReach)도 "지연 뒤부터 움직인다"고 보므로 대기는 노골도 같다
        var sim = new MatchSimulation(new RollQueue(new[] { 0.5f, 0f, 0.99f, 0.5f, 0.5f, 0.5f }).Next, PlayerTreeBuilder.Build(), TestThreat.Real) { ResetAfterEveryShot = true };
        sim.AddPlayer(new PlayerState(0, 0, Striker(), 30f, 0f));
        PlayerState keeper = sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 1.5f));
        sim.Kickoff();
        for (int i = 0; i < 20000 && sim.Ball.Phase != BallPhase.Flight; i++) { sim.Tick(Dt); }
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase, "슛이 떠야 한다");
        float holdX = keeper.X;
        float holdZ = keeper.Z;
        for (int i = 1; i < MatchTuning.KeeperReactionTicks; i++)
        {
            sim.Tick(Dt);
            Assert.AreEqual(holdX, keeper.X, 1e-4f, $"{i}틱: x 그대로");
            Assert.AreEqual(holdZ, keeper.Z, 1e-4f, $"{i}틱: z 그대로");
        }
        sim.Tick(Dt);
        Assert.IsTrue(keeper.IncomingShot, "노골은 반응 1배 뒤 차례");
    }

    [Test]
    public void GK는_슛을_잡으면_2초_들고_있다가_배급한다()
    {
        // 10-25 유저 Play "잡자마자 바로 패스하니 어색". 잡은 소유의 볼 끌기 대기 = KeeperHoldAfterCatchSeconds(FIFA 6초 안).
        // 주사위 상수 0.52: 골 roll은 22m 슛 확률(0.3 안팎)보다 커서 노골, 조준은 중앙 근처(유효), 캐치는 handling 60의 확률 0.54 아래라 캐치. 전체 트리는 돌파 주사위도 굴려 수열 대신 상수
        var sim = new MatchSimulation(() => 0.52f, PlayerTreeBuilder.Build(), TestThreat.Real);
        sim.AddPlayer(new PlayerState(0, 0, Striker(), 30f, 0f));
        PlayerState keeper = sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, Striker(), 30f, 5f));   // GK가 줄 동료
        var reports = new List<ShotReport>();
        sim.ShotResolved += r => reports.Add(r);
        sim.Kickoff();
        for (int i = 0; i < 20000 && reports.Count == 0; i++) { sim.Tick(Dt); }
        Assert.AreEqual(ShotOutcome.Caught, reports[0].Outcome, "잡아야 한다");
        Assert.AreEqual(keeper.PlayerId, sim.Ball.OwnerId);

        int holdTicks = (int)System.Math.Round(MatchTuning.KeeperHoldAfterCatchSeconds / Dt);
        for (int i = 0; i < holdTicks - 5; i++) { sim.Tick(Dt); }
        Assert.AreEqual(keeper.PlayerId, sim.Ball.OwnerId, "대기 안엔 아직 GK 발밑");
        Assert.AreEqual(BallPhase.Owned, sim.Ball.Phase);
        for (int i = 0; i < 60 && sim.Ball.Phase == BallPhase.Owned; i++) { sim.Tick(Dt); }
        Assert.AreNotEqual(BallPhase.Owned, sim.Ball.Phase, "대기가 끝나면 찬다");
    }

    [Test]
    public void GK가_없는_시뮬에서_골문_안_노골_슛은_빗나감으로_끝난다()
    {
        // 10-24 리뷰 🟡 ④: ResolveSaveAtLine은 GK가 없으면 false → 골라인에서 Missed. GK가 없으면 먼 포스트 구석 +3.16, 조준 roll (0, ·) → 오차 0(골문 안), 골 roll 0.99(노골)
        var sim = new MatchSimulation(new RollQueue(new[] { 0f, 0.5f, 0.99f }).Next, PlayerTreeBuilder.BuildLitmus(), TestThreat.Real) { ResetAfterEveryShot = true };
        sim.AddPlayer(new PlayerState(0, 0, Striker(), -10f, 0f));
        sim.Kickoff();

        List<ShotReport> reports = RunShots(sim, 1);

        Assert.AreEqual(ShotOutcome.Missed, reports[0].Outcome, "GK가 없으면 라인 세이브도 없다");
    }

    [Test]
    public void 후반_진영_교체_뒤에도_GK는_자기_골로_오는_슛에_반응한다()
    {
        // 10-24 리뷰 🟡 ④: IncomingShotFor는 슈터의 공격 부호로 팀을 찾는다. 진영이 바뀌면 부호도 바뀌어야 같은 GK가 걸린다.
        // 주사위 상수 0.5: 조준 roll 0.5 = 골문 중앙(유효 슛). 골 roll 0.5는 p에 따라 골/노골 어느 쪽이든 되므로 반응 ×2 + 1틱까지 기다린다
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.BuildLitmus(), TestThreat.Real) { ResetAfterEveryShot = true };
        sim.AddPlayer(new PlayerState(0, 0, Striker(), -10f, 0f));
        PlayerState keeper = sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        sim.KickoffBy(0);   // 진영 교체는 첫 킥오프 팀을 아는 경기(KickoffBy)에서만 일어난다
        while (!sim.IsSecondHalf) { sim.Tick(Dt); }
        Assert.AreEqual(+1, keeper.AttackSign, "후반엔 팀 1이 +X를 공격한다(진영 교체)");

        for (int i = 0; i < 20000 && sim.Ball.Phase != BallPhase.Flight; i++) { sim.Tick(Dt); }
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase, "후반에도 슛이 떠야 한다");
        for (int i = 0; i < MatchTuning.KeeperReactionTicks * 2 + 1 && !keeper.IncomingShot; i++) { sim.Tick(Dt); }
        Assert.IsTrue(keeper.IncomingShot, "교체된 진영에서도 GK 차례가 온다");
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
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.BuildLitmus(), TestThreat.Real);
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

    // 10-25: "골라인에 못 미치고 멈춘 슛" 테스트를 지웠다. 감속 1.2(실측 기반)에선 슛 정지 거리가 260m라 필드 어디서 쏴도 골라인에 닿아 그 경로를 만들 수 없다.
    // 규칙(ShotFlightTime < 0 → 빗나감)은 안전장치로 남겨 둔다

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
        var sim = new MatchSimulation(() => 0f, new PassFlightTestsHelper.ChaseBall(), TestThreat.Real);   // 자유 공이 되면 둘 다 쫓는다
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
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.ShootOnce(), TestThreat.Real);   // 0.99 = 골 안 됨·조준은 빗나감
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
    public void 골라인_밖으로_나간_공은_그_골을_지키는_팀_GK의_골킥이고_준비_시간_동안_차지_않으며_상대는_박스_밖에_머문다()
    {
        // 11-05 유저 Play "골킥 때 수비랑 공을 주고받는다": 전엔 골킥 자리에 주인 없는 공을 놓아 가까운 선수가 주웠고, 빗나간 슛 뒤 GK는 바로 찼다
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.Build(), TestThreat.Real);
        sim.AddPlayer(new PlayerState(0, 0, Keeper(), -48f, 0f));
        PlayerState st = sim.AddPlayer(new PlayerState(1, 0, Striker(), 44f, 5f));   // 팀 1 박스 안
        PlayerState gk = sim.AddPlayer(new PlayerState(2, 1, Keeper(), 48f, 0f));
        sim.AddPlayer(new PlayerState(3, 1, Striker(), 30f, 10f));
        sim.Kickoff();
        sim.Ball = BallRules.Kick(BallState.FreeAt(FieldBounds.HalfLength - 0.05f, 20f), 1f, 0f, 5f);   // 팀 1 골라인(+X) 밖으로 굴러 나간다
        sim.Tick(Dt);
        Assert.AreEqual(gk.PlayerId, sim.Ball.OwnerId, "그 골을 지키는 팀(1)의 GK가 공을 쥔다");

        int setupTicks = (int)(MatchTuning.GoalKickSetupSeconds / Dt);
        for (int i = 0; i < setupTicks - 5; i++)
        {
            sim.Tick(Dt);
            Assert.AreEqual(gk.PlayerId, sim.Ball.OwnerId, $"준비 시간 동안 들고 있다 (틱 {i})");
            Assert.IsFalse(MatchRules.IsInOwnPenaltyBox(st.X, st.Z, gk.AttackSign), $"상대는 박스 밖 (틱 {i})");
        }
        for (int i = 0; i < 60; i++) { sim.Tick(Dt); }
        Assert.AreNotEqual(gk.PlayerId, sim.Ball.OwnerId, "준비가 끝나면 배급한다");
    }

    [Test]
    public void 빗나간_슛_뒤_골킥도_준비_시간_동안_들고_있다가_배급하고_다음_소유자에게는_대기가_이어지지_않는다()
    {
        // 첫 주사위 0 = 퍼스트 터치(11-08 S3 1단계). 준비 시간 동안 GK가 공을 뺏으면 이 테스트가 보려는 골킥 장면이 안 나온다. 그 뒤 0.99 = 골 안 됨·조준은 빗나감
        var rolls = new RollQueue(new[] { 0f }, 0.99f);
        var sim = new MatchSimulation(rolls.Next, PlayerTreeBuilder.Build(), TestThreat.Real);
        sim.AddPlayer(new PlayerState(0, 0, Keeper(), -48f, 0f));
        sim.AddPlayer(new PlayerState(1, 0, Striker(), 44f, 0f));
        PlayerState gk = sim.AddPlayer(new PlayerState(2, 1, Keeper(), 48f, 0f));
        PlayerState cb = sim.AddPlayer(new PlayerState(3, 1, Striker(), 25f, 15f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 1, 44f, 0f);
        int caughtTick = -1;
        for (int i = 0; i < 300 && caughtTick < 0; i++)
        {
            sim.Tick(Dt);
            if (sim.Ball.Phase == BallPhase.Owned && sim.Ball.OwnerId == gk.PlayerId) { caughtTick = sim.TickCount; }
        }
        Assert.GreaterOrEqual(caughtTick, 0, "빗나간 슛 뒤 GK 소유(골킥)");

        int setupTicks = (int)(MatchTuning.GoalKickSetupSeconds / Dt);
        while (sim.TickCount < caughtTick + setupTicks - 5)
        {
            sim.Tick(Dt);
            Assert.AreEqual(gk.PlayerId, sim.Ball.OwnerId, "준비 시간 동안 들고 있다");
        }
        int releaseTick = -1;
        for (int i = 0; i < 60 && releaseTick < 0; i++)
        {
            sim.Tick(Dt);
            if (sim.Ball.Phase != BallPhase.Owned || sim.Ball.OwnerId != gk.PlayerId) { releaseTick = sim.TickCount; }
        }
        Assert.GreaterOrEqual(releaseTick, 0, "준비가 끝나면 배급한다");
        Assert.LessOrEqual(releaseTick - caughtTick, setupTicks + 5, "대기는 준비 시간만큼이다");

        // 다음 소유자(배급 받은 아군)는 자기 볼 끌기(테스트 스탯 0)만큼만 기다린다. 골킥 4초가 이어지면 대기 틱이 200으로 남는다
        for (int i = 0; i < 200 && !(sim.Ball.Phase == BallPhase.Owned && sim.Ball.OwnerId == cb.PlayerId); i++) { sim.Tick(Dt); }
        Assert.AreEqual(cb.PlayerId, sim.Ball.OwnerId, "배급을 받는다");
        int ownHoldTicks = (int)System.Math.Round(cb.Stats.HoldUp / Dt);
        Assert.LessOrEqual(sim.HoldUpTicksLeft, ownHoldTicks, "받은 선수의 대기는 자기 볼 끌기만큼이다");
    }

    [Test]
    public void GK가_없는_시뮬은_골라인_밖으로_나간_공을_예전처럼_골킥_자리의_자유_공으로_둔다()
    {
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.BuildLitmus(), TestThreat.Real);
        sim.AddPlayer(new PlayerState(0, 0, Striker(), -40f, -30f));
        sim.Kickoff();
        sim.Ball = BallRules.Kick(BallState.FreeAt(FieldBounds.HalfLength - 0.05f, 20f), 1f, 0f, 5f);
        sim.Tick(Dt);
        Assert.AreEqual(BallPhase.Free, sim.Ball.Phase);
        Assert.AreEqual(FieldBounds.HalfLength - MatchTuning.GoalKickOffset, sim.Ball.X, 1e-4f, "골킥 자리");
    }

    [Test]
    public void 공을_잡은_GK는_태클당하지_않는다()
    {
        // 규칙 12조. 주사위 0 = 시도하면 항상 성공인데도 GK 소유 중엔 시도 자체가 없어야 한다
        var sim = new MatchSimulation(() => 0f, new PassFlightTestsHelper.ChaseBall(), TestThreat.Real);
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
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.ChaseBall(), TestThreat.Real);   // 주사위 0.99 = 항상 실패
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
        var sim = new MatchSimulation(() => 0f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
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
        var shooter = new MatchSimulation(() => 0f, new PassFlightTestsHelper.ShootOnce(), TestThreat.Real);
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
        // 09-21 리뷰: 파링은 슛도 패스도 아닌 비행이라 잡기 분기가 없어 멈출 때까지(10-25 감속 1.2·4.4m/s = 약 3.7초) 아무도 못 잡았다
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.BuildLitmus(), TestThreat.Real);
        sim.AddPlayer(new PlayerState(0, 0, Striker(), 3f, 0f));   // 튕긴 공의 경로 위 3m 앞
        sim.Kickoff();
        sim.Ball = BallRules.Kick(BallState.FreeAt(0f, 0f), 1f, 0f, MatchTuning.ParrySpeed);

        int ticks = 0;
        while (sim.Ball.Phase != BallPhase.Owned && ticks < 100)
        {
            sim.Tick(Dt);
            ticks++;
        }
        Assert.AreEqual(0, sim.Ball.OwnerId, "경로 위 선수가 잡는다");
        Assert.Less(ticks, 100, "공이 멈추기(100틱) 전에 잡아야 한다");
    }

    [Test]
    public void GK는_박스_밖_자유_공을_쫓지_않는다()
    {
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.BuildLitmus(), TestThreat.Real);
        PlayerState gk = sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        sim.Kickoff();   // 공은 중앙(0,0), GK 박스는 x ≥ 36

        for (int i = 0; i < 100; i++) { sim.Tick(Dt); }

        Assert.AreEqual(50f, gk.X, "자리 유지");
        Assert.AreEqual(BallPhase.Free, sim.Ball.Phase);
    }

    [Test]
    public void 라인_밖으로_나간_공은_골킥_스로인_자리로_돌아온다()
    {
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.BuildLitmus(), TestThreat.Real);
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
        var sim = new MatchSimulation(() => 0.99f, PlayerTreeBuilder.Build(), TestThreat.Real);
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

        var kickers = new List<int>();
        var shots = new List<bool>();
        sim.BallKicked += report => { kickers.Add(report.KickerId); shots.Add(report.IsShot); };
        for (int i = 0; i < 100 && sim.PassCount == 0; i++) { sim.Tick(Dt); }
        Assert.AreEqual(1, sim.PassCount, "첫 행동은 패스");
        Assert.AreEqual(1, kickers.Count, "패스도 찼다 이벤트 1회(킥 애니)");
        Assert.AreEqual(st1.PlayerId, kickers[0], "찬 사람 = 키커");
        Assert.IsFalse(shots[0], "패스는 슛이 아니다");
        sim.Tick(Dt);
        Assert.IsTrue(st2.IsPassTarget, "옆·뒤 동료(ST2)에게");
        Assert.IsFalse(st1.IsKickoffTaker, "첫 킥 뒤엔 키커 표시가 풀린다");
    }

    [Test]
    public void 돌파_의도는_소유_때_드리블_성향으로_한_번_굴리고_패스하면_풀린다()
    {
        // 09-29. 주사위 0 → 성향이 0보다 크면 의도가 선다. 우리 진영 서드·드리블 성향 0이면 굴리지 않는다
        PlayerStats winger = Striker(); winger.Dribble = 0.9f;
        var sim = new MatchSimulation(() => 0f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerState w = sim.AddPlayer(new PlayerState(0, 0, winger, 0f, 20f));
        PlayerState mate = sim.AddPlayer(new PlayerState(1, 0, Striker(), 10f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, Keeper(), 48f, 0f));
        sim.Kickoff();
        sim.Ball = BallState.FreeAt(0.3f, 20f);
        sim.Tick(Dt);
        Assert.AreEqual(0, sim.Ball.OwnerId);
        Assert.IsTrue(w.WantsTakeOn, "중원에서 잡음 + 성향 0.9 + 주사위 0");
        Assert.AreEqual(1, sim.TakeOnCount);

        var own = new MatchSimulation(() => 0f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerState back = own.AddPlayer(new PlayerState(0, 0, winger, -30f, 20f));
        own.AddPlayer(new PlayerState(1, 1, Keeper(), 48f, 0f));
        own.Kickoff();
        own.Ball = BallState.FreeAt(-29.7f, 20f);
        own.Tick(Dt);
        Assert.AreEqual(0, own.Ball.OwnerId);
        Assert.IsFalse(back.WantsTakeOn, "우리 진영 서드에선 굴리지 않는다");

        var pass = new MatchSimulation(() => 0f, new PassFlightTestsHelper.PassOnce(0, 1), TestThreat.Real);
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
    public void 공이_라인_앞_10m_안에_오면_압박_거리_밖이어도_가장_가까운_뒷줄이_압박_0순위가_된다()
    {
        // 09-29 수비 D1 → 11-09 D2-b 라인 상대 구역. 팀 0 CB(압박 거리 6m)가 공에서 9m. 공은 팀 0 골라인에서 18m, 팀 1이 소유.
        // GK 없는 명부라 라인 L = 하한 8.5m(공과 최소 간격 10m가 L₀ 15.2를 8로 자르고 하한이 8.5로 올림) → 구역 = 18.5m 안
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerState near = sim.AddPlayer(new PlayerState(0, 0, CenterBack(), -43.5f, 0f));
        PlayerState far = sim.AddPlayer(new PlayerState(1, 0, CenterBack(), -45f, 15f));
        sim.AddPlayer(new PlayerState(2, 1, Striker(), -34.5f, 0f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 2, -34.5f, 0f);
        sim.Tick(Dt);
        Assert.AreEqual(0, near.PressRank, "가장 가까운 CB가 전진");
        Assert.AreEqual(int.MaxValue, far.PressRank, "다른 CB는 후보가 아니다(압박 거리 밖)");

        // 공이 골라인에서 40m면 L ≈ 25.9m(간격 30m는 안 걸림) → 구역 35.9m 밖. 압박 거리(6m) 밖 CB는 후보가 아니다
        var outside = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerState cbOut = outside.AddPlayer(new PlayerState(0, 0, CenterBack(), -30f, 0f));
        outside.AddPlayer(new PlayerState(2, 1, Striker(), -12.5f, 0f));
        outside.Kickoff();
        outside.Ball = BallRules.Own(outside.Ball, 2, -12.5f, 0f);
        outside.Tick(Dt);
        Assert.AreEqual(int.MaxValue, cbOut.PressRank, "라인 앞 10m 밖이라 17.5m 떨어진 CB는 후보가 아니다");
    }

    [Test]
    public void 전진_뒷줄은_더_가까운_압박자가_있어도_0순위라_뒤에서_쫓는_1명과_집게가_된다()
    {
        // 11-09 D2-b 결정 5: 라인 앞 운반자를 앞에서 막는 사람이 없어(5m 뒤에서 쫓는 CM에게 순위가 밀림) 슛 77%가 운반 중 라인 통과였다.
        // 팀 0 CB(깊이 9)와 CM(깊이 15.5, 공에서 2.5m, 압박 거리 안). 공은 골라인에서 18m 중앙. CM은 거리순 0순위, CB는 전진 뒷줄이라 역시 0순위
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerState back = sim.AddPlayer(new PlayerState(0, 0, CenterBack(), -43.5f, 0f));
        PlayerStats chaserStats = Midfielder();
        chaserStats.PressRange = 8f;
        PlayerState chaser = sim.AddPlayer(new PlayerState(1, 0, chaserStats, -37f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, Striker(), -34.5f, 0f));
        sim.SetTactics(0, new TeamTactics { PressStart = new[] { 1, 1, 1 } });
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 2, -34.5f, 0f);
        sim.Tick(Dt);
        Assert.IsTrue(back.IsBackLine);
        Assert.IsFalse(chaser.IsBackLine, "깊이 15.5는 9 + 6 밖");
        Assert.AreEqual(0, chaser.PressRank, "가까운 CM이 거리순 0순위");
        Assert.AreEqual(0, back.PressRank, "전진 뒷줄도 0순위(MaxPressers 1의 예외)");
    }

    [Test]
    public void 전진_구역_밖이면_뒷줄도_거리순이라_더_가까운_압박자가_있으면_0순위가_아니다()
    {
        // 11-09 D2-b 예외가 꺼지는지 잠금: 공이 골라인에서 40m(L ≈ 25.9, 구역 35.9 밖). CB와 CM 둘 다 압박 거리 안이고 CM이 더 가깝다 → CB는 거리순 1순위(MaxPressers 1 밖)
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerStats backStats = CenterBack();
        backStats.PressRange = 8f;
        PlayerState back = sim.AddPlayer(new PlayerState(0, 0, backStats, -18.5f, 0f, -43.5f, 0f));
        PlayerStats chaserStats = Midfielder();
        chaserStats.PressRange = 8f;
        PlayerState chaser = sim.AddPlayer(new PlayerState(1, 0, chaserStats, -14.5f, 0f, -30f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, Striker(), -12.5f, 0f));
        sim.SetTactics(0, new TeamTactics { PressStart = new[] { 1, 1, 1 } });
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 2, -12.5f, 0f);
        sim.Tick(Dt);
        Assert.IsTrue(back.IsBackLine, "전제: 수비 시 자리 깊이 9로 뒷줄");
        Assert.AreEqual(0, chaser.PressRank, "가까운 CM이 0순위");
        Assert.AreEqual(1, back.PressRank, "구역 밖이라 예외 없이 거리순 1순위");
    }

    [Test]
    public void 전진_구역은_이번_틱_라인_깊이로_판정한다()
    {
        // FillLineDepth가 FillPressEligible보다 먼저 돈다(순서 잠금). 첫 틱 공 깊이 18 → L 8.5·구역 18.5.
        // 공을 깊이 29.5로 옮기면 이번 틱 L = min(20.8, 19.5) = 19.5·구역 29.5라 안이다. 직전 틱 L로 판정하면 구역 18.5 밖이라 후보가 아니다
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerState back = sim.AddPlayer(new PlayerState(0, 0, CenterBack(), -43.5f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, Striker(), -34.5f, 0f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 2, -34.5f, 0f);
        sim.Tick(Dt);
        Assert.AreEqual(0, back.PressRank, "전제: 첫 틱 전진");
        sim.Ball = BallRules.Own(sim.Ball, 2, -23f, 0f);
        sim.Tick(Dt);
        Assert.AreEqual(0, back.PressRank, "이번 틱 L로 구역을 다시 잰다");
    }

    [Test]
    public void 수비_자리는_시뮬이_다이얼을_얹어_잰_팀_이동량과_공보다_골_쪽_상한을_전체_트리로_받는다()
    {
        // 11-10 재검증 R5 P1(D2 시뮬 배선). 순수 함수(LineRules·PositionRules)와 트리 Fake 테스트만으론 배선을 빼도 통과한다:
        // FillLineDepth(Δ·상한·DialedRestDepth) → FillSnapshots 대입(DefendLineShift·DefendDepthCap) → 트리 MoveToDefendHome이 ctx.DefendDepthCap을 넘김.
        // 팀 0(+X 공격)이 수비. CB 깊이 12.5 + 다이얼 20 × 0.3 = 18.5(가장 깊음 = restDepth), 앞 선수 깊이 42.5 + 다이얼 10 × 0.3 = 45.5.
        // 상대가 깊이 42.5(z 10, 골 중심에서 40m 밖이라 마크 없음)에서 소유. 상한 = 42.5 − 1.5 = 41이라 앞 선수는 Δ를 더하면 상한에 걸린다
        var sim = new MatchSimulation(() => 0.99f, PlayerTreeBuilder.Build(), TestThreat.Real);
        PlayerStats noPress = CenterBack();
        noPress.PressRange = 0f;
        PlayerStats backStats = noPress.Clone();
        backStats.LineHeight = 20f;
        PlayerStats frontStats = noPress.Clone();
        frontStats.RoleId = "CM";
        frontStats.VariantId = "cm_test";
        frontStats.LineHeight = 10f;
        PlayerState keeper = sim.AddPlayer(new PlayerState(0, 0, Keeper(), -48f, 0f));
        PlayerState back = sim.AddPlayer(new PlayerState(1, 0, backStats, -40f, 0f));
        PlayerState front = sim.AddPlayer(new PlayerState(2, 0, frontStats, -10f, 0f));
        sim.AddPlayer(new PlayerState(3, 1, Striker(), -10f, 10f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 3, -10f, 10f);

        sim.Tick(Dt);

        Assert.AreEqual(+1, back.AttackSign, "전제: 팀 0이 +X 공격");
        Assert.GreaterOrEqual(back.PressRank, MatchTuning.MaxPressers, "전제: 압박 안 함");
        Assert.GreaterOrEqual(front.PressRank, MatchTuning.MaxPressers, "전제: 압박 안 함");
        Assert.IsFalse(back.IsMarker, "전제: 마크 안 함");
        Assert.IsFalse(front.IsMarker, "전제: 마크 안 함");

        float ballDepth = LineRules.HomeDepth(-10f, +1);
        float keeperLineDepth = LineRules.HomeDepth(PositionRules.KeeperLineX(keeper.DefendHomeX, keeper.DefendHomeZ, +1, keeper.Stats.LineHeight), +1);
        float restDepth = System.Math.Min(LineRules.DialedHomeDepth(back.DefendHomeX, +1, backStats.LineHeight), LineRules.DialedHomeDepth(front.DefendHomeX, +1, frontStats.LineHeight));
        int defenseLine = sim.TacticsOf(0).DefenseLine;
        float shift = LineRules.TeamLineShift(ballDepth, defenseLine, keeperLineDepth, restDepth);
        float cap = LineRules.TeamStandDepthCap(ballDepth, keeperLineDepth);
        float shiftWithoutDial = LineRules.TeamLineShift(ballDepth, defenseLine, keeperLineDepth, LineRules.HomeDepth(back.DefendHomeX, +1));
        Assert.Greater(System.Math.Abs(shift - shiftWithoutDial), 1f, "전제: 다이얼을 빼면 Δ가 달라진다");
        Assert.Greater(LineRules.DialedHomeDepth(front.DefendHomeX, +1, frontStats.LineHeight) + shift, cap + 1f, "전제: 앞 선수는 상한에 걸린다");

        Assert.AreEqual(shift, back.DefendLineShift, 1e-4f, "Δ = L − 다이얼 얹은 뒷줄 깊이(FillLineDepth → 대입)");
        Assert.AreEqual(shift, front.DefendLineShift, 1e-4f, "같은 팀은 같은 Δ");
        Assert.AreEqual(cap, front.DefendDepthCap, 1e-4f, "상한 = 공 깊이 − GoalSideMargin(FillLineDepth → 대입)");

        (float x, float z) backStand = PositionRules.DefendStand(back.DefendHomeX, back.DefendHomeZ, +1, backStats, 10f, shift, cap, goalkeeper: false);
        (float x, float z) frontStand = PositionRules.DefendStand(front.DefendHomeX, front.DefendHomeZ, +1, frontStats, 10f, shift, cap, goalkeeper: false);
        Assert.AreEqual(backStand.x, back.MoveTargetX, 1e-4f, "가장 깊은 선수는 L에 선다");
        Assert.AreEqual(backStand.z, back.MoveTargetZ, 1e-4f);
        Assert.AreEqual(frontStand.x, front.MoveTargetX, 1e-4f, "앞 선수는 상한(공보다 골 쪽)에 선다(트리가 ctx.DefendDepthCap을 넘김)");
        Assert.AreEqual(LineRules.XAtDepth(cap, +1), front.MoveTargetX, 1e-4f, "상한 깊이 그대로");
    }

    [Test]
    public void 막힌_슛은_자유_공이_되고_슈터가_바로_되잡지_못한다()
    {
        // 09-29 수비 D3 + 리뷰 🔴: 슈터 (40,0), 수비수 1.5m 정면. 주사위 0 = 골 판정 성공이어도 블록(1.5m 곡선 0.65 > 0)이 먼저
        var sim = new MatchSimulation(() => 0f, new PassFlightTestsHelper.ShootOnce(), TestThreat.Real);
        sim.AddPlayer(new PlayerState(0, 0, Striker(), 40f, 0f));
        sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, CenterBack(), 41.5f, -0.3f));   // GK 중앙이라 구석 목표 +3.16 선(블록은 오차 전 목표선으로 본다). 그 선에서 0.7m라 블록 폭 1.3m 안
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
    public void 막힌_슛은_블록_주사위_하나만_쓴다()
    {
        // 11-09 B 직렬: 블록이 조준보다 먼저라 막힌 슛은 조준 2개·골 1개 주사위를 안 쓴다(주사위 순서가 결과에 안 갈리게).
        // 슈터 (40,0), GK 중앙이라 구석 목표 +3.16, 수비수가 그 선 1.5m 앞. 주사위 = 소유 때 퍼스트 터치 0.99(실패, 슛 노드는 준비 시간과 무관하게 찬다) → 블록 0 < 곡선 0.65(2m 안).
        // 막히지 않았다면 조준 2 + 골 1을 더 써서 남는 수가 0이 된다
        var rolls = new RollQueue(new[] { 0.99f, 0f, 0.5f, 0.5f, 0.5f });
        var sim = new MatchSimulation(rolls.Next, new PassFlightTestsHelper.ShootOnce(), TestThreat.Real);
        sim.AddPlayer(new PlayerState(0, 0, Striker(), 40f, 0f));
        sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, CenterBack(), 41.5f, 0.4f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 40f, 0f);
        var outcomes = new List<ShotOutcome>();
        sim.ShotResolved += r => outcomes.Add(r.Outcome);

        sim.Tick(Dt);

        Assert.AreEqual(1, outcomes.Count);
        Assert.AreEqual(ShotOutcome.Blocked, outcomes[0]);
        Assert.AreEqual(3, rolls.Remaining, "퍼스트 터치 1 + 블록 1. 슛 쪽은 블록 주사위 1개만 썼다");
    }

    [Test]
    public void 당긴_세이브_슛은_전체_트리에서_GK_접촉으로_끝나고_골라인_순간이동이_아니다()
    {
        // 11-09 B3 당김의 실행 쪽. GK z +3 → 조준 목표 반대쪽 구석 −3.16, 조준 roll (0, ·) → 오차 0, 골 roll 0.99(노골 = 세이브).
        // 가로 6m는 못 가니 찬 순간 조준이 GK 닿는 경계로 당겨진다. 전체 트리(⑨b)라 GK가 그 점으로 다이빙해 발 반경에 닿아야 한다.
        // 골라인 세이브(ResolveSaveAtLine)는 GK를 공 자리로 옮기므로 마감 틱의 GK 이동이 크다. 접촉 세이브는 그 틱 이동 전에 판정해 0이다
        var sim = new MatchSimulation(new RollQueue(new[] { 0f, 0.5f, 0.99f, 0.5f, 0.5f, 0.5f }).Next, PlayerTreeBuilder.Build(), TestThreat.Real) { ResetAfterEveryShot = true };
        PlayerState striker = sim.AddPlayer(new PlayerState(0, 0, Striker(), 30f, 0f));
        PlayerState keeper = sim.AddPlayer(new PlayerState(1, 1, Keeper(), 50f, 3f));
        float aimZ = float.NaN;
        float prevKeeperX = keeper.X;
        float prevKeeperZ = keeper.Z;
        float jumpAtFinish = float.NaN;
        var reports = new List<ShotReport>();
        sim.BallKicked += k =>
        {
            if (!k.IsShot) { return; }
            aimZ = striker.Z + k.DirZ;
        };
        sim.ShotResolved += r =>
        {
            reports.Add(r);
            float dx = keeper.X - prevKeeperX;
            float dz = keeper.Z - prevKeeperZ;
            jumpAtFinish = (float)System.Math.Sqrt(dx * dx + dz * dz);
        };
        sim.Kickoff();
        for (int i = 0; i < 20000 && reports.Count == 0; i++)
        {
            prevKeeperX = keeper.X;
            prevKeeperZ = keeper.Z;
            sim.Tick(Dt);
        }

        Assert.AreEqual(1, reports.Count, "슛이 끝나야 한다");
        Assert.Greater(aimZ, -(FieldBounds.GoalHalfWidth - MatchTuning.AimInset) + 0.5f, "전제: 조준이 GK 쪽으로 당겨졌다");
        Assert.IsTrue(MatchRules.IsOnTarget(aimZ), "당긴 점도 골문 안");
        Assert.IsTrue(reports[0].Outcome == ShotOutcome.Caught || reports[0].Outcome == ShotOutcome.Parried, "세이브");
        Assert.Less(jumpAtFinish, 0.2f, "마감 틱에 GK가 공 자리로 순간이동하지 않았다(접촉 세이브)");
    }

    [Test]
    public void 라인_앞_전진은_역할_이름이_아니라_뒷줄이_기준이고_측면_공엔_가까운_FB가_나간다()
    {
        // 11-06 D1: StepOutCenterBack(RoleId == "CB")을 LineRules 뒷줄로 일반화. 팀 0 CB(깊이 9)와 DM(깊이 16.5, 밴드 6m 밖)이 있고 공은 골라인에서 18m 중앙(전진 구역 안).
        // DM이 공에서 1.5m로 더 가깝지만 뒷줄이 아니라 전진 후보가 아니고(압박 시작 0이라 거리 후보도 아님), 13.5m 떨어진 CB가 1순위
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerState back = sim.AddPlayer(new PlayerState(0, 0, CenterBack(), -43.5f, 10f));
        PlayerState dm = sim.AddPlayer(new PlayerState(1, 0, Midfielder(), -36f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, Striker(), -34.5f, 0f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 2, -34.5f, 0f);
        sim.Tick(Dt);
        Assert.IsTrue(back.IsBackLine);
        Assert.IsFalse(dm.IsBackLine, "깊이 16.5는 9 + 6 밖");
        Assert.AreEqual(0, back.PressRank, "뒷줄이 전진");
        Assert.AreEqual(int.MaxValue, dm.PressRank, "수미는 더 가까워도 후보가 아니다");

        // 4-4-2 FB(−40, z 22)도 뒷줄이다. 11-07엔 구역 폭을 중앙 ±10.08로 좁혔지만, 11-09 D2-b(결정 4)에서 박스 반폭(±20.16)으로 되돌렸다:
        // 라인이 오른 D2에선 측면 운반자를 아무도 앞에서 안 막았다. 측면 공(z 19)엔 가까운 FB가, 중앙 공(z 5)엔 CB가, 박스 폭 밖(z 25)엔 아무도 안 나간다
        var wide = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
        PlayerState cb = wide.AddPlayer(new PlayerState(0, 0, CenterBack(), -41f, 7f));
        PlayerState fb = wide.AddPlayer(new PlayerState(1, 0, FullBack(), -40f, 22f));
        wide.AddPlayer(new PlayerState(2, 1, Striker(), -36f, 19f));
        wide.Kickoff();
        wide.Ball = BallRules.Own(wide.Ball, 2, -36f, 19f);
        wide.Tick(Dt);
        Assert.IsTrue(fb.IsBackLine, "깊이 12.5는 11.5 + 6 안");
        Assert.AreEqual(0, fb.PressRank, "측면(z 19) 공엔 가까운 FB가 전진");
        Assert.AreEqual(int.MaxValue, cb.PressRank);

        wide.Ball = BallRules.Own(wide.Ball, 2, -36f, 5f);
        wide.Tick(Dt);
        Assert.AreEqual(0, cb.PressRank, "중앙(z 5) 공엔 가까운 CB가 전진");
        Assert.AreEqual(int.MaxValue, fb.PressRank);

        wide.Ball = BallRules.Own(wide.Ball, 2, -36f, 25f);
        wide.Tick(Dt);
        Assert.AreEqual(int.MaxValue, fb.PressRank, "박스 폭(±20.16) 밖은 구역 밖");
        Assert.AreEqual(int.MaxValue, cb.PressRank);
    }

    [Test]
    public void 팀_1도_라인_앞에서_가장_가까운_뒷줄이_압박_0순위가_된다()
    {
        // 09-30 리뷰: D1 대칭. 팀 1 골라인은 x = +52.5. 공은 거기서 18m, 팀 0이 소유
        var sim = new MatchSimulation(() => 0.99f, new PassFlightTestsHelper.NoOp(), TestThreat.Real);
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
        var sim = new MatchSimulation(() => 0f, new PassFlightTestsHelper.ShootOnce(), TestThreat.Real);
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
        var sim = new MatchSimulation(() => 0f, new PassFlightTestsHelper.ShootOnce(), TestThreat.Real);
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
        var sim = new MatchSimulation(() => roll, tree, TestThreat.Real);
        PlayerState shooter = sim.AddPlayer(new PlayerState(0, 0, Striker(), -10f, 0f));
        sim.AddPlayer(new PlayerState(1, 1, Keeper(), 52f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, Striker(), 30f, 25f));   // 후반 킥오프를 받을 팀 1 필드 선수. 슛 라인에서 멀리
        sim.KickoffBy(0);
        int halfTimeTick = MatchClock.HalfTimeTick(AddedTime.None, MatchLength.Compressed);
        for (int i = 0; i < halfTimeTick - 2; i++) { sim.Tick(Dt); }

        shooter.X = 25f;   // 골 중심 27.5m. 11-08 사거리 30 전엔 20(32.5m)이었다
        shooter.Z = 0f;
        sim.Ball = BallRules.Own(sim.Ball, 0, 25f, 0f);
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

    // 종료 한 틱 전에 슛을 쏘게 해 두고 종료 틱까지 돌린다. 후반이라 팀 0은 -X로 공격한다. 슈터 (-20,0)에서 골까지 32.5m라 공은 1초 넘게 난다
    private static (MatchSimulation sim, List<ShotOutcome> outcomes) ShotAtFullTime(float roll)
    {
        var tree = new PassFlightTestsHelper.ShootWhenArmed();
        var sim = new MatchSimulation(() => roll, tree, TestThreat.Real);
        PlayerState shooter = sim.AddPlayer(new PlayerState(0, 0, Striker(), -10f, 0f));
        sim.AddPlayer(new PlayerState(1, 1, Keeper(), 52f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, Striker(), 30f, 25f));
        sim.KickoffBy(0);
        for (int i = 0; i < sim.TotalTicks - 2; i++) { sim.Tick(Dt); }
        Assert.IsTrue(sim.SidesSwitched, "후반이다");

        shooter.X = -25f;   // 후반이라 −X 골. 골 중심 27.5m(11-08 사거리 30)
        shooter.Z = 0f;
        sim.Ball = BallRules.Own(sim.Ball, 0, -25f, 0f);
        tree.Armed = true;
        var outcomes = new List<ShotOutcome>();
        sim.ShotResolved += r => outcomes.Add(r.Outcome);
        sim.Tick(Dt);   // 종료 한 틱 전: 슛
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase, "종료 직전에 슛이 날아가는 중");
        Assert.IsEmpty(outcomes);
        sim.Tick(Dt);   // 종료 틱
        Assert.AreEqual(sim.TotalTicks, sim.TickCount);
        return (sim, outcomes);
    }

    [Test]
    public void 종료_휘슬_순간_날아가던_골이_될_슛은_골로_남는다()
    {
        // 09-30 하프타임 수정 중 발견: 경기는 MatchTicks에서 멈추는데 그때 날아가던 슛은 결과 보고가 없고 골이 될 슛이면 골도 사라졌다
        (MatchSimulation sim, List<ShotOutcome> outcomes) = ShotAtFullTime(0f);   // 주사위 0 = 골 판정 성공, 조준은 골문 안
        CollectionAssert.AreEqual(new[] { ShotOutcome.Goal }, outcomes);
        Assert.AreEqual(1, sim.HomeGoals);
    }

    [Test]
    public void 종료_휘슬_순간_날아가던_골이_아닌_슛은_빗나감으로_마감한다()
    {
        (MatchSimulation sim, List<ShotOutcome> outcomes) = ShotAtFullTime(0.99f);   // 주사위 0.99 = 골 판정 실패
        CollectionAssert.AreEqual(new[] { ShotOutcome.Missed }, outcomes);
        Assert.AreEqual(0, sim.HomeGoals);
    }

    // 후반에 골 앞 4.5m에서 쏜다. armTick = 슛을 쏘는 틱 번호. 종료 틱까지 돌리고, 슛 결과가 처음 나온 틱 번호를 같이 돌려준다
    private static (MatchSimulation sim, List<ShotOutcome> outcomes, int resolvedTick) CloseRangeGoal(int armTick)
    {
        var tree = new PassFlightTestsHelper.ShootWhenArmed();
        var sim = new MatchSimulation(() => 0f, tree, TestThreat.Real);
        PlayerState shooter = sim.AddPlayer(new PlayerState(0, 0, Striker(), -10f, 0f));
        sim.AddPlayer(new PlayerState(1, 1, Keeper(), 52f, 0f));
        sim.AddPlayer(new PlayerState(2, 1, Striker(), 30f, 25f));
        sim.KickoffBy(0);
        for (int i = 0; i < armTick - 1; i++) { sim.Tick(Dt); }

        shooter.X = -48f;
        shooter.Z = 0f;
        sim.Ball = BallRules.Own(sim.Ball, 0, -48f, 0f);
        tree.Armed = true;
        var outcomes = new List<ShotOutcome>();
        sim.ShotResolved += r => outcomes.Add(r.Outcome);
        int resolvedTick = -1;
        while (sim.TickCount < sim.TotalTicks)
        {
            sim.Tick(Dt);
            if (resolvedTick == -1 && outcomes.Count > 0) { resolvedTick = sim.TickCount; }
        }
        return (sim, outcomes, resolvedTick);
    }

    [Test]
    public void 종료_틱에_골라인을_넘은_슛은_한_번만_센다()
    {
        // 종료 마감은 그 틱의 평소 처리가 끝난 뒤에도 날아가는 슛에만 걸린다. 종료 틱에 골라인에서 정상 판정된 슛을 한 번 더 세면 안 된다.
        // 먼저 쏜 틱부터 골라인까지 몇 틱인지 재고, 골라인 통과가 정확히 종료 틱이 되게 다시 쏜다(10-06 리뷰: 처음엔 종료 55틱 전에 골이 나서 경계를 안 봤다)
        int endTick = MatchLength.Default.RegulationTicks;   // 시뮬을 만들기 전에 정하는 틱. 이 장면의 시뮬은 기본 길이에 추가시간이 없다
        int earlyArmTick = endTick - 200;
        int flightTicks = CloseRangeGoal(earlyArmTick).resolvedTick - earlyArmTick;
        Assert.Greater(flightTicks, 0);

        (MatchSimulation sim, List<ShotOutcome> outcomes, int resolvedTick) = CloseRangeGoal(endTick - flightTicks);
        Assert.AreEqual(endTick, resolvedTick, "골라인 통과가 종료 틱이다");
        CollectionAssert.AreEqual(new[] { ShotOutcome.Goal }, outcomes);
        Assert.AreEqual(1, sim.HomeGoals);
    }

    [Test]
    public void 슛마다_찼다_이벤트가_슈터_번호로_온다()
    {
        // 화면 킥 애니용 BallKicked(10-21). 리트머스는 패스가 없어 슛 수 = 킥 수이고, 막힌 슛도 찬 것으로 센다
        float[] sequence = { 0.10f,0.5f, 0.50f,0.5f,0.50f, 0.90f,0.5f,0.20f, 0.35f,0.5f,0.70f, 0.05f,0.5f, 0.60f,0.5f,0.99f, 0.40f,0.5f,0.30f, 0.80f,0.5f,0.15f, 0.55f,0.5f,0.25f, 0.65f,0.5f,0.45f, 0.75f,0.5f,0.33f, 0.66f,0.5f,0.11f };
        MatchSimulation sim = LitmusMatch(new RollQueue(sequence));
        var kickers = new List<int>();
        int shotFlags = 0;
        var dirX = new List<float>();
        sim.BallKicked += report => { kickers.Add(report.KickerId); dirX.Add(report.DirX); if (report.IsShot) { shotFlags++; } };

        RunShots(sim, 10);

        Assert.AreEqual(10, kickers.Count);
        Assert.AreEqual(10, shotFlags, "전부 슛");
        for (int i = 0; i < dirX.Count; i++) { Assert.Greater(dirX[i], 0f, $"{i}번째 슛 방향은 +X 골 쪽(찬 순간 공 속도가 0이라 방향은 보고가 들고 있어야 한다)"); }
        for (int i = 0; i < kickers.Count; i++) { Assert.AreEqual(0, kickers[i], "리트머스에서 차는 건 스트라이커(0)뿐"); }
    }

    // ── 슛 결정 시간(11-08 S3 1단계): 소유 때 퍼스트 터치를 한 번 굴리고, 못 얻으면 준비 시간 동안 슛을 판정에서 뺀다

    private static MatchSimulation StrikerOwnsAt(System.Func<float> roll, float strikerX, out PlayerState striker)
    {
        var sim = new MatchSimulation(roll, PlayerTreeBuilder.Build(), TestThreat.Real);
        striker = sim.AddPlayer(new PlayerState(0, 0, Striker(), strikerX, 0f));
        sim.AddPlayer(new PlayerState(1, 1, Keeper(), FieldBounds.HalfLength - 0.5f, 0f));
        sim.Kickoff();
        striker.X = strikerX;   // 킥오프가 자리를 옮겼어도 잡는 자리를 못 박는다
        striker.Z = 0f;
        sim.Ball = BallRules.Own(sim.Ball, 0, strikerX, 0f);
        return sim;
    }

    [Test]
    public void 퍼스트_터치를_못_얻으면_준비_시간_전엔_슛을_차지_않는다()
    {
        // 골 중심 17.5m(16~22 구간 33.9%). 주사위 0.99 = 퍼스트 터치 실패
        MatchSimulation sim = StrikerOwnsAt(() => 0.99f, 35f, out PlayerState striker);
        int prep = ShotDecisionRules.PrepTicks(MatchTuning.ShotPrepSeconds);
        int shotTick = -1;
        int tick = 0;
        sim.BallKicked += k =>
        {
            if (k.IsShot && shotTick < 0) { shotTick = tick; }
        };
        sim.Tick(Dt);
        Assert.IsFalse(striker.ShotReady, "소유한 틱부터 준비 중");
        Assert.AreEqual(0, sim.FirstTouchReadyCount);
        for (tick = 1; tick < prep + 200 && shotTick < 0; tick++) { sim.Tick(Dt); }
        Assert.GreaterOrEqual(shotTick, 0, "준비 시간이 끝나면 찬다(타이머가 안 풀리는 회귀를 잡는다)");
        Assert.GreaterOrEqual(shotTick, prep, $"슛은 소유한 틱 포함 {prep}틱 뒤에만");
    }

    [Test]
    public void 퍼스트_터치를_얻으면_소유한_틱부터_슛을_판정에_넣는다()
    {
        MatchSimulation sim = StrikerOwnsAt(() => 0f, 35f, out PlayerState striker);
        sim.Tick(Dt);
        Assert.IsTrue(striker.ShotReady);
        Assert.AreEqual(1, sim.FirstTouchReadyCount);
    }

    [Test]
    public void 퍼스트_터치를_얻은_소유는_볼_끌기가_슛을_붙잡지_않는다()
    {
        // 리뷰 🔴(11-08): 볼 끌기 1초(타깃맨)가 퍼스트 터치 슛을 다시 막으면 판정(바로 찬다)과 실행이 어긋난다. 골 중심 8.5m
        MatchSimulation sim = StrikerOwnsAt(() => 0f, 44f, out PlayerState striker);
        striker.Stats.HoldUp = 1f;
        int holdUpTicks = (int)System.Math.Round(1f / MatchTuning.FixedStep);
        int shotTick = -1;
        int tick = 0;
        sim.BallKicked += k =>
        {
            if (k.IsShot && shotTick < 0) { shotTick = tick; }
        };
        for (tick = 0; tick < holdUpTicks && shotTick < 0; tick++) { sim.Tick(Dt); }
        Assert.GreaterOrEqual(shotTick, 0, "볼 끌기 1초가 끝나기 전에 찬다");
    }

    [Test]
    public void 사거리_밖에서_잡으면_주사위를_안_쓰고_준비_시간만_건다()
    {
        // 골 중심 52.5m. 주사위 수열이 비어 있어 굴리면 예외
        var rolls = new RollQueue(new float[0]);
        MatchSimulation sim = StrikerOwnsAt(rolls.Next, 0f, out PlayerState striker);
        sim.Tick(Dt);
        Assert.IsFalse(striker.ShotReady);
        Assert.AreEqual(0, sim.FirstTouchReadyCount);
    }

    // ── 구르는 공 경합(11-10 A1, 설계-A-공과-받기 3-1): 착지점을 지난 패스는 루즈볼이라 양 팀 최근접 1명씩이 쫓는다. 소유 팀 표시는 찬 팀 그대로

    // 지정 선수가 공을 가지면 한 번 지정 착지점으로 패스하고, 나머지 틱·선수는 안쪽 트리(가짜 트리 또는 실제 트리)를 돈다
    private sealed class PassToPointThen : BehaviorNode
    {
        private readonly int passer;
        private readonly int receiver;
        private readonly float landingX;
        private readonly float landingZ;
        private readonly BehaviorNode inner;
        private bool done;

        public PassToPointThen(int passer, int receiver, float landingX, float landingZ, BehaviorNode inner)
        {
            this.passer = passer;
            this.receiver = receiver;
            this.landingX = landingX;
            this.landingZ = landingZ;
            this.inner = inner;
        }

        public override NodeState Tick(IPlayerContext ctx)
        {
            if (done || ctx.PlayerId != passer || !ctx.OwnsBall) { return inner.Tick(ctx); }
            ctx.Pass(receiver, landingX, landingZ);
            done = true;
            return NodeState.Success;
        }
    }

    private const float LooseLandingX = 12f;

    // 팀 0 패서(0번, 원점)가 (12, 0)으로 찬다. 리시버(1번)는 착지점에서 25m 옆이라 공이 착지점을 지나 구른다.
    // 팀 1 수비(3번)는 착지점 너머 (30, −6), 압박 거리 6m 밖이고 팀 압박 시작은 기본값(안 감)
    private static MatchSimulation LooseBallScene(BehaviorNode inner, out PlayerState receiver, out PlayerState defender)
    {
        var sim = new MatchSimulation(() => 0.5f, new PassToPointThen(0, 1, LooseLandingX, 0f, inner), TestThreat.Real);
        sim.AddPlayer(new PlayerState(0, 0, Midfielder(), 0f, 0f));
        PlayerStats slow = Midfielder();
        slow.Speed = 30;
        receiver = sim.AddPlayer(new PlayerState(1, 0, slow, LooseLandingX, 25f));
        defender = sim.AddPlayer(new PlayerState(3, 1, Midfielder(), 30f, -6f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 0f);
        return sim;
    }

    // 공이 x에 닿을 때까지(최대 limit틱) 돌린다. 그 전에 누가 잡거나 공이 서면 실패
    private static void TickUntilBallX(MatchSimulation sim, float x, int limit = 400)
    {
        for (int i = 0; i < limit && sim.Ball.X < x; i++)
        {
            sim.Tick(Dt);
        }
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase, $"공이 x {x}까지 굴러가는 패스여야 한다(지금 {sim.Ball.X:0.00})");
        Assert.GreaterOrEqual(sim.Ball.X, x);
    }

    private static int LooseChaserOf(MatchSimulation sim, int team)
    {
        int found = -1;
        foreach (PlayerState p in sim.Players)
        {
            if (p.Team != team || !p.IsLooseBallChaser) { continue; }
            Assert.AreEqual(-1, found, $"팀 {team} 루즈볼 추격자는 1명");
            found = p.PlayerId;
        }
        return found;
    }

    [Test]
    public void 착지점을_지난_패스는_양_팀_최근접이_루즈볼_추격자가_된다()
    {
        // 11-09 receive probe: 착지점을 지난 패스는 멈출 때까지 passInFlight라 상대는 압박 거리 안일 때만 쫓았다(아무도 안 쫓는 구르는 공 판당 142초)
        MatchSimulation sim = LooseBallScene(PlayerTreeBuilder.Build(), out PlayerState receiver, out PlayerState defender);
        TickUntilBallX(sim, LooseLandingX + 2f);

        Assert.AreEqual(0, sim.OwnerTeam(), "소유 팀 표시는 찬 팀 그대로(블록 형태 보존, 09-28)");
        PlayerState passer = sim.Players[0];
        Assert.AreEqual(passer.PlayerId, LooseChaserOf(sim, 0), "찬 팀은 공에 가장 가까운 필드 선수(패서)");
        Assert.AreEqual(defender.PlayerId, LooseChaserOf(sim, 1), "상대 팀도 최근접 1명");
        Assert.AreEqual(MoveMode.Run, passer.MoveMode, "찬 팀 추격자는 공으로 달린다");
        Assert.AreEqual(MoveMode.Run, defender.MoveMode, "상대 추격자는 압박 거리 밖이어도 공으로 달린다");
        Assert.IsFalse(receiver.IsLooseBallChaser);
    }

    [Test]
    public void 착지점_전에는_루즈볼_추격자가_없고_리시버만_마중한다()
    {
        MatchSimulation sim = LooseBallScene(new PassFlightTestsHelper.NoOp(), out PlayerState receiver, out PlayerState defender);
        TickUntilBallX(sim, LooseLandingX - 6f);

        Assert.AreEqual(-1, LooseChaserOf(sim, 0), "착지점 전엔 찬 팀 추격자 없음");
        Assert.AreEqual(-1, LooseChaserOf(sim, 1), "착지점 전엔 상대 추격자 없음");
        Assert.IsTrue(receiver.IsPassTarget, "리시버는 착지점으로 마중");
        Assert.AreEqual(int.MaxValue, defender.PressRank, "압박 거리 밖 수비는 순위 밖(⑨ 자리)");
    }

    [Test]
    public void 착지점을_지나면_리시버_마중과_마크_리시버_예외가_꺼진다()
    {
        // 구멍 3·13: 착지점을 지난 뒤에도 마커가 빈 착지점을 보면 2초 안팎 마커 1명이 빈 자리에 선다.
        // 팀 1 패서(2번)가 (−15, 0)에서 (−30, 0)으로 찬다. 리시버(3번)는 (−32, 12)에 서 있다.
        // 팀 0 뒷줄 CB 2명: 공 길에 가까운 1번은 라인 앞 전진(압박)이라 마커가 아니고, 0번이 마커
        var sim = new MatchSimulation(() => 0.5f, new PassToPointThen(2, 3, -30f, 0f, new PassFlightTestsHelper.NoOp()), TestThreat.Real);
        PlayerState marker = sim.AddPlayer(new PlayerState(0, 0, CenterBack(), -42f, 15f));
        sim.AddPlayer(new PlayerState(1, 0, CenterBack(), -40f, -5f));
        sim.AddPlayer(new PlayerState(2, 1, Midfielder(), -15f, 0f));
        PlayerState receiver = sim.AddPlayer(new PlayerState(3, 1, Midfielder(), -32f, 12f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 2, -15f, 0f);

        for (int i = 0; i < 400 && sim.Ball.X > -22f; i++) { sim.Tick(Dt); }
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase);
        Assert.IsTrue(receiver.IsPassTarget, "전제: 착지점 전엔 리시버가 마중");
        Assert.IsTrue(marker.IsMarker, "전제: 뒷줄 CB가 리시버를 맡는다");
        (float x, float z) atLanding = MarkRules.MarkPoint(-30f, 0f, 0f, 0f, 0f, marker.AttackSign, MatchTuning.MarkDistance);
        Assert.AreEqual(atLanding.z, marker.MarkZ, 1e-3f, "착지점 전엔 착지점 기준으로 선다");

        for (int i = 0; i < 400 && sim.Ball.X > -32f; i++) { sim.Tick(Dt); }
        Assert.AreEqual(BallPhase.Flight, sim.Ball.Phase, "착지점을 2m 지나 구르는 중");
        Assert.AreEqual(1, sim.OwnerTeam(), "소유 팀 표시는 찬 팀 그대로");
        Assert.IsFalse(receiver.IsPassTarget, "착지점을 지나면 리시버 마중이 꺼진다");
        Assert.IsTrue(marker.IsMarker, "마크는 계속(상대 소유 표시)");
        (float x, float z) atReceiver = MarkRules.MarkPoint(receiver.X, receiver.Z, 0f, 0f, 0f, marker.AttackSign, MatchTuning.MarkDistance);
        Assert.AreEqual(atReceiver.z, marker.MarkZ, 1e-3f, "리시버의 지금 자리 기준으로 선다");
        Assert.LessOrEqual(LineRules.HomeDepth(marker.MarkX, marker.AttackSign), marker.DefendDepthCap + 1e-4f, "마크 상한(공보다 골 쪽)을 다시 받는다");
    }

    [Test]
    public void 착지점_판정은_잡기와_추격이_같은_틱_값을_본다()
    {
        // 구멍 8: 잡기(TryCapture의 흘려 보내기)와 추격(루즈볼)이 같은 내적 판정을 본다. 착지점을 지나는 틱에 둘이 같이 바뀐다
        MatchSimulation sim = LooseBallScene(new PassFlightTestsHelper.NoOp(), out PlayerState receiver, out _);
        sim.Tick(Dt);   // 킥
        int flips = 0;
        bool wasLive = true;
        for (int i = 0; i < 400 && sim.Ball.Phase == BallPhase.Flight; i++)
        {
            sim.Tick(Dt);
            bool letThrough = sim.LetThroughTeamForTests != -1;
            bool chased = LooseChaserOf(sim, 1) != -1;
            Assert.AreEqual(letThrough, !chased, $"{i}틱: 흘려 보내기와 루즈볼 추격은 같은 판정의 반대");
            Assert.AreEqual(letThrough, receiver.IsPassTarget, $"{i}틱: 리시버 마중도 같은 판정");
            if (wasLive && !letThrough) { flips++; }
            wasLive = letThrough;
        }
        Assert.AreEqual(1, flips, "착지점을 한 번 지난다");
    }

    [Test]
    public void 루즈볼_추격자는_GK와_얼음_선수를_뺀다()
    {
        // 기존 ⑩ 후보 규칙(09-23 R3·태클 실패 얼음)을 착지점을 지난 공에도 그대로 쓴다. 팀 1: GK가 가장 가깝고, 그다음 얼은 선수, 맨 뒤 필드 선수
        var sim = new MatchSimulation(() => 0.5f, new PassToPointThen(0, 1, LooseLandingX, 0f, new PassFlightTestsHelper.NoOp()), TestThreat.Real);
        sim.AddPlayer(new PlayerState(0, 0, Midfielder(), 0f, 0f));
        sim.AddPlayer(new PlayerState(1, 0, Midfielder(), LooseLandingX, 25f));
        PlayerState keeper = sim.AddPlayer(new PlayerState(3, 1, Keeper(), 16f, 3f));
        PlayerState frozen = sim.AddPlayer(new PlayerState(4, 1, Midfielder(), 16f, -4f));
        PlayerState field = sim.AddPlayer(new PlayerState(5, 1, Midfielder(), 26f, 8f));
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 0f);
        frozen.FrozenTicks = 10000;

        TickUntilBallX(sim, LooseLandingX + 2f);
        Assert.IsFalse(keeper.IsLooseBallChaser, "GK는 루즈볼 후보가 아니다(출격은 트리가 따로 본다)");
        Assert.IsFalse(frozen.IsLooseBallChaser, "얼은 선수는 후보가 아니다");
        Assert.IsTrue(field.IsLooseBallChaser, "남은 필드 선수가 쫓는다");
    }

    [Test]
    public void 루즈볼_추격자가_있으면_그_선수가_팀의_압박_1순위이고_다른_압박자는_없다()
    {
        // 결정 2(구멍 9): 압박 거리 밖 최근접(추격자)과 압박 거리 안 다른 선수가 같은 공으로 2명 가지 않게 한 사람으로 맞춘다.
        // 팀 1: 가까운 A(압박 거리 6m, 착지점 2m 뒤 공에서 7m)와 먼 B(압박 거리 13m, 10.2m). 압박 시작 표준
        var sim = new MatchSimulation(() => 0.5f, new PassToPointThen(0, 1, LooseLandingX, 0f, new PassFlightTestsHelper.NoOp()), TestThreat.Real);
        sim.AddPlayer(new PlayerState(0, 0, Midfielder(), 0f, 0f));
        sim.AddPlayer(new PlayerState(1, 0, Midfielder(), LooseLandingX, 25f));
        PlayerState near = sim.AddPlayer(new PlayerState(3, 1, Midfielder(), 21f, 0.5f));
        PlayerStats wide = Midfielder();
        wide.PressRange = 13f;
        PlayerState far = sim.AddPlayer(new PlayerState(4, 1, wide, LooseLandingX, -10f));
        sim.SetTactics(1, new TeamTactics { PressStart = new[] { 1, 1, 1 } });
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, 0f, 0f);

        TickUntilBallX(sim, LooseLandingX + 2f);
        Assert.IsTrue(near.IsLooseBallChaser, "전제: 공에 가장 가까운 A가 추격자");
        Assert.AreEqual(0, near.PressRank, "추격자가 압박 1순위");
        Assert.GreaterOrEqual(far.PressRank, MatchTuning.MaxPressers, "압박 거리 안이어도 B는 압박하러 가지 않는다");
    }
}
