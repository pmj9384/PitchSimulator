using System;
using Game.Core.Placement;

namespace Game.Core.Match
{
    // xT(Expected Threat) 값 격자(11-03 ⑤ xT). 12열(공격 방향) × 8행(폭). 값은 ThreatTable.csv(ThreatTableParser)에서 온다.
    // 출처: Karun Singh, "Introducing Expected Threat (xT)" 2018의 공식을 쓰되 값은 이 게임 시뮬 이벤트로 직접 생성했다(공개 격자는 라이선스가 불명확해 안 씀)
    public sealed class ThreatGrid
    {
        public const int Columns = 12;
        public const int Rows = 8;

        private readonly float[,] cells;   // [행, 열]

        public ThreatGrid(float[,] cells)
        {
            if (cells.GetLength(0) != Rows || cells.GetLength(1) != Columns)
            {
                throw new ArgumentException($"[ThreatGrid] {Rows}행 {Columns}열이어야 한다 ({cells.GetLength(0)}행 {cells.GetLength(1)}열)");
            }
            this.cells = (float[,])cells.Clone();
        }

        public float Cell(int row, int column)
        {
            return cells[row, column];
        }

        // 그 점의 위협 값. 공격 방향 좌표로 바꿔(x × attackSign) 칸 중심 사이를 이중선형 보간한다.
        // 보간하는 이유: 칸 경계(필드 8.75m × 8.5m)를 넘는 순간 값이 계단으로 튀면 운반·패스 비교가 경계에서 뒤집힌다. 가장자리 바깥은 가장 가까운 칸 중심 값
        public float Value(float x, float z, int attackSign)
        {
            float u = (x * attackSign + FieldBounds.HalfLength) / (2f * FieldBounds.HalfLength) * Columns - 0.5f;
            float v = (z + FieldBounds.HalfWidth) / (2f * FieldBounds.HalfWidth) * Rows - 0.5f;
            u = Math.Clamp(u, 0f, Columns - 1);
            v = Math.Clamp(v, 0f, Rows - 1);

            int c0 = Math.Min((int)u, Columns - 2);
            int r0 = Math.Min((int)v, Rows - 2);
            float fc = u - c0;
            float fr = v - r0;
            float top = cells[r0, c0] * (1f - fc) + cells[r0, c0 + 1] * fc;
            float bottom = cells[r0 + 1, c0] * (1f - fc) + cells[r0 + 1, c0 + 1] * fc;
            return top * (1f - fr) + bottom * fr;
        }
    }

    // 운반·패스의 위협 증가 비교(11-03 ⑤ xT, 원문 모델: 이동이 성공해야 도착 칸 값을 얻고 실패하면 0). 상태 없음
    public static class ThreatRules
    {
        // 운반 가치 = 운반 목표점 값 − 지금 값. 운반 구간엔 상대가 없는 것을 ②c가 확인하므로 뺏길 위험은 넣지 않는다
        public static float CarryGain(ThreatGrid grid, float fromX, float fromZ, float toX, float toZ, int attackSign)
        {
            return grid.Value(toX, toZ, attackSign) - grid.Value(fromX, fromZ, attackSign);
        }

        // 패스 가치 = (1 − 스루 위험) × 런 지점 값 − 지금 값. 실패하면 소유를 잃어 도착 값이 0(원문이 실패한 이동을 모델에서 빼는 것과 같다)
        public static float PassGain(ThreatGrid grid, float fromX, float fromZ, float spotX, float spotZ, int attackSign, float throughRisk)
        {
            return (1f - throughRisk) * grid.Value(spotX, spotZ, attackSign) - grid.Value(fromX, fromZ, attackSign);
        }
    }
}
