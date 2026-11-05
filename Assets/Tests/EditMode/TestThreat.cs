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

    // 앞으로 갈수록 오르는 단조 격자(열 × 0.04, 폭 무관). 뒤·옆 후보의 도착 xT가 늘 낮아서 "앞 후보가 값이 큰" 장면을 요철 없이 만든다(11-03 P1).
    // 실제 격자는 레인마다 요철이 있어 비슷한 깊이의 후보끼리 값이 흔들린다
    public static ThreatGrid Slope()
    {
        var cells = new float[ThreatGrid.Rows, ThreatGrid.Columns];
        for (int r = 0; r < ThreatGrid.Rows; r++)
        {
            for (int c = 0; c < ThreatGrid.Columns; c++) { cells[r, c] = c * 0.04f; }
        }
        return new ThreatGrid(cells);
    }

    // 침투 시뮬 테스트용 둔덕 격자(11-03 P1+): 모든 칸 0.3이라 슛과 제자리 운반은 지고, 열 9~10 · 행 6~7(박스 옆 측면 깊은 곳, 이 장면의 런 지점)만 0.6이라
    // 그 점으로 가는 런 패스의 이득(성공 확률 × 0.3)이 가장 크다. 안전하지 않은 패스는 음수라 운반자가 런 제안을 기다린 만큼 공을 쥐는 효과가 난다
    public static ThreatGrid Step()
    {
        var cells = new float[ThreatGrid.Rows, ThreatGrid.Columns];
        for (int r = 0; r < ThreatGrid.Rows; r++)
        {
            for (int c = 0; c < ThreatGrid.Columns; c++) { cells[r, c] = 0.3f; }
        }
        for (int r = 6; r <= 7; r++)
        {
            cells[r, 9] = 0.6f;
            cells[r, 10] = 0.6f;
        }
        return new ThreatGrid(cells);
    }

    // 모든 칸 0.5: 지금 값이 커서 슛(xG × 배율 − 0.5)과 운반(위험이 있으면 음수)은 지고, 안전한 패스만 적합도 가산만큼 양수다(11-03 P1+).
    // 평평한 격자(0)에선 슛이 늘 양수라 슛이 먼저 나간다
    public static ThreatGrid Plateau()
    {
        var cells = new float[ThreatGrid.Rows, ThreatGrid.Columns];
        for (int r = 0; r < ThreatGrid.Rows; r++)
        {
            for (int c = 0; c < ThreatGrid.Columns; c++) { cells[r, c] = 0.5f; }
        }
        return new ThreatGrid(cells);
    }
}
