namespace Game.Core.Data
{
    // StageComposition.csv 한 행(스펙 §10: stage,side,kind,id,count,posX,posZ,posX2,posZ2).
    // 상대 팀 포메이션도, 플레이어가 놓은 세팅의 저장도 전부 이 한 줄 형식이다. 재도전·상대 데이터·멀티가 같은 것을 읽는다.
    // 자리 2쌍(스펙 §4-3): posX·posZ = 공격 시(아군 소유), posX2·posZ2 = 수비 시(상대 소유). 09-21 추가.
    // 비가역 경보: 빌드·다이얼 열이 더 붙는다(§11). 열을 늘릴 땐 여기와 ClassMap부터.
    // struct가 아니라 class인 이유: CsvHelper가 리플렉션으로 채운다.
    public class StageEntry
    {
        public const string SidePlayer = "player";
        public const string SideEnemy = "enemy";
        public const string KindPlayer = "player";   // 1차 출시는 선수뿐. 구조물 종류 없음

        public int Stage { get; set; }
        public string Side { get; set; } = string.Empty;   // player / enemy. 팀 번호(0/1)가 아니라 말로 쓴다: 기획이 읽는 파일이다
        public string Kind { get; set; } = string.Empty;   // player
        public string Id { get; set; } = string.Empty;     // roleId(GK·CB·FB·DM·CM·AM·W·ST)
        public int Count { get; set; }                     // 같은 자리 기준으로 몇 명. 1보다 크면 폭(Z) 방향으로 최소 간격씩 벌려 세운다
        public float PosX { get; set; }
        public float PosZ { get; set; }
        public float? PosX2 { get; set; }   // 수비 시 자리. 열이 없거나 비면 null = 공격 시 자리와 같다(옛 파일·세팅 초기값 호환). 0과 "없음"을 구분하려고 nullable
        public float? PosZ2 { get; set; }

        public int Team => Side == SidePlayer ? 0 : 1;
        public float DefendX => PosX2 ?? PosX;
        public float DefendZ => PosZ2 ?? PosZ;
    }
}
