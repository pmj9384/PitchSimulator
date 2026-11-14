using System.Collections.Generic;
using Game.Core.Data;

namespace Game.Core.Placement
{
    // 관중석 단 하나(직선 블록). 가운데 선 기준 시작·끝, 앞 가장자리 높이. 프리팹 생성기가 조각 하나를 긴 큐브 하나로 만든다.
    // 선수 통로가 지나가는 단은 통로 좌·우 2조각으로 나뉜다(Piece 0 = −X 쪽, 1 = +X 쪽). 안 나뉜 단은 조각 0 하나
    public readonly struct StandRow
    {
        public readonly StandSide Side;
        public readonly int Index;          // 0 = 맨 앞 단
        public readonly int Piece;
        public readonly float FromX;
        public readonly float FromZ;
        public readonly float ToX;
        public readonly float ToZ;
        public readonly float Height;       // 이 단 윗면 높이(m). 좌석도 이 높이에 선다

        public StandRow(StandSide side, int index, int piece, float fromX, float fromZ, float toX, float toZ, float height)
        {
            Side = side;
            Index = index;
            Piece = piece;
            FromX = fromX;
            FromZ = fromZ;
            ToX = toX;
            ToZ = toZ;
            Height = height;
        }
    }

    // 선수 통로 자리(11-14 fix/tunnel-recess, 설계 실험/2026-11-12-설계-경기장-오브젝트.md 4-2). 먼 쪽 관중석 중앙, 낮은 단을 끊어 낸 홈.
    // 프리팹 생성기(홈 지붕·벽), Rows(좌석 제거), 뒤 태스크(퇴장 연출·중계 구도)가 이 값만 읽는다
    public readonly struct TunnelSpot
    {
        public readonly float HalfWidth;    // 홈 반폭(m). 단·기단·먼 쪽 광고판이 x = ±HalfWidth에서 끊긴다
        public readonly float MouthZ;       // 입구 z = 기단 앞면
        public readonly float BackZ;        // 뒤 벽 z = 끊지 않은 첫 단의 앞면
        public readonly float Height;       // 홈 지붕 아랫면 높이
        public readonly float RoofTop;      // 홈 지붕 윗면 높이
        public readonly int CutRows;        // 끊은 단 수(앞에서부터)

        public TunnelSpot(float halfWidth, float mouthZ, float backZ, float height, float roofTop, int cutRows)
        {
            HalfWidth = halfWidth;
            MouthZ = mouthZ;
            BackZ = backZ;
            Height = height;
            RoofTop = roofTop;
            CutRows = cutRows;
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
    // 면마다 단은 선(터치라인·골라인)에서 런오프만큼 떨어진 곳부터 뒤로 쌓인다. 길이는 선 길이 + 양쪽 런오프이고, 통로 면의 낮은 단은 TunnelOf 폭만큼 끊겨 2조각이다. 코너 관중석은 아직 없다(1부 설계의 "코너 연결"은 프리팹 단계)
    public static class StadiumLayout
    {
        public const float SeatSpacing = 0.5f;   // 좌석 간격(m). 설계 §1 좌석 수 계산과 같은 값
        public const float BaseFrontMargin = 0.2f;   // [가정] 기단이 첫 단 앞으로 내미는 폭(m). 통로 입구가 기단 앞면이라 프리팹 생성기와 같이 읽는다
        public const StandSide TunnelSide = StandSide.Far;   // 벤치가 있는 쪽(11-10 유저: FM26처럼 중앙에)
        public const float TunnelWidth = 4f;          // [가정] 통로 폭. FIFA Stadium Guidelines 5.3 권고는 4.5m(국제 대회용)
        public const float TunnelHeight = 2.4f;       // 통로 높이. FIFA Stadium Guidelines 5.3
        public const float TunnelSideMargin = 0.3f;   // [가정] 통로 양옆 여유. 홈 반폭 = 폭/2 + 이 값
        public const float TunnelRoofThickness = 0.2f;   // [가정]
        private const float HeightEps = 1e-3f;        // 단 윗면 2.4m(2부·1부 첫 단)가 부동소수로 2.4를 살짝 넘어도 끊는 단에 넣는다

        public static List<StandRow> Rows(StadiumSpec spec)
        {
            var rows = new List<StandRow>();
            TunnelSpot tunnel = TunnelOf(spec);
            for (int s = 0; s < spec.Stands.Count; s++)
            {
                StandSide side = spec.Stands[s];
                for (int i = 0; i < spec.Rows; i++)
                {
                    AddRow(rows, spec, tunnel, side, i);
                }
            }
            return rows;
        }

        // 통로는 먼 쪽 관중석이 있는 부에만 있다(지금 CSV는 4개 부 전부)
        public static bool HasTunnel(StadiumSpec spec)
        {
            for (int i = 0; i < spec.Stands.Count; i++)
            {
                if (spec.Stands[i] == TunnelSide) { return true; }
            }
            return false;
        }

        // 윗면이 통로 높이 이하인 앞쪽 단을 끊는다. 홈 지붕 윗면이 끊지 않은 첫 단 윗면을 넘지 않아 뒤 좌석을 가리지 않는다(4부·3부 2.6 = 2.6, 2부·1부 2.6 < 2.8)
        public static TunnelSpot TunnelOf(StadiumSpec spec)
        {
            int cut = CutRowCount(spec);
            float mouthZ = FieldBounds.HalfWidth + spec.Runoff - BaseFrontMargin;
            float backZ = FieldBounds.HalfWidth + spec.Runoff + cut * spec.RowDepth;
            float halfWidth = TunnelWidth * 0.5f + TunnelSideMargin;
            return new TunnelSpot(halfWidth, mouthZ, backZ, TunnelHeight, TunnelHeight + TunnelRoofThickness, cut);
        }

        private static int CutRowCount(StadiumSpec spec)
        {
            int cut = 0;
            while (cut < spec.Rows && RowTop(spec, cut) <= TunnelHeight + HeightEps)
            {
                cut++;
            }
            return cut;
        }

        private static float RowTop(StadiumSpec spec, int index)
        {
            return spec.BaseHeight + (index + 1) * spec.RowHeight;
        }

        // 터치라인 쪽 면(Far·Near)은 X축을 따라 달리고 골대 뒤 면은 Z축을 따라 달린다. 프리팹 생성기·프리팹 테스트가 같은 판정을 쓴다
        public static bool IsAlongX(StandSide side)
        {
            return side == StandSide.Far || side == StandSide.Near;
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

        // 통로 면의 끊는 단이면 −X 조각·+X 조각 2개, 아니면 통째로 1개
        private static void AddRow(List<StandRow> rows, StadiumSpec spec, TunnelSpot tunnel, StandSide side, int index)
        {
            StandRow whole = RowOf(spec, side, index);
            if (side != TunnelSide || index >= tunnel.CutRows)
            {
                rows.Add(whole);
                return;
            }

            rows.Add(new StandRow(side, index, 0, whole.FromX, whole.FromZ, -tunnel.HalfWidth, whole.ToZ, whole.Height));
            rows.Add(new StandRow(side, index, 1, tunnel.HalfWidth, whole.FromZ, whole.ToX, whole.ToZ, whole.Height));
        }

        // 한 면의 단 하나. 앞에서 i번째 단의 가운데 선은 선에서 runoff + (i + 0.5) × rowDepth 떨어져 있다
        private static StandRow RowOf(StadiumSpec spec, StandSide side, int index)
        {
            float offset = spec.Runoff + (index + 0.5f) * spec.RowDepth;
            float height = RowTop(spec, index);
            float halfAlongX = FieldBounds.HalfLength + spec.Runoff;   // 터치라인 쪽 면의 반 길이
            float halfAlongZ = FieldBounds.HalfWidth + spec.Runoff;    // 골대 뒤 면의 반 길이
            switch (side)
            {
                case StandSide.Far:
                    return new StandRow(side, index, 0, -halfAlongX, FieldBounds.HalfWidth + offset, halfAlongX, FieldBounds.HalfWidth + offset, height);
                case StandSide.Near:
                    return new StandRow(side, index, 0, -halfAlongX, -FieldBounds.HalfWidth - offset, halfAlongX, -FieldBounds.HalfWidth - offset, height);
                case StandSide.EndLeft:
                    return new StandRow(side, index, 0, -FieldBounds.HalfLength - offset, -halfAlongZ, -FieldBounds.HalfLength - offset, halfAlongZ, height);
                default:
                    return new StandRow(side, index, 0, FieldBounds.HalfLength + offset, -halfAlongZ, FieldBounds.HalfLength + offset, halfAlongZ, height);
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
