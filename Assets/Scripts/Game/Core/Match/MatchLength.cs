namespace Game.Core.Match
{
    // 경기 길이(11-05 S1, 스펙 §0 2번). 규정 90분을 몇 틱으로 돌리는지와 추가시간을 실제로 도는지를 한 값으로 묶어 시뮬에 넣는다.
    // 3분 압축(9,000틱, 1틱 = 경기 0.6초)은 S3 재튜닝이 끝날 때까지의 게임 기본값이고, 실제 90분(270,000틱, 1틱 = 0.02초)은 러너·S3 측정이 쓴다.
    // 물리(m/s·반응 초)는 두 길이에서 같고, 경기 시간으로 정한 값(체력 소모·회복)만 TimeScale로 맞춘다
    public readonly struct MatchLength
    {
        public readonly int RegulationTicks;      // 추가시간 없는 90분의 틱 수
        public readonly bool PlaysAddedTime;      // true면 추가시간만큼 틱을 더 돈다. false면 표시 시계만 늘린다(3분 압축의 09-27 규칙)

        public static readonly MatchLength Compressed = new MatchLength(MatchTuning.MatchTicks, false);
        public static readonly MatchLength RealTime = new MatchLength(MatchTuning.RealTimeTicks, true);

        // 게임 기본값은 여기 한 곳이다. S3 재튜닝이 끝나면 RealTime으로 바꾼다
        public static MatchLength Default => Compressed;

        public MatchLength(int regulationTicks, bool playsAddedTime)
        {
            RegulationTicks = regulationTicks;
            PlaysAddedTime = playsAddedTime;
        }

        // 3분 압축 대비 1틱이 덮는 경기 시간의 비. 압축이면 정확히 1이라 체력 값이 바이트까지 같다
        public float TimeScale => (float)MatchTuning.MatchTicks / RegulationTicks;

        public int TicksPerMinute => RegulationTicks / MatchClock.DisplayMinutes;

        // 이 경기가 실제로 도는 틱 수. 매니저·러너·시즌이 이 수만큼 Tick을 부른다
        public int TotalTicks(AddedTime added)
        {
            if (!PlaysAddedTime) { return RegulationTicks; }
            return RegulationTicks + TicksPerMinute * (added.FirstHalfMinutes + added.SecondHalfMinutes);
        }
    }
}
