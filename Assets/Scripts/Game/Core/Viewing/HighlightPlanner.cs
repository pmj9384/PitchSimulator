using Game.Core.Match;

namespace Game.Core.Viewing
{
    // 1차 계산(S4): 재생할 시뮬과 같은 조립으로 만든 시뮬을 끝까지 돌려 사건을 모으고, 주요 장면·하이라이트 두 모드의 구간을 한 번에 만든다.
    // 모드와 상관없이 늘 부른다: 경기 중 모드를 바꿔도 시뮬을 다시 돌리지 않는다(11-19).
    // 엔진이 없어 뒤 스레드에서 돌려도 된다(MatchManager가 GameReady에서 부른다). 넘겨받은 시뮬은 끝까지 돌아 다시 쓸 수 없다
    public static class HighlightPlanner
    {
        public static HighlightSet Plan(MatchSimulation firstPass)
        {
            using (var log = new MatchEventLog(firstPass))
            {
                for (int i = 0; i < firstPass.TotalTicks; i++)
                {
                    firstPass.Tick(MatchTuning.FixedStep);
                }
                float ticksPerSecond = HighlightRules.ScreenTicksPerSecond;
                return new HighlightSet(
                    HighlightRules.Build(log.Events, ViewingMode.Key, firstPass.TotalTicks, ticksPerSecond),
                    HighlightRules.Build(log.Events, ViewingMode.Extended, firstPass.TotalTicks, ticksPerSecond));
            }
        }
    }
}
