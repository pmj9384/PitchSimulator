namespace Game.Core.Viewing
{
    // 보기 모드(11-05 유저 확정, S4. 화면 이름은 11-19 유저 Play 피드백으로 바꿨다). 결과는 모드와 무관하다: 모드가 바꾸는 것은 어느 틱을 화면에 보여 주느냐뿐이다.
    // 경기 중에도 바꿀 수 있다(MatchManager.SetViewingMode). 값은 PlayerPrefs에 저장돼 있어 이름·순서를 바꾸지 않는다
    public enum ViewingMode
    {
        Key,        // 주요 장면: 골 + 큰 찬스
        Extended,   // 하이라이트(기본): 주요 장면 + 유효 슛 + 위험한 블록
        Full        // 전체 경기: 90분 전부
    }
}
