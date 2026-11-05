namespace Game.Core.League
{
    // 관중 채움(11-06 경기장 4종 1단계, 설계 §3). 좌석 슬롯마다 해시로 채울지 정한다: 같은 시즌·같은 경기장·같은 홈 팀이면 빈자리가 같다.
    // KitRules처럼 생성기(TeamGenerator)의 난수 흐름에 안 끼우고 ScoutRules.Hash를 소금만 달리해 쓴다. 화면 전용이라 경기 결과와 무관하다
    public static class StadiumRules
    {
        private const uint FillSalt = 53u;   // KitRules(37)·ScoutRules·HomeNews와 입력 수가 같아 소금으로 떼어 놓는다
        private const uint SeatSalt = 59u;

        // 이번 경기 관중의 시드. 경기장 부와 홈 팀이 같으면 같은 값
        public static uint FillSeed(int seasonSeed, int stadiumTier, int homeTeamId)
        {
            uint h = ScoutRules.Hash((uint)seasonSeed, (uint)stadiumTier, FillSalt);
            return ScoutRules.Hash(h, (uint)homeTeamId, FillSalt);
        }

        // 좌석 slotIndex에 관중이 앉는가. fillPercent 0이면 아무도, 100이면 전부
        public static bool IsFilled(uint fillSeed, int slotIndex, int fillPercent)
        {
            return ScoutRules.Hash(fillSeed, (uint)slotIndex, SeatSalt) % 100u < (uint)fillPercent;
        }
    }
}
