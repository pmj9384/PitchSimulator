namespace Game.Core.League
{
    // 선수 초상 번호(1~VariantCount). 그림은 Kenney Sports Pack(CC0) 머리 10종이고 번호가 그 파일(portrait_NN)을 가리킨다.
    // 번호는 선수 개체(RosterPlayer)가 들고 다닌다: 후보 때 (팀, 번호)로 정한 얼굴을 영입 뒤에도 같게 보이려고(10-22)
    public static class PortraitRules
    {
        public const int None = 0;          // 옛 세이브(10-22 전)의 값. 로드 때 Of(playerId)로 바꾼다
        public const int VariantCount = 10; // Resources/Sprites/Portrait의 장수. 에셋을 늘리면 같이 올린다(PortraitTests가 대조)

        private const int TeamStride = 13;  // 팀마다 시작 얼굴을 어긋나게 한다(10과 서로소). 같은 팀 안의 변화는 playerIndex가 이어지는 데서 온다

        // 기본 로스터·옛 세이브: playerId 하나로
        public static int Of(int seed)
        {
            int remainder = seed % VariantCount;
            if (remainder < 0) { remainder += VariantCount; }
            return 1 + remainder;
        }

        // 스카우트 후보: 시즌 안에서 (팀, 선수 번호)가 그 선수의 유일한 식별자다(ScoutCandidate)
        public static int Of(int teamId, int playerIndex)
        {
            return Of(teamId * TeamStride + playerIndex);
        }

        public static bool IsValid(int portrait)
        {
            return portrait >= 1 && portrait <= VariantCount;
        }
    }
}
