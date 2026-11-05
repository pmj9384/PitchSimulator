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

        // 같은 폭 z에서 x가 fromX~toX일 때 위협 값의 최댓값. 이중선형 보간은 x 방향으로 열 중심에서 꺾이므로 양 끝과 사이의 열 중심만 보면 된다.
        // 후보 패스 값의 상한(가지치기)이 쓴다
        public float MaxAlongX(float fromX, float toX, float z, int attackSign)
        {
            float low = Math.Min(fromX * attackSign, toX * attackSign);
            float high = Math.Max(fromX * attackSign, toX * attackSign);
            float best = Math.Max(Value(low * attackSign, z, attackSign), Value(high * attackSign, z, attackSign));
            float columnWidth = 2f * FieldBounds.HalfLength / Columns;
            for (int c = 0; c < Columns; c++)
            {
                float center = -FieldBounds.HalfLength + (c + 0.5f) * columnWidth;
                if (center <= low || center >= high) { continue; }
                best = Math.Max(best, Value(center * attackSign, z, attackSign));
            }
            return best;
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

    // 슛·운반·패스 행동 가치(11-03 P1, 원문 xT 모델: 이동이 성공해야 도착 칸 값을 얻고 실패하면 0). 상태 없음.
    // 값 = 성공 확률 × 도착 값 − 지금 값. 지금 값은 모든 행동에 똑같이 빠지므로 행동끼리의 순서는 안 바꾼다. 보유(값 0) 기준선은 없고 가장 큰 값을 늘 실행한다(11-03 P1+)
    public static class ThreatRules
    {
        // 운반 가치. carryRisk = DribbleRules.CarryRisk
        public static float CarryValue(ThreatGrid grid, float fromX, float fromZ, float toX, float toZ, int attackSign, float carryRisk)
        {
            return (1f - carryRisk) * grid.Value(toX, toZ, attackSign) - grid.Value(fromX, fromZ, attackSign);
        }

        // 슛 가치. 슛은 소유를 끝내는 행동이라 성공 확률(압박 보정한 xG)에 선수 슛 성향 배율(dial)을 곱한 값이 도착 값이다. 지금 값을 빼는 건 운반·패스와 같다
        public static float ShotValue(ThreatGrid grid, float fromX, float fromZ, int attackSign, float shotChance, float dial)
        {
            return shotChance * dial - grid.Value(fromX, fromZ, attackSign);
        }

        // 패스 가치. success = 성공 확률(1 − 위험에 허용치 띠를 곱한 PassRules.SafetyWeight), fitBonus = 거리 적합·측면 가중(성공했을 때만 얻는다)
        public static float PassValue(ThreatGrid grid, float fromX, float fromZ, float landingX, float landingZ, int attackSign, float success, float fitBonus)
        {
            return success * (grid.Value(landingX, landingZ, attackSign) + fitBonus) - grid.Value(fromX, fromZ, attackSign);
        }
    }
}
