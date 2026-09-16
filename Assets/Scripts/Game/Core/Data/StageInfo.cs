namespace Game.Core.Data
{
    // StageTable.csv 한 행(스펙 §10: stage,displayName,opponentName). "무엇을 어디에"는 StageEntry(StageComposition) 몫.
    public class StageInfo
    {
        public int Stage { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string OpponentName { get; set; } = string.Empty;   // 상대 팀 이름. 스코어보드에 표시
    }
}
