using Game.Core.Data;

namespace Game.Core.Match
{
    // 경기 조립 단위: 선수 1명 + 자리 2쌍(공격 시/수비 시). 편성 행(StageEntry)·생성 팀·내 로스터가 전부 이걸로 떨어져 조립 경로가 하나다.
    // 09-27 League → Match로 이동: League(시즌)가 AutoMatch(조립)를 쓰는데 조립이 League 타입을 쓰면 순환이라(리뷰 D5)
    public readonly struct LineupSlot
    {
        public readonly PlayerStats Stats;
        public readonly float AttackX;
        public readonly float AttackZ;
        public readonly float DefendX;
        public readonly float DefendZ;

        public LineupSlot(PlayerStats stats, float attackX, float attackZ, float defendX, float defendZ)
        {
            Stats = stats;
            AttackX = attackX;
            AttackZ = attackZ;
            DefendX = defendX;
            DefendZ = defendZ;
        }
    }
}
