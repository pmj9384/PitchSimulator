using System;
using System.Collections.Generic;
using Game.Core.Data;

namespace Game.Core.League
{
    // 전술판 격자(10-26 슬롯 격자 ②, FM 전술 화면처럼 칩을 격자에 세운다). 미터 좌표를 그대로 그리면 22m 폭 칩이 12~17m 라인 간격에 겹친다(10-25 유저 Play).
    // 템플릿의 x를 라인(열)으로 묶어 열을 균등하게, 열 안은 z 순서로 균등하게 둔다. 어느 포메이션이든 칩이 안 겹치는 것이 이 함수의 계약(FormationGridTests가 전 조합을 잠근다).
    // 좌표는 경기 좌표가 아니라 필드 그림 안 0~1 비율이다. 경기는 템플릿 미터 값(FormationRules.Position)을 그대로 쓴다
    public static class FormationGrid
    {
        public const float LineMergeMeters = 4f;   // [가정] x 차이가 이 안이면 같은 라인(4-4-2 FB −35·CB −36)
        public const float EdgeInset = 0.1f;        // 첫 열·끝 열, 위·아래 끝 칩의 중심이 가장자리에서 떨어진 비율. 칩 반쪽(약 0.1)이 필드 밖으로 안 나가게
        public const int MinSpreadSlots = 2;        // 열 안 간격 = 0.8 / max(인원 − 1, 2): 혼자면 가운데, 둘이면 0.3·0.7, 다섯이면 0.2 간격

        // [슬롯 − 1] = (u, v). u는 우리 골(0) → 상대 골(1), v는 아래(0) → 위(1)
        public static (float u, float v)[] Layout(FormationTemplate template, bool defending)
        {
            List<List<FormationSlot>> lines = GroupLines(template, defending);
            var result = new (float u, float v)[FormationTemplate.SlotCount];
            for (int c = 0; c < lines.Count; c++)
            {
                float u = ColumnU(c, lines.Count);
                List<FormationSlot> line = lines[c];
                float step = (1f - 2f * EdgeInset) / Math.Max(line.Count - 1, MinSpreadSlots);
                for (int k = 0; k < line.Count; k++)
                {
                    float v = 0.5f + (k - (line.Count - 1) * 0.5f) * step;
                    result[line[k].Slot - 1] = (u, v);
                }
            }
            return result;
        }

        private static float ColumnU(int column, int columnCount)
        {
            if (columnCount <= 1) { return 0.5f; }
            return EdgeInset + (1f - 2f * EdgeInset) * column / (columnCount - 1);
        }

        // x 오름차순(우리 골부터)으로 훑어 LineMergeMeters 안이면 같은 라인. 라인 안은 z 오름차순
        private static List<List<FormationSlot>> GroupLines(FormationTemplate template, bool defending)
        {
            var sorted = new List<FormationSlot>(template.Slots);
            sorted.Sort(defending ? CompareDefendX : CompareAttackX);
            var lines = new List<List<FormationSlot>>();
            float lineX = float.NegativeInfinity;
            for (int i = 0; i < sorted.Count; i++)
            {
                float x = defending ? sorted[i].PosX2 : sorted[i].PosX;
                if (x - lineX > LineMergeMeters)
                {
                    lines.Add(new List<FormationSlot>());
                    lineX = x;
                }
                lines[lines.Count - 1].Add(sorted[i]);
            }
            for (int i = 0; i < lines.Count; i++)
            {
                lines[i].Sort(defending ? CompareDefendZ : CompareAttackZ);
            }
            return lines;
        }

        private static int CompareAttackX(FormationSlot a, FormationSlot b) { return a.PosX.CompareTo(b.PosX); }
        private static int CompareDefendX(FormationSlot a, FormationSlot b) { return a.PosX2.CompareTo(b.PosX2); }
        private static int CompareAttackZ(FormationSlot a, FormationSlot b) { return a.PosZ.CompareTo(b.PosZ); }
        private static int CompareDefendZ(FormationSlot a, FormationSlot b) { return a.PosZ2.CompareTo(b.PosZ2); }
    }
}
