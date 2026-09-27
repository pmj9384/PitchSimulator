namespace Game.Core.Match
{
    // 추가시간 표시 규칙(09-27 유저 결정, 경기-흐름-대조 #3·#25). 시뮬엔 중단이 없어 "실제로 늘어난 시간"은 없다.
    // 그래서 경기 길이(MatchTicks)는 그대로 두고 표시 시계만 90 + 추가분을 9,000틱에 나눠 흐르게 한다. 값은 시드에서 결정적으로 뽑아
    // 같은 경기는 늘 같은 추가시간을 보인다(경기 난수 수열은 건드리지 않는다). 중계 관례: 전반 1~2분, 후반 2~5분
    public readonly struct AddedTime
    {
        public readonly int FirstHalfMinutes;
        public readonly int SecondHalfMinutes;

        public static readonly AddedTime None = new AddedTime(0, 0);

        public AddedTime(int firstHalfMinutes, int secondHalfMinutes)
        {
            FirstHalfMinutes = firstHalfMinutes;
            SecondHalfMinutes = secondHalfMinutes;
        }

        public static AddedTime FromSeed(int seed)
        {
            uint h = unchecked((uint)seed * 2654435761u);   // Knuth 곱셈 해시. 시드 1·2·3처럼 붙어 있어도 값이 흩어진다
            h ^= h >> 15;
            return new AddedTime(1 + (int)(h % 2u), 2 + (int)((h >> 8) % 4u));
        }
    }

    // 시계 한 순간의 읽기. HUD가 문자열로 만든다(코어는 언어를 모른다)
    public readonly struct ClockReading
    {
        public readonly bool SecondHalf;
        public readonly int Minute;        // 추가시간이 아니면 0~90
        public readonly int Second;
        public readonly bool InAddedTime;  // true면 "45+" 또는 "90+" 뒤에 AddedMinute:AddedSecond
        public readonly int AddedMinute;
        public readonly int AddedSecond;

        public ClockReading(bool secondHalf, int minute, int second, bool inAddedTime, int addedMinute, int addedSecond)
        {
            SecondHalf = secondHalf;
            Minute = minute;
            Second = second;
            InAddedTime = inAddedTime;
            AddedMinute = addedMinute;
            AddedSecond = addedSecond;
        }
    }
}
