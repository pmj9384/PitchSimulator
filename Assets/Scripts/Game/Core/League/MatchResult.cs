namespace Game.Core.League
{
    // 리그가 읽는 경기 결과 한 줄(스펙 §10). 러너의 MatchSummary엔 팀 id가 없으니 시즌 쪽이 붙여 만든다.
    // 인게임 결과(MatchManager)와 자동 대전 결과가 같은 형식으로 승점표에 들어간다
    public readonly struct MatchResult
    {
        public readonly int HomeTeamId;
        public readonly int AwayTeamId;
        public readonly int HomeGoals;
        public readonly int AwayGoals;

        public MatchResult(int homeTeamId, int awayTeamId, int homeGoals, int awayGoals)
        {
            HomeTeamId = homeTeamId;
            AwayTeamId = awayTeamId;
            HomeGoals = homeGoals;
            AwayGoals = awayGoals;
        }
    }
}
