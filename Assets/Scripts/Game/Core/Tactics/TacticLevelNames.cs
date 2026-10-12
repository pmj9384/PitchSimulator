namespace Game.Core.Tactics
{
    // 팀 전술 3단(0·1·2)의 화면 이름 한 곳(10-12). 전엔 TacticsBoard.prefab 버튼 글자와 TacticsReadoutText 배열 두 곳에 있어
    // 한쪽만 고치면 설정창과 결과 화면이 다른 말을 썼다(10-06 리뷰 잔여). 값의 뜻은 TeamTactics 주석, 판정값은 MatchTuning
    public static class TacticLevelNames
    {
        public static readonly string[] PassStyle = { "짧게", "직접", "롱볼" };
        public static readonly string[] PassRisk = { "안전", "균형", "모험" };
        public static readonly string[] Tempo = { "느리게", "표준", "빠르게" };
        public static readonly string[] Width = { "좁게", "표준", "넓게" };
        public static readonly string[] PressStart = { "안 감", "표준", "적극" };
        public static readonly string[] Counter = { "안 함", "상황 봐서", "적극" };
        public static readonly string[] CounterPress = { "안 함", "상황 봐서", "적극" };
        public static readonly string[] GkDistribution = { "짧게", "섞어", "길게" };

        // 서드 3열. 스펙 §3 "우리 진영 ≤ −17.5 < 중원 < 17.5 ≤ 상대 진영"과 같은 말(10-12 표기 통일: 결과 화면이 "중앙"이라 적던 것)
        public static readonly string[] Thirds = { "우리 진영", "중원", "상대 진영" };
    }
}
