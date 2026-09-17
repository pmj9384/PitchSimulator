namespace Game.Core.Tactics
{
    // 공이 있는 세로 구역. 팀 전술 값은 항목마다 이 3구간 배열이다(전술-기획.md, 09-17 확정).
    // 팀 0 기준 "우리 진영"은 x<−17.5, "상대 진영"은 x>+17.5. 팀 1은 부호를 뒤집어 같은 인덱스를 쓴다.
    public enum Third { Own = 0, Middle = 1, Opponent = 2 }

    // 팀 전술 1세트(스타일 카드 1장 = 이 값 전부). 11명 공통 기본값이고 개인 다이얼이 덮어쓴다.
    // 값은 전부 0·1·2의 3단(짧게·직접·롱볼 / 안전·균형·모험 / 느리게·표준·빠르게 / 좁게·표준·넓게 / 안 감·표준·적극).
    // 서드별 배열 5개 + 서드 무관 4개. 판정 함수는 공이 있는 서드로 값을 꺼낼 뿐이라 항목당 1개일 때와 구조가 같다.
    // 라인 높이·간격·수비 폭은 항목이 아니다. 수비 시 자리(바둑알)에서 읽는다.
    // struct가 아니라 class인 이유: CsvHelper가 리플렉션으로 채운다.
    public class TeamTactics
    {
        public const int Levels = 3;   // 각 값의 정의역 0~2

        public string PresetId { get; set; } = string.Empty;   // buildup · balanced · counter · pressing (스타일 카드)
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        // ── 공격 조직: 우리 공이 있는 서드별 [Own, Middle, Opponent]
        public int[] PassStyle { get; set; } = new int[3];   // 패스 방식: 짧게·직접·롱볼 → 리시버 거리 가중
        public int[] PassRisk { get; set; } = new int[3];    // 패스 리스크: 안전·균형·모험 → 가로채기 허용 임계
        public int[] Tempo { get; set; } = new int[3];       // 속도: 느리게·표준·빠르게 → 소유 뒤 킥까지 대기
        public int[] Width { get; set; } = new int[3];       // 폭: 좁게·표준·넓게 → 자리 Z 오프셋 배율

        // ── 수비 조직: 상대 공이 있는 서드별 [Own(우리 골 앞), Middle, Opponent(상대 빌드업)]
        public int[] PressStart { get; set; } = new int[3];  // 압박 시작: 안 감·표준·적극. Opponent 열이 곧 하이 프레스 여부

        // ── 전환(서드 무관)
        public int Counter { get; set; }        // 역습 성향: 안 함·상황 봐서·적극 → 공 앞쪽 상대 수비 수 문턱(없음·3·4)
        public int CounterPress { get; set; }   // 역압박 성향: 안 함·상황 봐서·적극 → 뺏긴 순간 우리 뒤 수비 수 문턱
        public int GkDistribution { get; set; } // GK 배급: 짧게·섞어·길게
        public int Mentality { get; set; }      // 전진 정도: 수비적·균형·공격적 → 공격 시 자리 X 오프셋 배율(슬라이더 ①)

        // 역습 판정 문턱: 공보다 상대 골 쪽에 있는 상대 필드 플레이어가 이 수 이하면 역습. -1 = 안 함
        public int CounterThreshold => Counter == 0 ? -1 : (Counter == 1 ? 3 : 4);
    }
}
