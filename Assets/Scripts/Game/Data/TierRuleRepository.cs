using System;
using System.Collections.Generic;
using Game.Core.Data;
using UnityEngine;

// TierRules 접근층. PlayerTableRepository와 동형(정적·지연 로드·캐시). 파싱·검증은 TierRuleParser 몫
public static class TierRuleRepository
{
    private const string ResourcePath = "Tables/TierRules";
    private static List<TierRule> ordered;

    public static TierRule Get(int tier)
    {
        EnsureLoaded();
        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Tier == tier) { return ordered[i]; }
        }
        throw new InvalidOperationException($"[TierRuleRepository] TierRules에 {tier}부가 없다");
    }

    public static IReadOnlyList<TierRule> All
    {
        get { EnsureLoaded(); return ordered; }
    }

    private static void EnsureLoaded()
    {
        if (ordered != null) { return; }

        TextAsset csv = Resources.Load<TextAsset>(ResourcePath);
        if (csv == null)
        {
            throw new InvalidOperationException($"[TierRuleRepository] {ResourcePath} 없음");
        }
        ordered = TierRuleParser.Parse(csv.text);
    }
}
