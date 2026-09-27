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
    }
}
