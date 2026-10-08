namespace Game.Core.Data
{
    // 세팅 화면(3주차)에서 스탯을 묶어 보여주는 이름표. 판정 함수는 이 묶음을 모르고 스탯 하나하나를 쓴다.
    public enum StatGroup { Physical, Attack, Defense, Goalkeeper }

    // 역할 프리셋 1종(자리 × 변형)의 값 스키마. PlayerTable.csv 한 행과 1:1(스펙 §4: 빌드 9 + 지시 다이얼 11 + 표시).
    // 스키마의 주인은 이 클래스다: CSV 열을 늘리려면 여기부터 늘린다.
    // 09-17 확장: 자리(RoleId)마다 변형(VariantId)이 여러 개다(FM26·FC26·현실 용어 52종, 전술-기획.md 3-1).
    // 변형은 다이얼만 다르고 빌드는 자리별로 같다. 총점 고정(TotalPoints)이 이 게임의 축이라 파서가 합계를 검증한다.
    // GK 전용 3개(reflexes·handling·diving)도 같은 행에 있다(FM 방식). 필드 플레이어는 0을 둔다.
    // struct가 아니라 class인 이유: CsvHelper가 리플렉션으로 프로퍼티를 채운다.
    public class PlayerStats
    {
        public const int TotalPoints = 300;   // 빌드 9개 합계. 부별 총점(스펙 §4-1)은 리그 로드 때 비율로 축소한다

        public string RoleId { get; set; } = string.Empty;      // 자리: GK·CB·FB·DM·CM·AM·W·ST
        public string VariantId { get; set; } = string.Empty;   // 변형: poacher·targetman·false9 …. 프리셋 키. 편성 CSV의 id가 이것을 가리킨다
        public bool Exposed { get; set; }                       // 1차 출시에 세팅 화면에 보이나. 나머지는 CSV에 있되 잠금
        public bool IsKeeper => string.Equals(RoleId, "GK", System.StringComparison.OrdinalIgnoreCase);   // 자리 판정 한 곳(10-08). PlayerState.IsGoalkeeper도 이 값을 쓴다
        public int Focus { get; set; }                          // 기본 포커스: 0 수비 · 1 균형 · 2 공격(전진 오프셋 하나)

        // ── 빌드(능력) 공통 6. MatchRules 판정 함수의 입력값
        public int Speed { get; set; }          // 이동 속도(MatchRules.SpeedMps). 50 = 7m/s
        public int Stamina { get; set; }        // 체력 소모량의 입력(09-29 FatigueRules.Exert). 높을수록 전력 행동에 덜 지친다
        public int Pass { get; set; }
        public int Shot { get; set; }           // 슛 확률을 올리는 보정
        public int Tackle { get; set; }
        public int Positioning { get; set; }

        // ── 빌드(능력) GK 전용 3
        public int Reflexes { get; set; }       // 슛 확률을 내리는 보정
        public int Handling { get; set; }       // 세이브 뒤 캐치 확률을 올리는 보정
        public int Diving { get; set; }         // 슛 확률을 내리는 보정(닿는 범위)

        // ── 지시(행동) 다이얼 11. 트리의 판정값. 팀 전술 값을 덮어쓴다(FM26 개인 지시 기준, 전술-기획.md 3-2)
        public float PushUp { get; set; }        // 전진 폭: 아군 소유 때 자리에서 앞으로 얼마나(m)
        public float PressRange { get; set; }    // 압박 거리: 상대 소유 때 공이 몇 m 안이면 달려드나. 역할마다 달라 "누가 압박하나"의 답
        public float ShotBias { get; set; }      // 슛 성향(0~1): xG ≥ 성향 × MatchTuning.ShotBiasXgScale(0.5)이면 쏜다. 포처 0.2 → xG 0.10
        public float PassLength { get; set; }    // 패스 길이: 우선 패스 거리(m)
        public float Width { get; set; }         // 측면 쏠림: 자리 기준 좌우로 얼마나(m)
        public float LineHeight { get; set; }    // 라인 높이: 수비 때 후퇴 한계(m)
        public float RoamRadius { get; set; }    // 자리 이탈 반경(m). 폴스 9·프리 롤·라움도이터
        public float PassRisk { get; set; }      // 패스 리스크 0~1. 가로채기 허용 임계 개인값
        public float Dribble { get; set; }       // 드리블 성향 0~1. 위협 없을 때 드리블 유지 확률
        public float HoldUp { get; set; }        // 볼 끌기: 소유 뒤 킥까지 대기(초). 타깃맨
        public float GkRushRadius { get; set; }  // GK 출격 반경(m, 박스 상한). 스위퍼 키퍼. 필드 플레이어는 0

        // ── 표시 텍스트(선수 세팅 화면, 3주차에 채움)
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;

        public int BuildTotal => Speed + Stamina + Pass + Shot + Tackle + Positioning + Reflexes + Handling + Diving;

        // 전 필드 복사. 필드가 전부 값·문자열이라 얕은 복사로 충분하다. PlayerTable 행은 공유 객체라 값을 고치기 전에 복사한다
        public PlayerStats Clone()
        {
            return (PlayerStats)MemberwiseClone();
        }
    }
}
