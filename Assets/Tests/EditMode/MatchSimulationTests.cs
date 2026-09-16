using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Placement;
using NUnit.Framework;

// 경기 루프 검증(엔진 없음). 리트머스: ST 1 vs GK 1, 슛 10회의 결과가 각각 MatchRules.Resolve(p_i, roll_i)와 같고
// 같은 주사위 수열이면 같은 경기가 나온다. 그 외 잡기·라인 아웃·GK 출격 한계.
public class MatchSimulationTests
{
    private const float Dt = 0.02f;

    private static PlayerStats Striker()
    {
        return new PlayerStats { RoleId = "ST", Speed = 60, Stamina = 45, Pass = 40, Shot = 90, Tackle = 20, Positioning = 45, PushUp = 25f, PressRange = 8f, ShotBias = 0.3f, PassLength = 15f };
    }

    private static PlayerStats Keeper()
    {
        return new PlayerStats { RoleId = "GK", Speed = 30, Stamina = 30, Pass = 30, Shot = 5, Tackle = 10, Positioning = 15, Reflexes = 80, Handling = 60, Diving = 40, PressRange = 3f, ShotBias = 0.9f, PassLength = 25f };
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
        // 슛마다 골 주사위 1개, 골이 아니면 캐치 주사위 1개. 20개면 넉넉하다
        float[] sequence = { 0.10f, 0.50f, 0.90f, 0.20f, 0.35f, 0.70f, 0.05f, 0.60f, 0.99f, 0.40f, 0.30f, 0.80f, 0.15f, 0.55f, 0.25f, 0.65f, 0.45f, 0.75f, 0.33f, 0.66f };
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
        float[] sequence = { 0.10f, 0.50f, 0.90f, 0.20f, 0.35f, 0.70f, 0.05f, 0.60f, 0.99f, 0.40f, 0.30f, 0.80f, 0.15f, 0.55f, 0.25f, 0.65f, 0.45f, 0.75f, 0.33f, 0.66f };
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
    public void 골라인에_못_미치고_멈춘_슛은_빗나감으로_마감되고_킥오프한다()
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
        Assert.AreEqual(BallPhase.Free, sim.Ball.Phase);
        Assert.AreEqual(0f, sim.Ball.X, "킥오프로 돌아옴");
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
}
