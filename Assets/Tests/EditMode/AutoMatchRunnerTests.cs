using System.Collections.Generic;
using System.IO;
using Game.Core.AutoMatch;
using Game.Core.Data;
using Game.Core.Tactics;
using NUnit.Framework;

// 자동 대전 러너(09-23): 시드 N개 → 결과 N행, 같은 시드 = 같은 행, CSV 헤더·행 수. 100판은 메뉴(Tools/Match)가 돌린다(테스트는 5판)
public class AutoMatchRunnerTests
{
    private static (List<PlayerStats> table, List<StageEntry> rows, TeamTactics balanced) Load()
    {
        List<PlayerStats> table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        List<StageEntry> rows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(r => r.Stage == 1);
        TeamTactics balanced = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv")).Find(t => t.PresetId == "balanced");
        return (table, rows, balanced);
    }

    [Test]
    public void 시드_5개면_결과_5행이고_같은_시드는_같은_행이다()
    {
        var (table, rows, balanced) = Load();
        List<MatchSummary> a = AutoMatchRunner.Run(table, rows, balanced, balanced, firstSeed: 1, matches: 5);
        List<MatchSummary> b = AutoMatchRunner.Run(table, rows, balanced, balanced, firstSeed: 1, matches: 5);

        Assert.AreEqual(5, a.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.AreEqual(i + 1, a[i].Seed);
            Assert.AreEqual(a[i].HomeGoals, b[i].HomeGoals, $"시드 {i + 1} 홈 골");
            Assert.AreEqual(a[i].AwayGoals, b[i].AwayGoals, $"시드 {i + 1} 원정 골");
            Assert.AreEqual(a[i].Passes, b[i].Passes, $"시드 {i + 1} 패스");
            Assert.AreEqual(a[i].TackleAttempts, b[i].TackleAttempts, $"시드 {i + 1} 태클");
            Assert.AreEqual(9000, a[i].HomeOwnedTicks + a[i].AwayOwnedTicks + (9000 - a[i].HomeOwnedTicks - a[i].AwayOwnedTicks), "틱 합");
        }
        int totalShots = 0;
        for (int i = 0; i < 5; i++) { totalShots += a[i].HomeShots + a[i].AwayShots; }
        Assert.Greater(totalShots, 0, "5판 합쳐 슛이 난다(한 판 0슛은 있을 수 있다. 드리블 배율 0.6에서 시드 1이 그랬다)");

        AutoMatchStats stats = AutoMatchRunner.Aggregate(a);
        Assert.AreEqual(5, stats.Matches);
        Assert.AreEqual(1f, stats.HomeWinRate + stats.AwayWinRate + stats.DrawRate, 1e-5f);
    }

    [Test]
    public void CSV는_헤더_한_줄과_판_수만큼의_행이다()
    {
        var (table, rows, balanced) = Load();
        List<MatchSummary> results = AutoMatchRunner.Run(table, rows, balanced, balanced, firstSeed: 1, matches: 2);
        string csv = AutoMatchCsv.Serialize(results);

        string[] lines = csv.TrimEnd('\n').Split('\n');
        Assert.AreEqual(3, lines.Length, "헤더 1 + 행 2");
        StringAssert.StartsWith("seed,homePreset,awayPreset,homeGoals,awayGoals,winner,", lines[0]);
        StringAssert.StartsWith("1,balanced,balanced,", lines[1]);
        Assert.IsFalse(csv.Contains("\r"), "줄바꿈 \\n 고정");
    }
}
