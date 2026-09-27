using System.Collections.Generic;
using Game.Core.Match;
using Game.Core.Tactics;

namespace Game.Core.League
{
    // 내 경기 한 판의 재료 전부: 일정·시드·라인업 2개·전술 2개. 매니저(StageManager 스폰·MatchManager 시드/전술/킥오프)가 이 값 객체만 읽어 서로를 모른다(09-27 확정 스펙 ②).
    // 인게임 관례: 팀 0 = 나(승리 → GameClear, 색·카메라). 일정상 홈·원정은 킥오프 팀과 결과 변환(ResultFor)에서만 드러난다
    public sealed class MatchSetup
    {
        public Fixture Fixture { get; }
        public int Seed { get; }
        public IReadOnlyList<LineupSlot> Team0 { get; }   // 나. -X 진영에서 시작
        public IReadOnlyList<LineupSlot> Team1 { get; }   // 상대. +X 진영
        public TeamTactics Tactics0 { get; }
        public TeamTactics Tactics1 { get; }
        public string OpponentName { get; }

        public MatchSetup(Fixture fixture, int seed, IReadOnlyList<LineupSlot> team0, IReadOnlyList<LineupSlot> team1,
            TeamTactics tactics0, TeamTactics tactics1, string opponentName)
        {
            Fixture = fixture;
            Seed = seed;
            Team0 = team0;
            Team1 = team1;
            Tactics0 = tactics0;
            Tactics1 = tactics1;
            OpponentName = opponentName;
        }

        public bool MyTeamIsHome => Fixture.HomeTeamId == SeasonState.MyTeamId;

        // 킥오프는 일정상 홈이 한다(인게임 관례 "홈이 킥오프"를 일정에 맞춘 것. 러너는 시드 홀짝 교대)
        public int KickoffTeam => MyTeamIsHome ? 0 : 1;

        // 시뮬 골(팀 0 = 나, 팀 1 = 상대) → 승점표 결과(일정의 홈·원정 기준). 내가 원정이면 뒤집힌다
        public MatchResult ResultFor(int team0Goals, int team1Goals)
        {
            return MyTeamIsHome
                ? new MatchResult(Fixture.HomeTeamId, Fixture.AwayTeamId, team0Goals, team1Goals)
                : new MatchResult(Fixture.HomeTeamId, Fixture.AwayTeamId, team1Goals, team0Goals);
        }
    }
}
