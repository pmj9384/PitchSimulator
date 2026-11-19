namespace Game.Core.Viewing
{
    // 보여 줄 구간 하나. 좌표는 "지금까지 돈 틱 수"(MatchManager.Ticks)라서 Start <= Ticks < End인 동안 다음 틱을 화면에 보여 준다
    public readonly struct TickRange
    {
        public readonly int Start;
        public readonly int End;

        public TickRange(int start, int end)
        {
            Start = start;
            End = end;
        }

        public int Length => End - Start;
    }
}
