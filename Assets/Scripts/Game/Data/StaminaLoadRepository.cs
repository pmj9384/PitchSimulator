using System;
using System.Collections.Generic;
using Game.Core.Tactics;
using UnityEngine;

// StaminaLoadTable 접근층. TeamTacticsRepository와 동형(정적·지연 로드·캐시).
public static class StaminaLoadRepository
{
    private const string ResourcePath = "Tables/StaminaLoadTable";
    private static List<StaminaLoadEntry> entries;

    public static IReadOnlyList<StaminaLoadEntry> All
    {
        get { EnsureLoaded(); return entries; }
    }

    private static void EnsureLoaded()
    {
        if (entries != null) { return; }

        TextAsset csv = Resources.Load<TextAsset>(ResourcePath);
        if (csv == null)
        {
            throw new InvalidOperationException($"[StaminaLoadRepository] {ResourcePath} 없음");
        }

        entries = StaminaLoadTableParser.Parse(csv.text);
    }
}
