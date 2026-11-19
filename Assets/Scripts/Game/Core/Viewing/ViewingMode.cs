namespace Game.Core.Viewing
{
    // 보기 모드(11-05 유저 확정, S4). 결과는 모드와 무관하다: 모드가 바꾸는 것은 어느 틱을 화면에 보여 주느냐뿐이다
    public enum ViewingMode
    {
        Key,        // 핵심: 골 + 큰 찬스
        Extended,   // 확장(기본): 핵심 + 유효 슛 + 위험한 블록
        Full        // 전체: 90분 전부(배속 ×1·×2·×4)
    }
}
