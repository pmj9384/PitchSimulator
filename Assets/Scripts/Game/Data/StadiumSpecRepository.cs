using System;
using System.Collections.Generic;
using Game.Core.Data;
using UnityEngine;

// Stadiums 접근층. TierRuleRepository와 동형(정적·지연 로드·캐시). 파싱·검증은 StadiumSpecParser 몫
public static class StadiumSpecRepository
{
    private const string ResourcePath = "Tables/Stadiums";
    private static List<StadiumSpec> all;

    public static StadiumSpec Get(string id)
    {
        EnsureLoaded();
        return StadiumSpecParser.Find(all, id);
    }

    private static void EnsureLoaded()
    {
        if (all != null) { return; }

        TextAsset csv = Resources.Load<TextAsset>(ResourcePath);
        if (csv == null)
        {
            throw new InvalidOperationException($"[StadiumSpecRepository] {ResourcePath} 없음");
        }
        all = StadiumSpecParser.Parse(csv.text);
    }
}
