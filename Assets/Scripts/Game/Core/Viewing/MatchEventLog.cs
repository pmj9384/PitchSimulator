using System;
using System.Collections.Generic;
using Game.Core.Match;

namespace Game.Core.Viewing
{
    // 시뮬 이벤트를 틱 번호와 함께 순서대로 모은다(S4). 판단은 하지 않는다: 무엇을 보여 줄지는 HighlightRules가 정한다.
    // 1차(헤드리스)와 2차(재생)에 같은 로그를 붙이면 두 경기가 같은지 사건 단위로 대조할 수 있다
    public sealed class MatchEventLog : IDisposable
    {
        private const int NoPendingKick = -1;

        private readonly MatchSimulation simulation;
        private readonly List<MatchEvent> events = new List<MatchEvent>();
        private int pendingShotKickTick = NoPendingKick;   // BallKicked(슛)가 ShotResolved보다 먼저 온다. 찬 틱을 결과와 묶으려고 들고 있는다

        public IReadOnlyList<MatchEvent> Events => events;

        public MatchEventLog(MatchSimulation simulation)
        {
            this.simulation = simulation;
            simulation.BallKicked += OnBallKicked;
            simulation.ShotResolved += OnShotResolved;
            simulation.PossessionChanged += OnPossessionChanged;
        }

        public void Dispose()
        {
            simulation.BallKicked -= OnBallKicked;
            simulation.ShotResolved -= OnShotResolved;
            simulation.PossessionChanged -= OnPossessionChanged;
        }

        private void OnBallKicked(KickReport report)
        {
            if (!report.IsShot) { return; }
            pendingShotKickTick = simulation.TickCount;
        }

        private void OnShotResolved(ShotReport report)
        {
            int resolveTick = simulation.TickCount;
            int kickTick = pendingShotKickTick == NoPendingKick ? resolveTick : pendingShotKickTick;
            pendingShotKickTick = NoPendingKick;
            int team = simulation.Players[report.ShooterId].Team;
            events.Add(new MatchEvent(MatchEventKind.Shot, kickTick, resolveTick, team, report.ShooterId, report.Probability, report.Outcome));
        }

        private void OnPossessionChanged(PossessionReport report)
        {
            if (report.Kind == PossessionChange.PassReceived) { return; }   // 같은 팀 안의 이동은 전개의 시작이 아니다
            int tick = simulation.TickCount;
            events.Add(new MatchEvent(MatchEventKind.PossessionWon, tick, tick, report.NewOwnerTeam, report.NewOwnerId, 0f, ShotOutcome.Goal));
        }
    }
}
