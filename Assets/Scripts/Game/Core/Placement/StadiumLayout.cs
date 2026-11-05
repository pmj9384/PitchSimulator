using System.Collections.Generic;
using Game.Core.Data;

namespace Game.Core.Placement
{
    // 관중석 단 하나(직선 블록). 가운데 선 기준 시작·끝, 앞 가장자리 높이. 프리팹 생성기가 단 하나를 긴 큐브 하나로 만든다
    public readonly struct StandRow
    {
        public readonly StandSide Side;
        public readonly int Index;          // 0 = 맨 앞 단
        public readonly float FromX;
        public readonly float FromZ;
        public readonly float ToX;
        public readonly float ToZ;
        public readonly float Height;       // 이 단 윗면 높이(m). 좌석도 이 높이에 선다

        public StandRow(StandSide side, int index, float fromX, float fromZ, float toX, float toZ, float height)
        {
            Side = side;
            Index = index;
            FromX = fromX;
            FromZ = fromZ;
            ToX = toX;
            ToZ = toZ;
            Height = height;
        }
    }

    // 좌석 하나의 자리. 관중 인스턴스가 여기 선다(관중 렌더러가 StadiumRules로 채울지 정한다)
    public readonly struct SeatSlot
    {
        public readonly StandSide Side;
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public SeatSlot(StandSide side, float x, float y, float z)
        {
            Side = side;
            X = x;
            Y = y;
            Z = z;
        }
    }

    // 경기장 관중석 배치(11-06 경기장 4종 1단계). StadiumSpec(CSV)과 FieldBounds만으로 단·좌석 자리를 뽑는다. 화면 전용, 시뮬은 안 읽는다.
    // 같은 함수가 프리팹의 단 큐브와 관중 자리를 같이 내서 좌석과 관중이 어긋날 수 없다(PitchLayout과 같은 이유).
    // 면마다 단은 선(터치라인·골라인)에서 런오프만큼 떨어진 곳부터 뒤로 쌓인다. 길이는 선 길이 + 양쪽 런오프. 코너 관중석은 아직 없다(1부 설계의 "코너 연결"은 프리팹 단계)
    public static class StadiumLayout
    {
        public const float SeatSpacing = 0.5f;   // 좌석 간격(m). 설계 §1 좌석 수 계산과 같은 값

        public static List<StandRow> Rows(StadiumSpec spec)
        {
            var rows = new List<StandRow>();
            for (int s = 0; s < spec.Stands.Count; s++)
            {
                StandSide side = spec.Stands[s];
                for (int i = 0; i < spec.Rows; i++)
                {
                    rows.Add(RowOf(spec, side, i));
                }
            }
            return rows;
        }

        public static List<SeatSlot> Seats(StadiumSpec spec)
        {
            var seats = new List<SeatSlot>();
            List<StandRow> rows = Rows(spec);
            for (int r = 0; r < rows.Count; r++)
            {
                AddSeats(seats, rows[r]);
            }
            return seats;
        }

        // 한 면의 단 하나. 앞에서 i번째 단의 가운데 선은 선에서 runoff + (i + 0.5) × rowDepth 떨어져 있다
        private static StandRow RowOf(StadiumSpec spec, StandSide side, int index)
        {
            float offset = spec.Runoff + (index + 0.5f) * spec.RowDepth;
            float height = spec.BaseHeight + (index + 1) * spec.RowHeight;
            float halfAlongX = FieldBounds.HalfLength + spec.Runoff;   // 터치라인 쪽 면의 반 길이
            float halfAlongZ = FieldBounds.HalfWidth + spec.Runoff;    // 골대 뒤 면의 반 길이
            switch (side)
            {
                case StandSide.Far:
                    return new StandRow(side, index, -halfAlongX, FieldBounds.HalfWidth + offset, halfAlongX, FieldBounds.HalfWidth + offset, height);
                case StandSide.Near:
                    return new StandRow(side, index, -halfAlongX, -FieldBounds.HalfWidth - offset, halfAlongX, -FieldBounds.HalfWidth - offset, height);
                case StandSide.EndLeft:
                    return new StandRow(side, index, -FieldBounds.HalfLength - offset, -halfAlongZ, -FieldBounds.HalfLength - offset, halfAlongZ, height);
                default:
                    return new StandRow(side, index, FieldBounds.HalfLength + offset, -halfAlongZ, FieldBounds.HalfLength + offset, halfAlongZ, height);
            }
        }

        // 단 길이를 좌석 간격으로 나눈 칸마다 하나. 칸의 가운데에 앉는다
        private static void AddSeats(List<SeatSlot> seats, StandRow row)
        {
            float dx = row.ToX - row.FromX;
            float dz = row.ToZ - row.FromZ;
            float length = (float)System.Math.Sqrt(dx * dx + dz * dz);
            int count = (int)(length / SeatSpacing);
            for (int k = 0; k < count; k++)
            {
                float t = (k + 0.5f) / count;
                seats.Add(new SeatSlot(row.Side, row.FromX + dx * t, row.Height, row.FromZ + dz * t));
            }
        }
    }
}
