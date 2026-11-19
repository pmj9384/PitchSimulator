using System;
using System.Collections.Generic;

namespace Game.Core.Viewing
{
    // 구간 목록 위의 커서(S4). 2차 재생에서 "이번 스텝에 보여 주나, 건너뛰나, 몇 틱 건너뛰나"를 답한다.
    // 결과에는 영향이 없다: 건너뛰어도 시뮬 Tick을 같은 횟수·같은 순서로 부르고 화면 반영만 생략한다. 틱은 늘기만 한다고 가정한다(커서가 뒤로 안 간다)
    public sealed class ReplayPlan
    {
        public const int NoMoreShown = -1;

        private readonly IReadOnlyList<TickRange> ranges;
        private int index;   // 아직 끝나지 않은 첫 구간

        public ReplayPlan(IReadOnlyList<TickRange> ranges)
        {
            this.ranges = ranges;
        }

        public IReadOnlyList<TickRange> Ranges => ranges;

        // 지금까지 돈 틱 수가 ticks일 때 다음 틱을 화면에 보여 주나
        public bool IsShown(int ticks)
        {
            Advance(ticks);
            if (index >= ranges.Count) { return false; }
            return ranges[index].Start <= ticks;
        }

        // 다음에 보여 줄 첫 틱(지금 보이는 중이면 ticks 자신). 남은 구간이 없으면 NoMoreShown
        public int NextShownTick(int ticks)
        {
            Advance(ticks);
            if (index >= ranges.Count) { return NoMoreShown; }
            return Math.Max(ranges[index].Start, ticks);
        }

        // 이번 스텝에 화면 없이 돌릴 틱 수. 보이는 중이면 0, 아니면 다음 구간(없으면 경기 끝)까지를 budget으로 자른다.
        // budget은 프레임당 계산 예산이라 체감만 바꾸고 결과는 안 바꾼다(테스트가 예산 1·1,000·100,000을 대조)
        public int TicksToSkip(int ticks, int totalTicks, int budget)
        {
            if (budget < 1) { throw new ArgumentOutOfRangeException(nameof(budget), "[ReplayPlan] 예산은 1틱 이상"); }
            if (ticks >= totalTicks) { return 0; }
            if (IsShown(ticks)) { return 0; }
            int target = NextShownTick(ticks);
            if (target == NoMoreShown || target > totalTicks)
            {
                target = totalTicks;
            }
            return Math.Min(target - ticks, budget);
        }

        private void Advance(int ticks)
        {
            while (index < ranges.Count && ranges[index].End <= ticks)
            {
                index++;
            }
        }
    }
}
