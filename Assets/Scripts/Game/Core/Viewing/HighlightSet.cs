using System.Collections.Generic;

namespace Game.Core.Viewing
{
    // 1차 계산 한 번으로 만든 두 모드의 구간 목록(11-19). 경기 중 모드를 바꾸면 여기서 그 모드의 목록을 꺼낸다.
    // 전체 경기는 구간이 없다(null = 전부 보여 준다). 시뮬을 다시 돌리지 않으므로 바꿔도 결과는 같다
    public sealed class HighlightSet
    {
        public IReadOnlyList<TickRange> Key { get; }
        public IReadOnlyList<TickRange> Extended { get; }

        public HighlightSet(IReadOnlyList<TickRange> key, IReadOnlyList<TickRange> extended)
        {
            Key = key;
            Extended = extended;
        }

        public IReadOnlyList<TickRange>? RangesFor(ViewingMode mode)
        {
            switch (mode)
            {
                case ViewingMode.Key:
                    return Key;
                case ViewingMode.Extended:
                    return Extended;
                default:
                    return null;
            }
        }
    }
}
