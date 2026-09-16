namespace Game.Core.Data
{
    // 역할 프리셋 1종의 값 스키마. PlayerTable.csv 한 행과 1:1(스펙 §4: 빌드 6 + 지시 다이얼 6 + 표시 3).
    // 스키마의 주인은 이 클래스다: CSV 열을 늘리려면 여기부터 늘린다.
    // 빌드는 총점 고정(TotalPoints)이 이 게임의 축이라 파서가 로드 시점에 합계를 검증한다.
    // struct가 아니라 class인 이유: CsvHelper가 리플렉션으로 프로퍼티를 채운다.
    public class PlayerStats
    {
        public const int TotalPoints = 300;   // 빌드 6개 합계. 밸런스 문서에서 바꾼다(스펙 §4-1 초안 300)

        public string RoleId { get; set; } = string.Empty;   // GK·CB·FB·DM·CM·AM·W·ST

        // ── 빌드(능력). MatchRules 판정 함수의 입력값
        public int Speed { get; set; }
        public int Stamina { get; set; }
        public int Pass { get; set; }
        public int Shot { get; set; }
        public int Tackle { get; set; }
        public int Positioning { get; set; }

        // ── 지시(행동) 다이얼. 트리의 판정값
        public float PushUp { get; set; }        // 아군 소유 때 자리에서 앞으로 얼마나(m)
        public float PressRange { get; set; }    // 상대 소유 때 공이 몇 m 안이면 달려드나
        public float ShotBias { get; set; }      // 슛 확률이 얼마 이상이면 쏘나(0~1)
        public float PassLength { get; set; }    // 우선 패스 길이(m). 짧은 패스 우선 / 롱볼 허용
        public float Width { get; set; }         // 자리 기준 좌우로 얼마나 벌리나(m)
        public float LineHeight { get; set; }    // 수비 때 후퇴 한계(m)

        // ── 표시 텍스트(선수 세팅 화면, 3주차에 채움)
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;

        public int BuildTotal => Speed + Stamina + Pass + Shot + Tackle + Positioning;
    }
}
