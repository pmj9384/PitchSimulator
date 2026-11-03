using System;
using Game.Core.Match;
using Game.Core.Data;
using UnityEngine;

// ThreatTable 접근층. StaminaLoadRepository와 동형(정적·지연 로드·캐시).
// 경기 시뮬은 격자를 생성자로 받으므로, 시뮬을 만드는 Unity 쪽 입구(MatchManager·SeasonSystem)가 여기서 꺼내 넘긴다
public static class ThreatTableRepository
{
    private const string ResourcePath = "Tables/ThreatTable";
    private static ThreatGrid grid;

    public static ThreatGrid Grid
    {
        get
        {
            EnsureLoaded();
            return grid;
        }
    }

    private static void EnsureLoaded()
    {
        if (grid != null) { return; }

        TextAsset csv = Resources.Load<TextAsset>(ResourcePath);
        if (csv == null)
        {
            throw new InvalidOperationException($"[ThreatTableRepository] {ResourcePath} 없음");
        }

        grid = ThreatTableParser.Parse(csv.text);
    }
}
