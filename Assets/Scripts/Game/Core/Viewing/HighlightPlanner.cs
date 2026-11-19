using System.Collections.Generic;
using Game.Core.Match;

namespace Game.Core.Viewing
{
    // 1차 계산(S4): 재생할 시뮬과 같은 조립으로 만든 시뮬을 끝까지 돌려 사건을 모으고 보여 줄 구간을 만든다.
    // 엔진이 없어 뒤 스레드에서 돌려도 된다(MatchManager가 GameReady에서 부른다). 넘겨받은 시뮬은 끝까지 돌아 다시 쓸 수 없다
    public static class HighlightPlanner
    {
        public static IReadOnlyList<TickRange> Plan(MatchSimulation firstPass, ViewingMode mode)
        {
            using (var log = new MatchEventLog(firstPass))
            {
                for (int i = 0; i < firstPass.TotalTicks; i++)
                {
                    firstPass.Tick(MatchTuning.FixedStep);
                }
                return HighlightRules.Build(log.Events, mode, firstPass.TotalTicks, HighlightRules.TicksPerSecond(firstPass));
            }
        }
    }
}
