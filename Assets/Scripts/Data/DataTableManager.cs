using UnityEngine;

// 게임 데이터 표 접근층. 스킨 표는 제거(2026-09-15). 게임 규칙 CSV(PlayerTable 등)는 CsvHelper 파서 + Game.Core 리포지토리 관례로 따로 둔다.
public static class DataTableManager
{
    private static T LoadTable<T>(string filename) where T : DataTable, new()
    {
        var table = new T();
        table.Load(filename);
        return table;
    }
}
