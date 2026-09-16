namespace Game.Core.Data
{
    // StageComposition.csv 한 행(스펙 §10: stage,side,kind,id,count,posX,posZ).
    // 상대 팀 포메이션도, 플레이어가 놓은 세팅의 저장도 전부 이 한 줄 형식이다. 재도전·상대 데이터·멀티가 같은 것을 읽는다.
    // 비가역 경보: 2주차에 자리 2쌍(공격 시/수비 시)·빌드·다이얼 열이 붙는다(스펙 §4-3·§11). 열을 늘릴 땐 여기와 ClassMap부터.
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

        public int Team => Side == SidePlayer ? 0 : 1;
    }
}
