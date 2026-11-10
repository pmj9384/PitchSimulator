using Game.Core.Match;

// 개발·녹화용 경기 길이 스위치(11-10). 게임 기본값은 3분 압축(MatchLength.Default)인데, 튜닝·대시보드는 실제 90분으로 잰다.
// 에디터 치트 창에서 켜면 인게임(MatchManager)과 치트 헤드리스(SeasonSystem)가 둘 다 실제 90분으로 경기를 만든다(같은 조립 함수라 같은 경기가 나와야 한다).
// 출시 화면 길이는 하이라이트 모드(S4)에서 정한다. 정적 값이라 Play를 끄거나 도메인이 다시 로드되면 꺼진다
public static class DevMatchLength
{
    public static bool RealTime { get; set; }

    public static MatchLength Current
    {
        get
        {
            if (RealTime) { return MatchLength.RealTime; }
            return MatchLength.Default;
        }
    }
}
