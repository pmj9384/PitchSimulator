namespace Game.Core.League
{
    // 팀 유니폼 번호(10-21, 유저 결정 "유니폼은 팀마다 다르게"). 색 자체는 화면(KitPalette)이 들고 여기는 번호만 정한다.
    // 내 팀은 0 고정. 상대는 (시즌 시드, 부)로 시작점을 잡고 팀 번호로 한 칸씩 돌려 같은 부 안에서 겹치지 않는다(상대 최대 7팀 = 색 7개).
    // 생성기(TeamGenerator)의 난수 흐름에 안 끼운다: 끼우면 같은 시드의 상대 이름·구성이 바뀌어 옛 세이브가 깨진다(Version 올려야 함)
    public static class KitRules
    {
        public const int MyKit = 0;
        public const int KitCount = 8;           // KitPalette 색 수. 0 = 내 팀, 1~7 = 상대
        private const uint KitSalt = 37u;        // ScoutRules.Hash·HomeNews와 입력 수가 같아 소금으로 떼어 놓는다

        public static int Of(int seasonSeed, int tier, int teamId)
        {
            int opponentKits = KitCount - 1;
            int start = (int)(ScoutRules.Hash((uint)seasonSeed, (uint)tier, KitSalt) % (uint)opponentKits);
            int offset = teamId % opponentKits;
            if (offset < 0) { offset += opponentKits; }
            return 1 + (start + offset) % opponentKits;
        }

        public static bool IsValid(int kit)
        {
            return kit >= 0 && kit < KitCount;
        }
    }
}
