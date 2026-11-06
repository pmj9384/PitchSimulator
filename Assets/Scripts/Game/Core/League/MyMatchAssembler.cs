using System;
using System.Collections.Generic;
using Game.Core.AutoMatch;
using Game.Core.Match;

namespace Game.Core.League
{
    // 내 경기 한 판(MatchSetup) → 시뮬. 인게임(MatchManager)·치트 헤드리스(SeasonSystem.AutoPlayCurrentMatchAsync)·테스트가 이 함수 하나로 조립한다(11-06 S4-1).
    // 전에는 인게임이 스폰하며 선수를 등록하고 킥오프를 일정상 홈에게, 헤드리스는 MatchAssembler가 시드 홀짝으로 줘서 같은 MatchSetup이 다른 경기가 될 수 있었다.
    // League 쪽에 두는 이유: MatchSetup(일정·홈 원정)은 League 개념이고 MatchAssembler(AutoMatch)는 그걸 모른다. 의존은 League → AutoMatch 한 방향
    public static class MyMatchAssembler
    {
        // 팀 0 = 나, 팀 1 = 상대. 킥오프 = 일정상 홈(setup.KickoffTeam). 길이를 안 주면 MatchLength.Default
        public static MatchSimulation Create(MatchSetup setup, ThreatGrid threat, MatchLength? length = null)
        {
            return MatchAssembler.Create(setup.Team0, setup.Team1, setup.Tactics0, setup.Tactics1, setup.Seed, setup.KickoffTeam, threat, null, length);
        }

        // 시뮬 선수 → 그 선수를 만든 라인업 칸(겉모습 번호 등 화면 재료). 번호 규칙(팀 0 먼저, 라인업 순)은 MatchAssembler.Create와 같다
        public static LineupSlot SlotOf(MatchSetup setup, PlayerState player)
        {
            int index = player.Team == 0 ? player.PlayerId : player.PlayerId - setup.Team0.Count;
            IReadOnlyList<LineupSlot> lineup = player.Team == 0 ? setup.Team0 : setup.Team1;
            if (index < 0 || index >= lineup.Count) { throw new InvalidOperationException($"[MyMatchAssembler] 선수 {player.PlayerId}(팀 {player.Team})가 라인업 범위 밖이다"); }
            return lineup[index];
        }
    }
}
