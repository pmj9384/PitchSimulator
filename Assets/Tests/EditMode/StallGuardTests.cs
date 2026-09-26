using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Core.AutoMatch;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 교착 회귀 감시(09-23). 오늘 실제로 났던 정지 2종(발치 상대 되잡기 잠금·멈춘 패스 교착)과 탐지한 핑퐁을 시드 20개 × 3분에서 잠근다.
// A 전원 정지(5초 이벤트 없음 + 공·선수 안 움직임) / B 자유 공 방치 / C 멈춘 공인데 패스 진행 중 / D 소유자 정지 / E 같은 둘 6회 교대(공 X 3m 안)
public class StallGuardTests
{
    private const int Seeds = 20;
    private const int Ticks = 9000;

    [Test]
    public void 시드_20개_3분에_교착_5종이_없다()
    {
        List<PlayerStats> table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        List<StageEntry> rows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(r => r.Stage == 1);
        TeamTactics balanced = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv")).Find(t => t.PresetId == "balanced");
        FieldInfo passInFlight = typeof(MatchSimulation).GetField("passInFlight", BindingFlags.NonPublic | BindingFlags.Instance);

        var found = new List<string>();
        for (int seed = 1; seed <= Seeds; seed++)
        {
            MatchSimulation sim = MatchAssembler.Create(table, rows, balanced, balanced, seed);
            int lastEventTick = 0;
            var owners = new List<int>();
            float pingPongStartX = 0f;
            sim.PossessionChanged += r =>
            {
                lastEventTick = -1;   // 아래 루프가 현재 틱으로 채운다
                if (owners.Count == 0 || owners[owners.Count - 1] != r.NewOwnerId) { owners.Add(r.NewOwnerId); if (owners.Count > 10) { owners.RemoveAt(0); } }
                if (owners.Count == 1) { pingPongStartX = r.X; }
            };
            sim.ShotResolved += r => { lastEventTick = -1; owners.Clear(); };

            float[] px = new float[sim.Players.Count]; float[] pz = new float[sim.Players.Count];
            float ballX = sim.Ball.X, ballZ = sim.Ball.Z; int freeTicks = 0; float freeMinDist = 999f; int ownerId = -1; int ownerTicks = 0; float ownerX = 0f;
            for (int i = 0; i < sim.Players.Count; i++) { px[i] = sim.Players[i].X; pz[i] = sim.Players[i].Z; }
            int quietSince = 0; float quietBallX = ballX, quietBallZ = ballZ; float quietMove = 0f;
            for (int tick = 0; tick < Ticks; tick++)
            {
                sim.Tick(MatchTuning.FixedStep);
                if (lastEventTick == -1) { lastEventTick = tick; quietSince = tick; quietBallX = sim.Ball.X; quietBallZ = sim.Ball.Z; quietMove = 0f; }
                for (int i = 0; i < sim.Players.Count; i++) { quietMove += System.Math.Abs(sim.Players[i].X - px[i]) + System.Math.Abs(sim.Players[i].Z - pz[i]); px[i] = sim.Players[i].X; pz[i] = sim.Players[i].Z; }

                // A
                if (tick - quietSince >= 250 && System.Math.Abs(sim.Ball.X - quietBallX) + System.Math.Abs(sim.Ball.Z - quietBallZ) < 0.5f && quietMove < 2f)
                {
                    found.Add($"A 시드{seed} t={tick * 0.02f:0.0} 전원 정지 공 {sim.Ball.Phase} ({sim.Ball.X:0.0},{sim.Ball.Z:0.0})"); break;
                }
                // B
                if (sim.Ball.Phase == BallPhase.Free)
                {
                    float d = 999f; foreach (PlayerState q in sim.Players) { float dd = (q.X - sim.Ball.X) * (q.X - sim.Ball.X) + (q.Z - sim.Ball.Z) * (q.Z - sim.Ball.Z); if (dd < d) { d = dd; } }
                    d = (float)System.Math.Sqrt(d);
                    if (freeTicks == 0) { freeMinDist = d; }
                    freeTicks++;
                    if (d < freeMinDist - 0.05f) { freeMinDist = d; freeTicks = 1; }
                    if (freeTicks >= 150) { found.Add($"B 시드{seed} t={tick * 0.02f:0.0} 자유 공 방치 최근접 {d:0.00}m"); break; }
                }
                else { freeTicks = 0; }
                // C
                if ((bool)passInFlight.GetValue(sim) && sim.Ball.Phase != BallPhase.Flight) { found.Add($"C 시드{seed} t={tick * 0.02f:0.0} 패스 잔류 {sim.Ball.Phase}"); break; }
                // D
                if (sim.Ball.Phase == BallPhase.Owned)
                {
                    if (sim.Ball.OwnerId == ownerId) { ownerTicks++; } else { ownerId = sim.Ball.OwnerId; ownerTicks = 0; ownerX = sim.Ball.X; }
                    if (ownerTicks >= 150 && System.Math.Abs(sim.Ball.X - ownerX) < 0.3f) { found.Add($"D 시드{seed} t={tick * 0.02f:0.0} 소유자 #{ownerId} 정지"); break; }
                }
                else { ownerId = -1; ownerTicks = 0; }
                // E
                int alt = 0;
                for (int k = 2; k < owners.Count; k++) { if (owners[k] == owners[k - 2] && owners[k] != owners[k - 1]) { alt++; } else { alt = 0; } }
                bool tight = alt >= 4 && System.Math.Abs(sim.Ball.X - pingPongStartX) < 3f;   // 6회 교대 + 공 X 3m 안(골문 앞 태클전)
                bool longRun = alt >= 8;                                                          // 10회 교대는 거리 무관(측면 패스 왕복, 09-23 Play 22회)
                if (tight || longRun)
                {
                    found.Add($"E 시드{seed} t={tick * 0.02f:0.0} 핑퐁 #{owners[owners.Count - 2]}↔#{owners[owners.Count - 1]} {alt + 2}회 공 ({sim.Ball.X:0.0},{sim.Ball.Z:0.0})"); break;
                }
            }
        }
        Assert.IsEmpty(found, "교착:\n" + string.Join("\n", found));
    }
}
