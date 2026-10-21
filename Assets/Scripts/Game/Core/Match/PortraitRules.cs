namespace Game.Core.Match
{
    // 선수 겉모습 번호(1~VariantCount) = (머리 모양, 피부색, 머리색) 조합. 파워프로식: 생성 선수의 얼굴은 파츠 조합 데이터이고
    // 경기 모델(Quaternius Ultimate Modular Males, CC0)도 초상(그 머리를 찍은 portrait_NNN)도 같은 번호에서 풀린다(10-21 유저 결정).
    // 번호는 선수 개체(RosterPlayer)가 들고 다닌다: 후보 때 (팀, 번호)로 정한 얼굴을 영입 뒤에도 같게 보이려고
    public static class PortraitRules
    {
        public const int None = 0;          // 옛 세이브(10-21 전)의 값. 로드 때 Of(playerId)로 바꾼다
        public const int HeadCount = 6;     // 머리 메시 수(젊은 얼굴만 선별, PlayerView.headRenderers 순서). 에셋을 바꾸면 같이 고친다
        public const int SkinCount = 4;     // PlayerPalette.Skins
        public const int HairCount = 5;     // PlayerPalette.Hairs
        public const int VariantCount = HeadCount * SkinCount * HairCount;   // Resources/Sprites/Portrait 장수(PortraitTests가 대조)

        private const int TeamStride = 13;  // 팀마다 시작 조합을 어긋나게 한다(VariantCount와 서로소). 같은 팀 안의 변화는 playerIndex가 이어지는 데서 온다

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

        // 번호 → 파츠. 머리가 가장 빨리 돌고(이웃 번호는 머리부터 다르다) 머리색이 가장 느리다
        public static int HeadOf(int portrait)
        {
            return (portrait - 1) % HeadCount;
        }

        public static int SkinOf(int portrait)
        {
            return (portrait - 1) / HeadCount % SkinCount;
        }

        public static int HairOf(int portrait)
        {
            return (portrait - 1) / (HeadCount * SkinCount);
        }

        public static int Compose(int head, int skin, int hair)
        {
            return 1 + head + skin * HeadCount + hair * HeadCount * SkinCount;
        }
    }
}
