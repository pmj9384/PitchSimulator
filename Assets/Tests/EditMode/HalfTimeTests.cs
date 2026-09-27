using System.Collections.Generic;
using System.IO;
using Game.Core.AutoMatch;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 하프타임(09-27 유저 결정, 스펙 §11 1차 "교대 없음"의 최소 변경): 4,500틱에 중앙 리셋 + 전반 킥오프를 안 한 팀이 킥오프. 진영 교체는 없다.
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
        for (int i = 0; i < MatchTuning.HalfTimeTick - 1; i++) { sim.Tick(MatchTuning.FixedStep); }
        Assert.IsFalse(sim.IsSecondHalf);
        Assert.AreEqual(MatchTuning.HalfTimeTick - 1, sim.TickCount);

        sim.Tick(MatchTuning.FixedStep);   // 4,500번째 틱

        Assert.IsTrue(sim.IsSecondHalf);
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
        for (int i = 0; i < MatchTuning.HalfTimeTick; i++) { sim.Tick(MatchTuning.FixedStep); }
        Assert.AreEqual(0, sim.OwnerTeam());
    }

    [Test]
    public void 같은_시드는_하프타임이_있어도_같은_경기다()
    {
        MatchSimulation a = Assemble(seed: 7);
        MatchSimulation b = Assemble(seed: 7);
        for (int i = 0; i < MatchTuning.MatchTicks; i++) { a.Tick(MatchTuning.FixedStep); b.Tick(MatchTuning.FixedStep); }
        Assert.AreEqual(a.HomeGoals, b.HomeGoals);
        Assert.AreEqual(a.AwayGoals, b.AwayGoals);
        Assert.AreEqual(a.PassCount, b.PassCount);
        Assert.AreEqual(a.Ball.X, b.Ball.X, 1e-5f);
    }
}
