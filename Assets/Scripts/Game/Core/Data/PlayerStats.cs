namespace Game.Core.Data
{
    // 세팅 화면(3주차)에서 스탯을 묶어 보여주는 이름표. 판정 함수는 이 묶음을 모르고 스탯 하나하나를 쓴다.
    public enum StatGroup { Physical, Attack, Defense, Goalkeeper }

    // 역할 프리셋 1종의 값 스키마. PlayerTable.csv 한 행과 1:1(스펙 §4: 빌드 9 + 지시 다이얼 6 + 표시 3).
    // 스키마의 주인은 이 클래스다: CSV 열을 늘리려면 여기부터 늘린다.
    // 빌드는 총점 고정(TotalPoints)이 이 게임의 축이라 파서가 로드 시점에 합계를 검증한다.
    // GK 전용 3개(reflexes·handling·diving)도 같은 행에 있다(09-16, FM 방식). 필드 플레이어는 0을 둔다.
    // 역할군마다 스키마를 나누면 파서·세이브·세팅 화면이 2벌이 되므로 스키마는 하나로 유지한다.
    // struct가 아니라 class인 이유: CsvHelper가 리플렉션으로 프로퍼티를 채운다.
    public class PlayerStats
    {
        public const int TotalPoints = 300;   // 빌드 9개 합계. 밸런스 문서에서 바꾼다(스펙 §4-1 초안 300)

        public string RoleId { get; set; } = string.Empty;   // GK·CB·FB·DM·CM·AM·W·ST

        // ── 빌드(능력) 공통 6. MatchRules 판정 함수의 입력값
        public int Speed { get; set; }          // 이동 속도(MatchRules.SpeedMps). 50 = 7m/s
        public int Stamina { get; set; }        // 아직 판정 없음(2주차 이후)
        public int Pass { get; set; }
        public int Shot { get; set; }           // 슛 확률을 올리는 보정
        public int Tackle { get; set; }
        public int Positioning { get; set; }

        // ── 빌드(능력) GK 전용 3. FC 온라인 GK 능력치 중 판정이 있는 것만(GK 킥은 Pass가, GK 포지셔닝은 2주차 자리 로직이 맡는다)
        public int Reflexes { get; set; }       // 슛 확률을 내리는 보정
        public int Handling { get; set; }       // 세이브 뒤 캐치 확률을 올리는 보정
        public int Diving { get; set; }         // 슛 확률을 내리는 보정(닿는 범위)

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

        public int BuildTotal => Speed + Stamina + Pass + Shot + Tackle + Positioning + Reflexes + Handling + Diving;
    }
}
