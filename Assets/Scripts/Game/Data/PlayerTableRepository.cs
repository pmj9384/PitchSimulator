using System;
using System.Collections.Generic;
using Game.Core.Data;
using UnityEngine;

// PlayerTable 접근층. 템플릿 DataTableManager 관례와 동형(정적·지연 로드·캐시).
// 파싱·검증은 순수 코어(PlayerTableParser) 몫, 여기는 Resources에서 꺼내 캐시하는 문지기만 한다.
public static class PlayerTableRepository
{
    private const string ResourcePath = "Tables/PlayerTable";
    private static Dictionary<string, PlayerStats> byId;
    private static List<PlayerStats> ordered;   // CSV 행 순서. Dictionary.Values는 순서를 보장하지 않아 팔레트 순서가 흔들린다

    public static PlayerStats Get(string roleId)
    {
        EnsureLoaded();
        PlayerStats stats;
        if (byId.TryGetValue(roleId, out stats)) { return stats; }

        Debug.LogError($"[PlayerTableRepository] 없는 roleId: {roleId}");
        return null;
    }

    // CSV에 적힌 순서 그대로
    public static IReadOnlyList<PlayerStats> All
    {
        get { EnsureLoaded(); return ordered; }
    }

    private static void EnsureLoaded()
    {
        if (byId != null) { return; }

        TextAsset csv = Resources.Load<TextAsset>(ResourcePath);
        if (csv == null)
        {
            throw new InvalidOperationException($"[PlayerTableRepository] {ResourcePath} 없음");
        }

        // 파싱이 성공한 뒤에만 캐시를 공개한다. 먼저 만들어 두면 파싱 실패 뒤 호출이 빈 표로 조용히 넘어간다
        List<PlayerStats> parsed = PlayerTableParser.Parse(csv.text);
        var map = new Dictionary<string, PlayerStats>(StringComparer.OrdinalIgnoreCase);   // 파서의 중복 검사(대소문자 무시)와 같은 기준
        foreach (PlayerStats stats in parsed)
        {
            map.Add(stats.RoleId, stats);
        }

        ordered = parsed;
        byId = map;
    }
}
