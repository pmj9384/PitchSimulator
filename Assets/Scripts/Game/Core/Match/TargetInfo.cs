namespace Game.Core.Match
{
    // "가장 가까운 선수" 후보 1명의 최소 정보. 선정에 필요한 것만 담는다(팀·스탯은 선정과 무관하므로 없음).
    public struct TargetInfo
    {
        public int PlayerId;     // 런타임 발급 선수 번호(PlayerManager가 스폰 순서로 발급). CSV의 roleId와 무관.
                                 // 동률 타이브레이크 기준(GetInstanceID는 실행마다 달라 금지)
        public float X;
        public float Z;          // 필드는 평면 105×68m. 높이(y)는 판정에 안 쓴다

        public TargetInfo(int playerId, float x, float z)
        {
            PlayerId = playerId;
            X = x;
            Z = z;
        }
    }
}
