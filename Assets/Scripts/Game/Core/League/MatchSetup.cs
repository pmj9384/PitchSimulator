using System.Collections.Generic;
using Game.Core.Match;
using Game.Core.Tactics;

namespace Game.Core.League
{
    // 내 경기 한 판의 재료 전부: 일정·시드·라인업 2개·전술 2개. 시뮬로 바꾸는 곳은 MyMatchAssembler 하나다(인게임·치트 헤드리스·테스트 공용, 11-06 S4-1).
    // 매니저는 MatchManager가 조립하고, StageManager는 경기장·선수 뷰 재료(유니폼·머리 번호)만 이 값 객체에서 읽는다.
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
        public int Kit0 => KitRules.MyKit;   // 내 유니폼 번호
        public int Kit1 { get; }             // 상대 유니폼 번호(KitRules.Of). 색은 화면 PlayerPalette
        public int StadiumTier { get; }      // 경기장 = 홈 팀이 속한 부(11-06). 리그는 내 부, 승강전 1·2차전 원정만 상대 부. 화면 전용

        public MatchSetup(Fixture fixture, int seed, IReadOnlyList<LineupSlot> team0, IReadOnlyList<LineupSlot> team1,
            TeamTactics tactics0, TeamTactics tactics1, string opponentName, int kit1, int stadiumTier)   // kit1·stadiumTier에 기본값을 두지 않는다: 빠뜨리면 같은 색·내 부 경기장이 조용히 나온다
        {
            Fixture = fixture;
            Kit1 = kit1;
            StadiumTier = stadiumTier;
            Seed = seed;
            Team0 = team0;
            Team1 = team1;
            Tactics0 = tactics0;
            Tactics1 = tactics1;
            OpponentName = opponentName;
        }

        public bool MyTeamIsHome => Fixture.HomeTeamId == SeasonState.MyTeamId;

        // 킥오프는 일정상 홈이 한다(인게임 관례 "홈이 킥오프"를 일정에 맞춘 것. 읽는 곳은 MyMatchAssembler. 러너·상대끼리 경기는 시드 홀짝 교대)
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
