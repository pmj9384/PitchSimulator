using System;
using Game.Core.Data;
using UnityEngine;

// TeamNames 접근층. 표 하나를 그대로 준다(조합은 TeamNameTable 몫)
public static class TeamNameRepository
{
    private const string ResourcePath = "Tables/TeamNames";
    private static TeamNameTable table;

    public static TeamNameTable Table
    {
        get
        {
            if (table != null) { return table; }

            TextAsset csv = Resources.Load<TextAsset>(ResourcePath);
            if (csv == null)
            {
                throw new InvalidOperationException($"[TeamNameRepository] {ResourcePath} 없음");
            }
            table = TeamNameTable.Parse(csv.text);
            return table;
        }
    }
}
