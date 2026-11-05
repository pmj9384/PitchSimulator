using System;
using System.Collections.Generic;

namespace Game.Core.Placement
{
    // 경기장 선의 한 줄(가운데 선 기준 시작·끝, 폭은 FieldBounds.LineWidth)
    public readonly struct PitchLine
    {
        public readonly float FromX;
        public readonly float FromZ;
        public readonly float ToX;
        public readonly float ToZ;

        public PitchLine(float fromX, float fromZ, float toX, float toZ)
        {
            FromX = fromX;
            FromZ = fromZ;
            ToX = toX;
            ToZ = toZ;
        }

        public float Length => (float)Math.Sqrt((ToX - FromX) * (ToX - FromX) + (ToZ - FromZ) * (ToZ - FromZ));
    }

    // 경기장 표시(11-05, IFAB 1조). 화면의 선·골대가 시뮬이 판정하는 박스·골대와 같은 숫자(FieldBounds)를 쓰도록 한 곳에서 뽑는다.
    // 경기장 프리팹 4종(부별)이 같은 공통 피치를 넣고, 에디터 메뉴(PitchPrefabBuilder)가 이 목록으로 프리팹을 만든다
    public static class PitchLayout
    {
        public const int CircleSegments = 48;   // 센터서클 한 바퀴를 나누는 직선 수(지름 18.3m에서 한 칸 약 1.2m)

        public static List<PitchLine> Lines()
        {
            var lines = new List<PitchLine>();
            float hl = FieldBounds.HalfLength;
            float hw = FieldBounds.HalfWidth;
            lines.Add(new PitchLine(-hl, -hw, hl, -hw));   // 터치라인 2
            lines.Add(new PitchLine(-hl, hw, hl, hw));
            lines.Add(new PitchLine(-hl, -hw, -hl, hw));   // 골라인 2
            lines.Add(new PitchLine(hl, -hw, hl, hw));
            lines.Add(new PitchLine(0f, -hw, 0f, hw));     // 하프라인
            AddCircle(lines, 0f, 0f, FieldBounds.CenterCircleRadius, 0f, 360f);
            for (int side = -1; side <= 1; side += 2)
            {
                AddBox(lines, side, FieldBounds.PenaltyBoxDepth, FieldBounds.PenaltyBoxHalfWidth);
                AddBox(lines, side, FieldBounds.GoalAreaDepth, FieldBounds.GoalAreaHalfWidth);
                AddPenaltyArc(lines, side);
            }
            return lines;
        }

        // 센터 마크와 페널티 마크 2개
        public static List<(float x, float z)> Spots()
        {
            float spotX = FieldBounds.HalfLength - FieldBounds.PenaltySpotDistance;
            return new List<(float x, float z)> { (0f, 0f), (-spotX, 0f), (spotX, 0f) };
        }

        // 골대 포스트 4개의 바닥 중심. 안쪽 면 사이가 7.32m(IFAB 1조)라 중심은 GoalHalfWidth + 반 굵기. 시뮬 xG는 안쪽 면(GoalHalfWidth)을 쓴다.
        // 크로스바는 같은 쪽 두 포스트를 GoalHeight에서 잇는다
        public static List<(float x, float z)> GoalPosts()
        {
            float hl = FieldBounds.HalfLength;
            float gw = FieldBounds.GoalHalfWidth + FieldBounds.GoalPostDiameter * 0.5f;
            return new List<(float x, float z)> { (-hl, -gw), (-hl, gw), (hl, -gw), (hl, gw) };
        }

        // 골라인에서 depth만큼 들어간 박스의 세 변(골라인 쪽 변은 골라인이 맡는다). side −1 = 왼쪽(−X) 골
        private static void AddBox(List<PitchLine> lines, int side, float depth, float halfWidth)
        {
            float lineX = side * FieldBounds.HalfLength;
            float frontX = side * (FieldBounds.HalfLength - depth);
            lines.Add(new PitchLine(frontX, -halfWidth, frontX, halfWidth));
            lines.Add(new PitchLine(lineX, -halfWidth, frontX, -halfWidth));
            lines.Add(new PitchLine(lineX, halfWidth, frontX, halfWidth));
        }

        // 페널티 아크: 마크에서 반지름 9.15m 원 중 박스 밖 부분. 박스 앞 선과 만나는 각을 구해 그 사이만 그린다
        private static void AddPenaltyArc(List<PitchLine> lines, int side)
        {
            float spotX = side * (FieldBounds.HalfLength - FieldBounds.PenaltySpotDistance);
            float reach = FieldBounds.PenaltyBoxDepth - FieldBounds.PenaltySpotDistance;   // 마크에서 박스 앞 선까지 5.5m
            float half = (float)(Math.Acos(reach / FieldBounds.PenaltyArcRadius) * 180.0 / Math.PI);
            float center = side > 0 ? 180f : 0f;   // 필드 안쪽(중앙 쪽)을 향하는 각
            AddCircle(lines, spotX, 0f, FieldBounds.PenaltyArcRadius, center - half, center + half);
        }

        private static void AddCircle(List<PitchLine> lines, float cx, float cz, float radius, float fromDeg, float toDeg)
        {
            int segments = Math.Max(2, (int)Math.Ceiling(CircleSegments * (toDeg - fromDeg) / 360f));
            float step = (toDeg - fromDeg) / segments;
            for (int i = 0; i < segments; i++)
            {
                (float x, float z) a = PointOnCircle(cx, cz, radius, fromDeg + step * i);
                (float x, float z) b = PointOnCircle(cx, cz, radius, fromDeg + step * (i + 1));
                lines.Add(new PitchLine(a.x, a.z, b.x, b.z));
            }
        }

        private static (float x, float z) PointOnCircle(float cx, float cz, float radius, float deg)
        {
            double rad = deg * Math.PI / 180.0;
            return (cx + radius * (float)Math.Cos(rad), cz + radius * (float)Math.Sin(rad));
        }
    }
}
