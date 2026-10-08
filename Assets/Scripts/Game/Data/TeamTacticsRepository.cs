using System;
using System.Collections.Generic;
using Game.Core.Tactics;
using UnityEngine;

// TacticPresets 접근층. PlayerTableRepository와 동형(정적·지연 로드·캐시).
public static class TeamTacticsRepository
{
    private const string ResourcePath = "Tables/TacticPresets";
    private static Dictionary<string, TeamTactics> byId;
    private static List<TeamTactics> ordered;

    public static TeamTactics Get(string presetId)
    {
        EnsureLoaded();
        TeamTactics t;
        if (byId.TryGetValue(presetId, out t)) { return t; }

        Debug.LogError($"[TeamTacticsRepository] 없는 presetId: {presetId}");
        return null;
    }

    public static IReadOnlyList<TeamTactics> All
    {
        get
        {
            EnsureLoaded();
            return ordered;
        }
    }

    private static void EnsureLoaded()
    {
        if (byId != null) { return; }

        TextAsset csv = Resources.Load<TextAsset>(ResourcePath);
        if (csv == null)
        {
            throw new InvalidOperationException($"[TeamTacticsRepository] {ResourcePath} 없음");
        }

        List<TeamTactics> parsed = TeamTacticsParser.Parse(csv.text);
        var map = new Dictionary<string, TeamTactics>(StringComparer.OrdinalIgnoreCase);
        foreach (TeamTactics t in parsed)
        {
            map.Add(t.PresetId, t);
        }

        ordered = parsed;
        byId = map;
    }
}
