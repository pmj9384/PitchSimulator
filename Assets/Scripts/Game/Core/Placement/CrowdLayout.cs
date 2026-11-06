using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;

namespace Game.Core.Placement
{
    // 관중 배치(11-06 경기장 4종 4단계, 설계 §3). 좌석 슬롯(StadiumLayout.Seats) 중 채움 해시(StadiumRules.IsFilled)를 통과한 자리만 관중이 선다.
    // 좌석 번호는 Seats 전체 순서의 번호라서 면·단을 바꿔도 다른 좌석의 빈자리가 안 흔들린다. 화면 전용
    public static class CrowdLayout
    {
        // MaxInstancesPerCall·CallCount·CallSize·SideBox는 Graphics API·UnityEngine 타입 없이 산술만이라 Game.Core에 둔다(dotnet 러너에서도 검증하려고)
        public const int MaxInstancesPerCall = 1023;   // RenderMeshInstanced 한 호출의 상한(assumeuniformscaling이면 worldToObject를 안 올려 1,023. 아니면 511)

        // 한 면의 채운 좌석을 전부 덮는 상자(루트 로컬 좌표). 가로·깊이는 좌석 범위에 padding, 높이는 아래로 padding·위로 쿼드 높이 + padding
        public readonly struct SeatBox
        {
            public readonly float MinX;
            public readonly float MinY;
            public readonly float MinZ;
            public readonly float MaxX;
            public readonly float MaxY;
            public readonly float MaxZ;

            public SeatBox(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
            {
                MinX = minX;
                MinY = minY;
                MinZ = minZ;
                MaxX = maxX;
                MaxY = maxY;
                MaxZ = maxZ;
            }

            public bool Contains(float x, float y, float z)
            {
                if (x < MinX || x > MaxX) { return false; }
                if (y < MinY || y > MaxY) { return false; }
                return z >= MinZ && z <= MaxZ;
            }
        }

        public static SeatBox SideBox(List<SeatSlot> filled, StandSide side, float quadHeight, float padding)
        {
            bool any = false;
            float minX = 0f;
            float minY = 0f;
            float minZ = 0f;
            float maxX = 0f;
            float maxY = 0f;
            float maxZ = 0f;
            for (int i = 0; i < filled.Count; i++)
            {
                SeatSlot seat = filled[i];
                if (seat.Side != side) { continue; }

                if (!any)
                {
                    minX = seat.X;
                    maxX = seat.X;
                    minY = seat.Y;
                    maxY = seat.Y;
                    minZ = seat.Z;
                    maxZ = seat.Z;
                    any = true;
                    continue;
                }
                if (seat.X < minX) { minX = seat.X; }
                if (seat.X > maxX) { maxX = seat.X; }
                if (seat.Y < minY) { minY = seat.Y; }
                if (seat.Y > maxY) { maxY = seat.Y; }
                if (seat.Z < minZ) { minZ = seat.Z; }
                if (seat.Z > maxZ) { maxZ = seat.Z; }
            }
            return new SeatBox(minX - padding, minY - padding, minZ - padding, maxX + padding, maxY + quadHeight + padding, maxZ + padding);
        }

        public static List<SeatSlot> FilledSeats(StadiumSpec spec, uint fillSeed)
        {
            List<SeatSlot> seats = StadiumLayout.Seats(spec);
            var filled = new List<SeatSlot>(seats.Count);
            for (int i = 0; i < seats.Count; i++)
            {
                if (StadiumRules.IsFilled(fillSeed, i, spec.FillPercent))
                {
                    filled.Add(seats[i]);
                }
            }
            return filled;
        }

        // 면을 따라 선 관중 쿼드가 경기장 가운데를 보게 하는 Y축 회전(도). 쿼드의 보이는 면은 로컬 -Z다
        // 먼 쪽(+Z)은 -Z를 보고(0도), 가까운 쪽은 +Z(180도), 왼쪽 골대 뒤(−X)는 +X(−90도), 오른쪽(+X)은 −X(90도)
        public static float FacingYaw(StandSide side)
        {
            switch (side)
            {
                case StandSide.Far: return 0f;
                case StandSide.Near: return 180f;
                case StandSide.EndLeft: return -90f;
                default: return 90f;
            }
        }

        // 인스턴스 n개를 한 호출 상한으로 나눈 호출 수
        public static int CallCount(int instances)
        {
            return (instances + MaxInstancesPerCall - 1) / MaxInstancesPerCall;
        }

        // call번째(0부터) 호출이 그리는 인스턴스 수. 마지막만 나머지
        public static int CallSize(int instances, int call)
        {
            int remaining = instances - call * MaxInstancesPerCall;
            return remaining < MaxInstancesPerCall ? remaining : MaxInstancesPerCall;
        }
    }
}
