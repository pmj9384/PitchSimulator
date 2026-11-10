namespace Game.Core.Match
{
    // "가장 가까운 선수" 후보 1명의 최소 정보. 선정에 필요한 것만 담는다(팀·스탯은 선정과 무관하므로 없음).
    public struct TargetInfo
    {
        public int PlayerId;     // 런타임 발급 선수 번호(MatchAssembler가 등록 순서로 발급). CSV의 roleId와 무관.
                                 // 동률 타이브레이크 기준(GetInstanceID는 실행마다 달라 금지)
        public float X;
        public float Z;          // 필드는 평면 105×68m. 높이(y)는 판정에 안 쓴다
        public float VelX;       // 지금 속도(m/s). 리드 롤아웃(PassRules.PickLeadLanding)이 동료가 이미 달리는 속도를 본다(11-10 A2). 시뮬 스냅샷만 채우고, 선정용 후보는 0
        public float VelZ;

        public TargetInfo(int playerId, float x, float z) : this(playerId, x, z, 0f, 0f)
        {
        }

        public TargetInfo(int playerId, float x, float z, float velX, float velZ)
        {
            PlayerId = playerId;
            X = x;
            Z = z;
            VelX = velX;
            VelZ = velZ;
        }
    }
}
