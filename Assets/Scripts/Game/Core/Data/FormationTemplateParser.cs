using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;
using Game.Core.Placement;

namespace Game.Core.Data
{
    // FormationTemplates.csv → 템플릿 목록. 다른 파서와 같은 틀(CsvHelper, 규칙 검증만 여기서).
    // 검증: 템플릿마다 정확히 11슬롯(1~11), GK 1명, 자기 진영(x < 0)·필드 안, 슬롯끼리 최소 간격, 역할 id 8종
    public static class FormationTemplateParser
    {
        private static readonly HashSet<string> Roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GK", "CB", "FB", "DM", "CM", "AM", "W", "ST" };

        public static List<FormationTemplate> Parse(string csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText)) { throw new FormatException("FormationTemplates: 내용이 비어 있다"); }

            var byId = new Dictionary<string, List<FormationSlot>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            try
            {
                using var reader = new StringReader(csvText);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                csv.Context.RegisterClassMap<RowMap>();
                foreach (Row row in csv.GetRecords<Row>())
                {
                    int line = csv.Parser.Row;
                    if (string.IsNullOrWhiteSpace(row.FormationId)) { throw new FormatException($"FormationTemplates {line}행: formationId가 비어 있다"); }
                    if (!Roles.Contains(row.RoleId)) { throw new FormatException($"FormationTemplates {line}행: roleId는 GK·CB·FB·DM·CM·AM·W·ST ({row.RoleId})"); }
                    if (row.Slot < 1 || row.Slot > FormationTemplate.SlotCount) { throw new FormatException($"FormationTemplates {line}행: slot은 1~11 ({row.Slot})"); }
                    if (!byId.TryGetValue(row.FormationId, out List<FormationSlot> slots))
                    {
                        slots = new List<FormationSlot>();
                        byId[row.FormationId] = slots;
                        order.Add(row.FormationId);
                    }
                    slots.Add(new FormationSlot { Slot = row.Slot, RoleId = row.RoleId.ToUpperInvariant(), PosX = row.PosX, PosZ = row.PosZ, PosX2 = row.PosX2, PosZ2 = row.PosZ2 });
                }
            }
            catch (HeaderValidationException ex)
            {
                throw new FormatException("FormationTemplates: 헤더가 스키마와 다르다. " + CsvParseHelper.FirstLine(ex.Message));
            }
            catch (CsvHelperException ex)
            {
                int line = ex.Context?.Parser?.Row ?? 0;
                throw new FormatException($"FormationTemplates {line}행: 값 형식 오류. " + CsvParseHelper.FirstLine(ex.Message));
            }
            if (order.Count == 0) { throw new FormatException("FormationTemplates: 데이터 행이 없다 (헤더만 있음)"); }

            var templates = new List<FormationTemplate>(order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                List<FormationSlot> slots = byId[order[i]];
                Validate(order[i], slots);
                slots.Sort((a, b) => a.Slot.CompareTo(b.Slot));
                templates.Add(new FormationTemplate(order[i], slots));
            }
            return templates;
        }

        private static void Validate(string id, List<FormationSlot> slots)
        {
            if (slots.Count != FormationTemplate.SlotCount) { throw new FormatException($"FormationTemplates {id}: 슬롯이 11이 아니다 ({slots.Count})"); }
            int keepers = 0;
            var seenSlots = new HashSet<int>();
            float limitX = FieldBounds.HalfLength - FieldBounds.EdgeMargin;
            float limitZ = FieldBounds.HalfWidth - FieldBounds.EdgeMargin;
            for (int i = 0; i < slots.Count; i++)
            {
                FormationSlot s = slots[i];
                if (!seenSlots.Add(s.Slot)) { throw new FormatException($"FormationTemplates {id}: slot {s.Slot} 중복"); }
                if (string.Equals(s.RoleId, "GK", StringComparison.OrdinalIgnoreCase)) { keepers++; }   // 슬롯은 PlayerStats가 아니라 roleId 문자열. Roles 집합과 같은 IgnoreCase(10-08 리뷰)
                if (s.PosX >= 0f || s.PosX2 >= 0f) { throw new FormatException($"FormationTemplates {id} slot {s.Slot}: 자기 진영(x < 0)이어야 한다"); }
                if (s.PosX < -limitX || s.PosX2 < -limitX || Math.Abs(s.PosZ) > limitZ || Math.Abs(s.PosZ2) > limitZ) { throw new FormatException($"FormationTemplates {id} slot {s.Slot}: 필드 밖"); }
                for (int j = 0; j < i; j++)
                {
                    float dx = slots[j].PosX - s.PosX;
                    float dz = slots[j].PosZ - s.PosZ;
                    if (dx * dx + dz * dz < FieldBounds.MinSpacing * FieldBounds.MinSpacing) { throw new FormatException($"FormationTemplates {id}: slot {slots[j].Slot}·{s.Slot} 간격이 {FieldBounds.MinSpacing}m 미만"); }
                }
            }
            if (keepers != 1) { throw new FormatException($"FormationTemplates {id}: GK가 1명이어야 한다 ({keepers})"); }
        }

        private sealed class Row
        {
            public string FormationId { get; set; } = string.Empty;
            public int Slot { get; set; }
            public string RoleId { get; set; } = string.Empty;
            public float PosX { get; set; }
            public float PosZ { get; set; }
            public float PosX2 { get; set; }
            public float PosZ2 { get; set; }
        }

        private sealed class RowMap : ClassMap<Row>
        {
            public RowMap()
            {
                Map(r => r.FormationId).Name("formationId");
                Map(r => r.Slot).Name("slot");
                Map(r => r.RoleId).Name("roleId");
                Map(r => r.PosX).Name("posX");
                Map(r => r.PosZ).Name("posZ");
                Map(r => r.PosX2).Name("posX2");
                Map(r => r.PosZ2).Name("posZ2");
            }
        }
    }
}
