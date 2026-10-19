namespace Game.Core.League
{
    // 스쿼드 규칙(스펙 §4-5 「10-19 스쿼드」): 선발 11 + 벤치. 숫자만 둔다. 벤치가 누구인지는 SeasonState(라인업에 없는 로스터 선수)
    public static class SquadRules
    {
        public const int MaxBench = 7;             // [가정] FM26 벤치 인원 미확인(SI 매뉴얼 403). 현실은 대회별(PL 9). 폰 한 열에 칩 7개
        public const int DefaultStars = 1;         // 기본 로스터(프리셋)의 ★. 영입 선수는 판정 때의 ★
        public const int ReleaseCoinsPerStar = 30; // [가정] 방출 = ★ × 30. 가치는 ★뿐이다: 총점이 같아 능력치로 못 매긴다(FM의 6요인을 의도적으로 단순화)

        public static int ReleaseCoins(int stars)
        {
            return ReleaseCoinsPerStar * stars;
        }

        public static bool BenchHasRoom(int benchCount)
        {
            return benchCount < MaxBench;
        }
    }
}
