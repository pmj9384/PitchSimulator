using System.Collections.Generic;

namespace Game.Core.Data
{
    // FormationTemplates.csv 한 슬롯. 팀0 기준 좌표(자기 진영 x < 0, 킥오프 위치). 팀1은 생성기가 X를 뒤집는다.
    // roleId(GK·CB·FB·DM·CM·AM·W·ST)만 적고 변형(variantId)은 TeamGenerator(09-27)가 부 규칙대로 고른다
    public sealed class FormationSlot
    {
        public int Slot { get; set; }
        public string RoleId { get; set; } = string.Empty;
        public float PosX { get; set; }
        public float PosZ { get; set; }
        public float PosX2 { get; set; }
        public float PosZ2 { get; set; }
    }

    public sealed class FormationTemplate
    {
        public const int SlotCount = 11;

        public string Id { get; }
        public IReadOnlyList<FormationSlot> Slots { get; }

        public FormationTemplate(string id, List<FormationSlot> slots)
        {
            Id = id;
            Slots = slots;
        }
    }
}
