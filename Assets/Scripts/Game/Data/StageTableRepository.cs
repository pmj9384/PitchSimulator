using System;
using System.Collections.Generic;
using Game.Core.Data;
using UnityEngine;

// StageTable 접근층. PlayerTableRepository와 동형(정적·지연 로드·캐시). 파싱·검증은 순수 코어 몫.
public static class StageTableRepository
{
    private const string ResourcePath = "Tables/StageTable";
    private static Dictionary<int, StageInfo> byStage;

    public static StageInfo Get(int stage)
    {
        EnsureLoaded();
        StageInfo info;
        if (byStage.TryGetValue(stage, out info)) { return info; }

        Debug.LogError($"[StageTableRepository] 없는 stage: {stage}");
        return null;
    }

    private static void EnsureLoaded()
    {
        if (byStage != null) { return; }

        TextAsset csv = Resources.Load<TextAsset>(ResourcePath);
        if (csv == null)
        {
            throw new InvalidOperationException($"[StageTableRepository] {ResourcePath} 없음");
        }

        // 파싱이 성공한 뒤에만 캐시를 공개한다(PlayerTableRepository와 같은 이유)
        var map = new Dictionary<int, StageInfo>();
        foreach (StageInfo info in StageTableParser.Parse(csv.text))
        {
            map.Add(info.Stage, info);
        }
        byStage = map;
    }
}
