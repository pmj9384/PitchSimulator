using System;
using System.Collections.Generic;

namespace Game.Core.Data
{
    // PlayerTable 조회 규칙 한 곳(09-27 리뷰 Y3): variantId는 대소문자를 무시한다(파서의 중복 검사·PlayerTableRepository의 Dictionary와 같은 기준).
    // 조립기(MatchAssembler)와 세이브 복원(SeasonState.FromSave)이 같은 함수를 부른다
    public static class PlayerTableLookup
    {
        public static PlayerStats? FindVariant(IReadOnlyList<PlayerStats> table, string variantId)
        {
            for (int i = 0; i < table.Count; i++)
            {
                if (string.Equals(table[i].VariantId, variantId, StringComparison.OrdinalIgnoreCase)) { return table[i]; }
            }
            return null;
        }

        // 그 자리의 선수에게 새로 줄 수 있는 역할인가(09-30): 같은 자리이고 노출된 역할. 전술 화면의 역할 목록(ExposedVariants)과
        // 역할 변경(SeasonState.SetPlayerRole)이 이 조건 하나를 같이 쓴다. 목록에 보이는 역할과 실제로 받아 주는 역할이 갈리지 않게
        public static bool IsAssignable(PlayerStats variant, string roleId)
        {
            if (!variant.Exposed) { return false; }
            return IsSamePosition(variant, roleId);
        }

        // 그 자리의 역할인가. 이미 쓰고 있는 역할을 지키는 데는 노출을 묻지 않는다(10-08: 업데이트로 역할이 잠겨도 세이브·시즌 이월의 역할과 개인 지시는 남는다. FM·FC처럼 "내가 준 건 남는다")
        public static bool IsSamePosition(PlayerStats variant, string roleId)
        {
            return string.Equals(variant.RoleId, roleId, StringComparison.OrdinalIgnoreCase);
        }

        // 같은 자리의 노출된 역할(전술 화면의 역할 목록). CSV 행 순서 그대로
        public static List<PlayerStats> ExposedVariants(IReadOnlyList<PlayerStats> table, string roleId)
        {
            var variants = new List<PlayerStats>();
            for (int i = 0; i < table.Count; i++)
            {
                if (IsAssignable(table[i], roleId)) { variants.Add(table[i]); }
            }
            return variants;
        }
    }
}
