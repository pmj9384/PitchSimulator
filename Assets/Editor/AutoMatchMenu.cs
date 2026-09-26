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
        Run("balanced", "balanced", Matches, firstSeed: 1);
    }

    // 밸런스 판단용 큰 표본. 100판은 승률 ±10%p가 흔들린다(09-23 실측 30/24/46). 시드는 100판과 겹치지 않게 101부터
    [MenuItem("Tools/Match/자동 대전 300판 CSV (balanced vs balanced, 시드 101~)")]
    public static void RunBalancedVsBalanced300()
    {
        Run("balanced", "balanced", 300, firstSeed: 101);
    }

    public static string Run(string homePreset, string awayPreset, int matches, int firstSeed)
    {
        List<PlayerStats> table = PlayerTableParser.Parse(LoadTable("PlayerTable"));
        List<StageEntry> rows = StageCompositionParser.Parse(LoadTable("StageComposition")).FindAll(r => r.Stage == 1);
        List<TeamTactics> presets = TeamTacticsParser.Parse(LoadTable("TacticPresets"));
        TeamTactics home = presets.Find(t => t.PresetId == homePreset) ?? throw new InvalidOperationException($"프리셋 없음: {homePreset}");
        TeamTactics away = presets.Find(t => t.PresetId == awayPreset) ?? throw new InvalidOperationException($"프리셋 없음: {awayPreset}");

        var watch = System.Diagnostics.Stopwatch.StartNew();
        List<MatchSummary> results = AutoMatchRunner.Run(table, rows, home, away, firstSeed, matches);
        watch.Stop();

        string dir = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Reports");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"automatch-{homePreset}-vs-{awayPreset}-{matches}-{DateTime.Now:yyyyMMdd-HHmm}.csv");
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
