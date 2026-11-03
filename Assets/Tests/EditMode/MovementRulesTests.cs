using Game.Core.Match;
using NUnit.Framework;

// 가속이 있는 이동(09-29 F2b, 10-29 개인 가속). 개인 가속 = 최고 속도 ÷ (2 × 반응)이라 반응 등가가 모든 선수에게 같다
public class MovementRulesTests
{
    private const float Dt = 0.02f;

    [Test]
    public void speed_50_선수의_가속은_7이다()
    {
        float accel = MovementRules.AccelerationFor(MatchTuning.SpeedMpsAt50, MatchTuning.ReactionTime);
        Assert.AreEqual(MatchTuning.AccelerationAt50, accel, 1e-4f);
    }

    [TestCase(30)]
    [TestCase(50)]
    [TestCase(80)]
    public void 반응_등가는_속도_스탯과_무관하게_판정의_반응_지연과_같다(int speedStat)
    {
        float top = MatchRules.SpeedMps(speedStat);
        float accel = MovementRules.AccelerationFor(top, MatchTuning.ReactionTime);
        Assert.AreEqual(MatchTuning.ReactionTime, MovementRules.ReactionEquivalent(top, accel), 1e-4f);
    }

    [TestCase(30)]
    [TestCase(80)]
    public void 정지에서_최고_속도까지_걸리는_시간은_반응의_두_배로_모두_같다(int speedStat)
    {
        float top = MatchRules.SpeedMps(speedStat);
        float accel = MovementRules.AccelerationFor(top, MatchTuning.ReactionTime);
        int ticks = (int)System.Math.Round(2f * MatchTuning.ReactionTime / Dt);
        (float x, float z, float vx, float vz) state = (0f, 0f, 0f, 0f);
        for (int i = 0; i < ticks - 1; i++)
        {
            state = MovementRules.Step(state.x, state.z, state.vx, state.vz, 100f, 0f, top, accel, Dt, false);
        }
        Assert.Less(state.vx, top - 1e-3f, "한 틱 전엔 아직 최고 속도 아래");
        state = MovementRules.Step(state.x, state.z, state.vx, state.vz, 100f, 0f, top, accel, Dt, false);
        Assert.AreEqual(top, state.vx, 1e-3f, "2 × 반응초에 최고 속도");
    }

    [Test]
    public void 빠른_선수가_같은_거리를_먼저_간다()
    {
        Assert.Less(TicksToCover(10f, 80), TicksToCover(10f, 30));
    }

    [Test]
    public void 자리_잡기는_목표를_지나치지_않고_멈춘다()
    {
        float top = MatchTuning.SpeedMpsAt50;
        float accel = MovementRules.AccelerationFor(top, MatchTuning.ReactionTime);
        (float x, float z, float vx, float vz) state = (0f, 0f, top, 0f);
        for (int i = 0; i < 200; i++)
        {
            state = MovementRules.Step(state.x, state.z, state.vx, state.vz, 5f, 0f, top, accel, Dt, true);
            Assert.LessOrEqual(state.x, 5f + 1e-4f, $"{i}틱: 목표 5m를 넘지 않는다");
        }
        Assert.AreEqual(5f, state.x, 1e-3f);
        Assert.AreEqual(0f, state.vx, 1e-3f);
    }

    [Test]
    public void 자리가_가까우면_조깅_상한이다()
    {
        float speed = MovementRules.PositioningSpeed(7f, MatchTuning.LateToPositionMeters, MatchTuning.JogSpeedFraction, MatchTuning.LateToPositionMeters);
        Assert.AreEqual(7f * MatchTuning.JogSpeedFraction, speed, 1e-4f, "문턱과 같은 거리는 아직 조깅");
    }

    [Test]
    public void 자리에서_많이_늦었으면_전력으로_돌아간다()
    {
        float speed = MovementRules.PositioningSpeed(7f, MatchTuning.LateToPositionMeters + 0.1f, MatchTuning.JogSpeedFraction, MatchTuning.LateToPositionMeters);
        Assert.AreEqual(7f, speed, 1e-4f);
    }

    [Test]
    public void 전력_이동은_목표를_지나쳐도_멈추지_않는다()
    {
        float top = MatchTuning.SpeedMpsAt50;
        float accel = MovementRules.AccelerationFor(top, MatchTuning.ReactionTime);
        (float x, float z, float vx, float vz) state = (4.9f, 0f, top, 0f);
        state = MovementRules.Step(state.x, state.z, state.vx, state.vz, 5f, 0f, top, accel, Dt, false);
        Assert.Greater(state.x, 5f, "공 쫓기는 Arrive가 아니라 지나간다");
        Assert.AreEqual(top, state.vx, 1e-3f, "감속 없음");
    }

    [Test]
    public void 멈춤은_가속_한도로_줄고_반대로_가지_않는다()
    {
        float accel = MovementRules.AccelerationFor(MatchTuning.SpeedMpsAt50, MatchTuning.ReactionTime);
        (float x, float z, float vx, float vz) state = (0f, 0f, 1f, 0f);
        state = MovementRules.Brake(state.x, state.z, state.vx, state.vz, accel, Dt);
        Assert.AreEqual(1f - accel * Dt, state.vx, 1e-4f, "한 틱에 a·dt만큼");
        for (int i = 0; i < 100; i++)
        {
            state = MovementRules.Brake(state.x, state.z, state.vx, state.vz, accel, Dt);
        }
        Assert.AreEqual(0f, state.vx, 1e-4f);
    }

    [Test]
    public void 속도_자르기는_상한을_넘을_때만_방향을_지키며_줄인다()
    {
        (float vx, float vz) cut = MovementRules.ClampSpeed(6f, 8f, 5f);
        Assert.AreEqual(3f, cut.vx, 1e-4f, "크기 10 → 5, 방향 (0.6, 0.8) 그대로");
        Assert.AreEqual(4f, cut.vz, 1e-4f);
        (float vx, float vz) keep = MovementRules.ClampSpeed(3f, 0f, 5f);
        Assert.AreEqual(3f, keep.vx, 1e-6f);
    }

    private static int TicksToCover(float distance, int speedStat)
    {
        float top = MatchRules.SpeedMps(speedStat);
        float accel = MovementRules.AccelerationFor(top, MatchTuning.ReactionTime);
        (float x, float z, float vx, float vz) state = (0f, 0f, 0f, 0f);
        int ticks = 0;
        while (state.x < distance)
        {
            state = MovementRules.Step(state.x, state.z, state.vx, state.vz, 100f, 0f, top, accel, Dt, false);
            ticks++;
        }
        return ticks;
    }

    [Test]
    public void 목표에_이르면_Arrive는_멈추고_Seek는_지나쳐_간다()
    {
        // 런 패스 수신이 Seek인 이유(RunRules.RunnerMeetsBall): Arrive는 목표에서 서고 Seek는 멈출 생각 없이 지나간다
        float top = MatchRules.SpeedMps(50);
        float accel = MovementRules.AccelerationFor(top, MatchTuning.ReactionTime);
        (float x, float z, float vx, float vz) arrive = (0f, 0f, top, 0f);
        (float x, float z, float vx, float vz) seek = (0f, 0f, top, 0f);
        float arriveMaxX = 0f;
        float seekMaxX = 0f;
        for (int i = 0; i < 150; i++)
        {
            arrive = MovementRules.Step(arrive.x, arrive.z, arrive.vx, arrive.vz, 10f, 0f, top, accel, Dt, true);
            seek = MovementRules.Step(seek.x, seek.z, seek.vx, seek.vz, 10f, 0f, top, accel, Dt, false);
            arriveMaxX = System.Math.Max(arriveMaxX, arrive.x);
            seekMaxX = System.Math.Max(seekMaxX, seek.x);
        }
        Assert.LessOrEqual(arriveMaxX, 10.5f, "Arrive: 목표에서 감속해 선다");
        Assert.AreEqual(10f, arrive.x, 0.5f);
        Assert.Greater(seekMaxX, 11f, "Seek: 최고 속도로 목표를 지나친다");
    }
}
