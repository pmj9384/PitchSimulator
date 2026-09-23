using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.AutoMatch;
using Game.Core.Data;
using Game.Core.Tactics;
using UnityEditor;
using UnityEngine;

// 자동 대전 러너 메뉴(플랜 09-23). Resources 테이블 3개 → 러너 100판 → 프로젝트 루트 Reports/ CSV + 콘솔 요약.
// 코어(MatchAssembler·AutoMatchRunner)는 순수라 여기서는 파일 읽기·쓰기만 한다. 100판 ≈ 1분(EditMode 3분 경기 0.5초 기준)
public static class AutoMatchMenu
{
    private const int Matches = 100;

    [MenuItem("Tools/Match/자동 대전 100판 CSV (balanced vs balanced)")]
    public static void RunBalancedVsBalanced()
    {
        Run("balanced", "balanced");
    }

    public static string Run(string homePreset, string awayPreset)
    {
        List<PlayerStats> table = PlayerTableParser.Parse(LoadTable("PlayerTable"));
        List<StageEntry> rows = StageCompositionParser.Parse(LoadTable("StageComposition")).FindAll(r => r.Stage == 1);
        List<TeamTactics> presets = TeamTacticsParser.Parse(LoadTable("TacticPresets"));
        TeamTactics home = presets.Find(t => t.PresetId == homePreset) ?? throw new InvalidOperationException($"프리셋 없음: {homePreset}");
        TeamTactics away = presets.Find(t => t.PresetId == awayPreset) ?? throw new InvalidOperationException($"프리셋 없음: {awayPreset}");

        var watch = System.Diagnostics.Stopwatch.StartNew();
        List<MatchSummary> results = AutoMatchRunner.Run(table, rows, home, away, firstSeed: 1, matches: Matches);
        watch.Stop();

        string dir = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Reports");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"automatch-{homePreset}-vs-{awayPreset}-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        File.WriteAllText(path, AutoMatchCsv.Serialize(results));

        AutoMatchStats stats = AutoMatchRunner.Aggregate(results);
        Debug.Log($"[AutoMatch] {stats} | {watch.Elapsed.TotalSeconds:0.0}초 | {path}");
        return path;
    }

    private static string LoadTable(string name)
    {
        TextAsset csv = Resources.Load<TextAsset>("Tables/" + name);
        if (csv == null) { throw new InvalidOperationException($"Resources/Tables/{name} 없음"); }
        return csv.text;
    }
}
