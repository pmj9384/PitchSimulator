namespace Game.Core.Match
{
    // "가장 가까운 선수" 후보 1명의 최소 정보. 선정에 필요한 것만 담는다(팀·스탯은 선정과 무관하므로 없음).
    public struct TargetInfo
    {
        public int SpawnIndex;   // 자체 부여 스폰 순번. 동률 타이브레이크 기준(GetInstanceID는 실행마다 달라 금지)
        public float X;
        public float Z;          // 필드는 평면 105×68m. 높이(y)는 판정에 안 쓴다

        public TargetInfo(int spawnIndex, float x, float z)
        {
            SpawnIndex = spawnIndex;
            X = x;
            Z = z;
        }
    }
}
