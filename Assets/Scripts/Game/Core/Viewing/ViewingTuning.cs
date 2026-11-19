namespace Game.Core.Viewing
{
    // 하이라이트 상수(11-06 S4 설계 4절). 초는 전부 화면 시간(×1 배속에서 실제로 흐르는 초)이다. 틱으로는 HighlightRules.ScreenTicksPerSecond로 바꾼다.
    // 11-19 유저 Play "장면 시작 1초 만에 골": 경기 시계 초로 재면 3분 압축(경기 1초 ≈ 1.67틱)에서 45초가 75틱 = 화면 1.5초였다. 실제 90분은 두 기준이 같다(1초 = 50틱).
    // 값은 전부 [가정]이고 S3 재튜닝 뒤 대시보드 지표(하이라이트 주요 장면/판·하이라이트/판·분량)로 다시 정한다
    public static class ViewingTuning
    {
        public const float BigChanceP = 0.30f;            // 큰 찬스 문턱. Opta Big Chance 비페널티 전환율 약 38%, 분석가 제안 xG 0.30~0.35 [가정]
        public const float DangerousBlockP = 0.15f;       // 하이라이트 모드에 넣는 블록 슛의 문턱 [가정]
        public const int KeyMinimumShots = 3;             // 주요 장면 하한. 모자라면 p 높은 순으로 채운다(FM Key "하이라이트 0개" 불만 회피)

        public const float BuildUpLeadSec = 2f;           // 공격 전개 시작(소유 획득) 앞으로 더 보여 주는 여유
        public const float MinLeadSec = 8f;               // 찬 순간보다 최소 이만큼 앞에서 시작(슛만 툭 나오는 장면 방지)
        public const float MaxLeadSec = 45f;              // 찬 순간보다 이보다 앞에서는 시작하지 않는다(FM Extended "빌드업이 지루하다" 방지)

        public const float AfterGoalSec = 3f;             // 골 결과 뒤(시뮬은 같은 틱에 킥오프 자리로 리셋한다)
        public const float AfterSaveSec = 4f;             // 캐치·튕김 뒤(튕김이면 자유 공 다툼까지)
        public const float AfterMissSec = 3f;             // 빗나감 뒤(골킥 멈춤 포함)
        public const float AfterBlockSec = 3f;            // 블록 뒤

        public const float MergeGapSec = 20f;             // 다음 구간 시작이 이 안이면 하나로 합친다
        public const float EmptyMatchSec = 30f;           // 고를 사건이 하나도 없으면 킥오프 뒤·종료 전 이만큼만 보여 준다
    }
}
