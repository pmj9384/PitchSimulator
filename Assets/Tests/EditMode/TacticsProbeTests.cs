using System.Collections.Generic;
using System.IO;
using Game.Core.AutoMatch;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 전술 실측(10-06, 결과 화면 "설정 대 실측"). 작은 시뮬로 세는 규칙을 잠그고, 진짜 경기로 "설정을 바꾸면 실측이 그 방향으로 갈린다"를 본다
public class TacticsProbeTests
{
    private const float Dt = 0.02f;
    private const int Seeds = 4;

    private static PlayerStats Mid()
    {
        return new PlayerStats { RoleId = "CM", VariantId = "cm_central", Speed = 50, Stamina = 50, Pass = 50, Shot = 50, Tackle = 50, Positioning = 50, ShotBias = 0.3f, PassLength = 15f, PressRange = 8f };
    }

    private static PlayerStats Keeper()
    {
        return new PlayerStats { RoleId = "GK", VariantId = "gk_standard", Speed = 30, Stamina = 30, Pass = 30, Shot = 5, Tackle = 10, Positioning = 15, Reflexes = 80, Handling = 60, Diving = 40, PressRange = 3f, ShotBias = 0.9f, PassLength = 25f };
    }

    // 0번이 공을 쥐고 첫 틱에 1번에게 준다. 1번이 받을 때까지 돌린다
    private static TacticsProbe PassOnce(PlayerStats passerStats, float passerX)
    {
        var sim = new MatchSimulation(() => 0.5f, new PassFlightTestsHelper.PassOnce(passer: 0, receiver: 1));
        sim.AddPlayer(new PlayerState(0, 0, passerStats, passerX, 0f));
        sim.AddPlayer(new PlayerState(1, 0, Mid(), passerX + 12f, 0f));
        var probe = new TacticsProbe(sim, 0);
        sim.Kickoff();
        sim.Ball = BallRules.Own(sim.Ball, 0, passerX, 0f);
        for (int i = 0; i < 200; i++)
        {
            sim.Tick(Dt);
            probe.Sample();
            if (sim.Ball.Phase == BallPhase.Owned && sim.Ball.OwnerId == 1) { break; }
        }
        Assert.AreEqual(1, sim.Ball.OwnerId, "1번이 받았다");
        return probe;
    }

    [Test]
    public void 받은_패스는_찬_곳의_서드에_길이와_함께_센다()
    {
        // 공을 쥔 첫 틱에 차는 패스다. 틱 뒤에만 킥 지점을 적으면 이 패스의 킥 지점이 없다
        TacticsReadout readout = PassOnce(Mid(), 0f).Readout;
        Assert.AreEqual(1, readout.PassCount(Third.Middle));
        Assert.AreEqual(12f, readout.MeanPassLength(Third.Middle), 1.5f, "0에서 차서 12에 선 선수가 받았다");
        Assert.AreEqual(0, readout.PassCount(Third.Own));
        Assert.AreEqual(0, readout.PassCount(Third.Opponent));
    }

    [Test]
    public void 서드는_받은_곳이_아니라_찬_곳으로_가른다()
    {
        // 팀 0의 우리 진영은 x ≤ -17.5. -25에서 차서 -13(중앙)에서 받아도 우리 진영 패스다
        TacticsReadout readout = PassOnce(Mid(), -25f).Readout;
        Assert.AreEqual(1, readout.PassCount(Third.Own));
        Assert.AreEqual(0, readout.PassCount(Third.Middle));
    }

    [Test]
    public void 골키퍼가_찬_패스는_세지_않는다()
    {
        // GK 배급은 패스 방식이 아니라 GK 배급 설정을 따른다
        TacticsReadout readout = PassOnce(Keeper(), -45f).Readout;
        Assert.AreEqual(0, readout.PassCount(Third.Own) + readout.PassCount(Third.Middle) + readout.PassCount(Third.Opponent));
    }

    [Test]
    public void 패스가_없으면_평균_길이는_0이다()
    {
        var readout = new TacticsReadout();
        Assert.AreEqual(0f, readout.MeanPassLength(Third.Middle));
    }

    private static (List<PlayerStats> table, List<StageEntry> rows, List<TeamTactics> presets) Load()
    {
        List<PlayerStats> table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        List<StageEntry> rows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(r => r.Stage == 1);
        List<TeamTactics> presets = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv"));
        return (table, rows, presets);
    }

    // 내 팀(팀 0)에 그 카드를 주고 균형 카드와 한 판. 양 팀 실측과 시뮬을 돌려준다
    private static (TacticsReadout mine, TacticsReadout theirs, MatchSimulation sim) Play(string presetId, int seed)
    {
        var (table, rows, presets) = Load();
        TeamTactics mine = presets.Find(t => t.PresetId == presetId);
        TeamTactics balanced = presets.Find(t => t.PresetId == "balanced");
        MatchSimulation sim = MatchAssembler.Create(table, rows, mine, balanced, seed);
        var probe0 = new TacticsProbe(sim, 0);
        var probe1 = new TacticsProbe(sim, 1);
        for (int i = 0; i < MatchTuning.MatchTicks; i++)
        {
            sim.Tick(MatchTuning.FixedStep);
            probe0.Sample();
            probe1.Sample();
        }
        return (probe0.Readout, probe1.Readout, sim);
    }

    private static int Total(System.Func<Third, int> perThird)
    {
        return perThird(Third.Own) + perThird(Third.Middle) + perThird(Third.Opponent);
    }

    [Test]
    public void 양_팀이_뺏은_횟수의_합은_시뮬의_가로채기와_태클_탈취_수와_같다()
    {
        (TacticsReadout mine, TacticsReadout theirs, MatchSimulation sim) = Play("balanced", 101);
        Assert.AreEqual(sim.InterceptCount + sim.TurnoverCount, Total(mine.RegainCount) + Total(theirs.RegainCount));
        Assert.Greater(sim.InterceptCount + sim.TurnoverCount, 0, "한 판에 한 번은 뺏는다");
    }

    [Test]
    public void 양_팀이_받은_패스의_합은_시뮬이_찬_패스_수를_넘지_않는다()
    {
        (TacticsReadout mine, TacticsReadout theirs, MatchSimulation sim) = Play("balanced", 101);
        int received = Total(mine.PassCount) + Total(theirs.PassCount);
        Assert.Greater(received, 0);
        Assert.LessOrEqual(received, sim.PassCount, "끊긴 패스와 GK 배급은 빠진다");
    }

    [Test]
    public void 역습을_안_하는_카드는_역습이_0이고_역습_카드는_역습과_역습_슛이_난다()
    {
        int counters = 0;
        int counterShots = 0;
        for (int seed = 101; seed < 101 + Seeds; seed++)
        {
            Assert.AreEqual(0, Play("buildup", seed).mine.Counters, $"빌드업 축구는 역습 성향이 '안 함'이다(시드 {seed})");

            TacticsReadout counter = Play("counter", seed).mine;
            Assert.LessOrEqual(counter.CounterShots, counter.Shots, "역습 슛은 슛의 일부다");
            counters += counter.Counters;
            counterShots += counter.CounterShots;
        }
        Assert.Greater(counters, Seeds, "역습 축구는 판마다 한 번 넘게 역습한다(10-06 계측: 판당 6회 안팎)");
        Assert.Greater(counterShots, 0);
    }

    [Test]
    public void 롱볼_카드의_중앙_패스가_짧게_카드보다_길다()
    {
        // 중앙 서드 패스 방식: 빌드업 축구 = 짧게(0), 역습 축구 = 롱볼(2). 10-06 계측(24판): 17.7m 대 20.7m
        float buildupSum = 0f;
        int buildupCount = 0;
        float counterSum = 0f;
        int counterCount = 0;
        for (int seed = 101; seed < 101 + Seeds; seed++)
        {
            TacticsReadout buildup = Play("buildup", seed).mine;
            buildupSum += buildup.MeanPassLength(Third.Middle) * buildup.PassCount(Third.Middle);
            buildupCount += buildup.PassCount(Third.Middle);

            TacticsReadout counter = Play("counter", seed).mine;
            counterSum += counter.MeanPassLength(Third.Middle) * counter.PassCount(Third.Middle);
            counterCount += counter.PassCount(Third.Middle);
        }
        Assert.Greater(counterSum / counterCount, buildupSum / buildupCount + 1f, "롱볼이 1m 넘게 길다");
    }
}
