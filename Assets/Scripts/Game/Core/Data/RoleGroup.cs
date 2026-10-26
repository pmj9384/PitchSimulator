using System;

namespace Game.Core.Data
{
    // 역할군(10-25 슬롯 격자): 포메이션 슬롯 교체는 같은 군 안에서만 허용한다(FM은 어디든 두고 적성 감점을 주지만 감점 값의 출처가 없어 1차는 제한으로).
    // GK / 수비(CB·FB) / 미드(DM·CM·AM) / 공격(W·ST). PlayerTable·FormationTemplates의 roleId 8종이 전부 여기 들어간다
    public enum RoleGroup { Keeper, Defender, Midfielder, Attacker }

    public static class RoleGroups
    {
        public static RoleGroup Of(string roleId)
        {
            switch (roleId.ToUpperInvariant())
            {
                case "GK": return RoleGroup.Keeper;
                case "CB":
                case "FB": return RoleGroup.Defender;
                case "DM":
                case "CM":
                case "AM": return RoleGroup.Midfielder;
                case "W":
                case "ST": return RoleGroup.Attacker;
                default: throw new ArgumentException($"[RoleGroups] 모르는 roleId: {roleId}");
            }
        }

        public static bool SameGroup(string roleIdA, string roleIdB)
        {
            return Of(roleIdA) == Of(roleIdB);
        }
    }
}
