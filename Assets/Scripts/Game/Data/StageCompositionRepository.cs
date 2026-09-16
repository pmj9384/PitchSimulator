using System;
using System.Collections.Generic;
using Game.Core.Data;
using UnityEngine;

// StageComposition 접근층. 스테이지 번호로 편성 행을 꺼낸다. 전 스테이지를 한 번 읽어 스테이지별로 묶어 둔다.
public static class StageCompositionRepository
{
    private const string ResourcePath = "Tables/StageComposition";
    private static readonly List<StageEntry> Empty = new List<StageEntry>();
    private static Dictionary<int, List<StageEntry>> byStage;

    public static IReadOnlyList<StageEntry> RowsFor(int stage)
    {
        EnsureLoaded();
        List<StageEntry> rows;
        if (byStage.TryGetValue(stage, out rows)) { return rows; }

        Debug.LogWarning($"[StageCompositionRepository] stage {stage}의 편성 행이 없다");
        return Empty;
    }

    private static void EnsureLoaded()
    {
        if (byStage != null) { return; }

        TextAsset csv = Resources.Load<TextAsset>(ResourcePath);
        if (csv == null)
        {
            throw new InvalidOperationException($"[StageCompositionRepository] {ResourcePath} 없음");
        }

        // 파싱이 성공한 뒤에만 캐시를 공개한다(PlayerTableRepository와 같은 이유)
        var map = new Dictionary<int, List<StageEntry>>();
        foreach (StageEntry entry in StageCompositionParser.Parse(csv.text))
        {
            List<StageEntry> rows;
            if (!map.TryGetValue(entry.Stage, out rows))
            {
                rows = new List<StageEntry>();
                map.Add(entry.Stage, rows);
            }
            rows.Add(entry);
        }
        byStage = map;
    }
}
