using System.IO;
using Game.Core.Data;
using Game.Core.Match;

// 테스트용 xT 격자. 시뮬은 격자를 생성자로 받으므로(전역 없음) 시뮬을 만드는 테스트가 여기서 꺼내 넘긴다.
public static class TestThreat
{
    private static ThreatGrid real;

    // 실제 ThreatTable.csv(게임이 쓰는 값 그대로). 시뮬·조립기 테스트의 기본
    public static ThreatGrid Real
    {
        get
        {
            if (real == null) { real = ThreatTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/ThreatTable.csv")); }
            return real;
        }
    }

    // 모든 칸 0: 운반·패스 이득이 항상 0이라 양보가 일어나지 않는다. 격자를 보지 않는 트리 테스트의 기본
    public static ThreatGrid Flat()
    {
        return new ThreatGrid(new float[ThreatGrid.Rows, ThreatGrid.Columns]);
    }
}
