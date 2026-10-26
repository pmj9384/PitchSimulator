using System;
using System.Collections.Generic;
using Game.Core.Tactics;
using UnityEngine;

// DefenseRoles 접근층. TeamTacticsRepository와 동형(정적·지연 로드·캐시). 10-25 슬롯 격자
public static class DefenseRoleRepository
{
    private const string ResourcePath = "Tables/DefenseRoles";
    private static List<DefenseRole> ordered;

    public static IReadOnlyList<DefenseRole> All
    {
        get
        {
            if (ordered != null) { return ordered; }

            TextAsset csv = Resources.Load<TextAsset>(ResourcePath);
            if (csv == null)
            {
                throw new InvalidOperationException($"[DefenseRoleRepository] {ResourcePath} 없음");
            }
            ordered = DefenseRoleParser.Parse(csv.text);
            return ordered;
        }
    }

    public static DefenseRole Get(string id)
    {
        DefenseRole role = DefenseRoleParser.Find(All, id);
        if (role != null) { return role; }

        Debug.LogError($"[DefenseRoleRepository] 없는 defenseRoleId: {id}. 기본 역할로 대신한다");
        return DefenseRoleParser.Find(All, DefenseRole.DefaultId);
    }
}
