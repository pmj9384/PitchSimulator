using System.Collections.Generic;
using System.IO;
using Game.Core.AutoMatch;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 하프타임(09-27 유저 결정, IFAB 8조): 표시 시계가 45분 + 전반 추가시간에 닿는 틱에 진영 교체 + 중앙 리셋 + 전반 킥오프를 안 한 팀이 킥오프.
// 시뮬 코어에 두는 이유: 러너·시즌 헤드리스·인게임이 같은 Tick을 타야 같은 시드 = 같은 경기가 유지된다
public class HalfTimeTests
{
    private static MatchSimulation Assemble(int seed)
    {
        List<PlayerStats> table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        List<StageEntry> rows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(r => r.Stage == 1);
        TeamTactics balanced = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv")).Find(t => t.PresetId == "balanced");
        return MatchAssembler.Create(table, rows, balanced, balanced, seed);
    }

    private static float KeeperX(MatchSimulation sim, int team)
    {
        for (int i = 0; i < sim.Players.Count; i++)
        {
            if (sim.Players[i].Team == team && sim.Players[i].IsGoalkeeper) { return sim.Players[i].X; }
        }
        Assert.Fail($"팀 {team} GK 없음");
        return 0f;
    }

    private static PlayerState Kicker(MatchSimulation sim)
    {
        for (int i = 0; i < sim.Players.Count; i++)
        {
            if (sim.Players[i].PlayerId == sim.Ball.OwnerId) { return sim.Players[i]; }
        }
        Assert.Fail("소유자가 없다");
        return null!;
    }

    [Test]
    public void 사천오백틱에_전반_킥오프를_안_한_팀이_중앙에서_후반을_시작한다()
    {
        MatchSimulation sim = Assemble(seed: 2);   // 짝수 시드 = 팀 0 킥오프(MatchAssembler)
        Assert.AreEqual(0, sim.OwnerTeam(), "전반 킥오프 = 팀 0");
        float gk0Before = KeeperX(sim, 0);
        Assert.Less(gk0Before, 0f, "전반 팀 0 GK는 -X 골 앞");
        int ht = MatchClock.HalfTimeTick(sim.Added);
        for (int i = 0; i < ht - 1; i++) { sim.Tick(MatchTuning.FixedStep); }
        Assert.IsFalse(sim.IsSecondHalf);
        Assert.IsFalse(sim.SidesSwitched);

        sim.Tick(MatchTuning.FixedStep);   // 하프타임 틱

        Assert.IsTrue(sim.IsSecondHalf);
        Assert.IsTrue(sim.SidesSwitched, "진영 교체");
        Assert.AreEqual(-1, sim.AttackSignOf(0), "후반 팀 0은 -X 골을 공격");
        Assert.AreEqual(+1, sim.AttackSignOf(1));
        Assert.AreEqual(0, sim.TeamOfSign(-1));
        Assert.Greater(KeeperX(sim, 0), 0f, "후반 팀 0 GK는 +X 골 앞");
        Assert.AreEqual(-gk0Before, KeeperX(sim, 0), 1e-4f, "자리 X가 정확히 미러");
        Assert.AreEqual(1, sim.OwnerTeam(), "후반 킥오프 = 팀 1");
        PlayerState kicker = Kicker(sim);
        Assert.AreEqual(kicker.X, sim.Ball.X, 1e-4f, "공은 킥커 발치(09-23 킥오프 규칙: 중앙에 가장 가까운 필드 플레이어가 자기 자리에서 잡는다)");
        Assert.AreEqual(kicker.Z, sim.Ball.Z, 1e-4f);
        Assert.IsFalse(kicker.IsGoalkeeper);
    }

    [Test]
    public void 홀수_시드면_팀1이_전반을_시작하고_팀0이_후반을_시작한다()
    {
        MatchSimulation sim = Assemble(seed: 3);
        Assert.AreEqual(1, sim.OwnerTeam());
        int ht = MatchClock.HalfTimeTick(sim.Added);
        for (int i = 0; i < ht; i++) { sim.Tick(MatchTuning.FixedStep); }
        Assert.AreEqual(0, sim.OwnerTeam());
    }

    [Test]
    public void 같은_시드는_하프타임이_있어도_같은_경기다()
    {
        MatchSimulation a = Assemble(seed: 7);
        MatchSimulation b = Assemble(seed: 7);
        for (int i = 0; i < MatchTuning.MatchTicks; i++)
        {
            a.Tick(MatchTuning.FixedStep);
            b.Tick(MatchTuning.FixedStep);
        }
        Assert.AreEqual(a.HomeGoals, b.HomeGoals);
        Assert.AreEqual(a.AwayGoals, b.AwayGoals);
        Assert.AreEqual(a.PassCount, b.PassCount);
        Assert.AreEqual(a.Ball.X, b.Ball.X, 1e-5f);
        Assert.IsTrue(a.SidesSwitched && b.SidesSwitched);
    }

    [Test]
    public void 후반_골은_뒤집힌_방향으로_맞는_팀에_집계된다()
    {
        // 후반 진영 교체 뒤 팀 0이 -X 골로 공격한다. 22명 경기를 끝까지 돌려 팀 0 소유 중 공이 +X 골라인을 넘어 골이 되는 일이 없음을(= 자기 골 집계 없음) 간접 확인:
        // 골 수 합이 슛 결과 Goal 수와 같고, 후반 골 순간 슈터 팀의 AttackSign이 그 팀의 AttackSignOf와 같다
        MatchSimulation sim = Assemble(seed: 11);
        int goals = 0;
        bool signMismatch = false;
        sim.ShotResolved += r =>
        {
            if (r.Outcome != ShotOutcome.Goal) { return; }
            goals++;
            PlayerState shooter = null!;
            for (int i = 0; i < sim.Players.Count; i++) { if (sim.Players[i].PlayerId == r.ShooterId) { shooter = sim.Players[i]; } }
            if (shooter.AttackSign != sim.AttackSignOf(shooter.Team)) { signMismatch = true; }
        };
        for (int i = 0; i < MatchTuning.MatchTicks; i++) { sim.Tick(MatchTuning.FixedStep); }
        Assert.AreEqual(goals, sim.HomeGoals + sim.AwayGoals, "집계된 골 = 골 판정 수");
        Assert.IsFalse(signMismatch, "슈터의 부호는 늘 팀 부호와 같다(진영 교체가 선수·시뮬 양쪽에 반영)");
    }
}
