namespace Game.Core.Placement
{
    // 한 자리에 놓을 수 있는가에 대한 답(WarTableSimulator에서 이식, 10-08). Ok가 아니면 왜 안 되는지가 곧 UI 색·메시지의 근거다.
    // 인구 상한은 뺐다: 이 게임은 로스터가 11명 고정이라 "정확히 11명, GK 1명"이 늘 참이다(스펙 §8 10-08 구현 설계)
    public enum PlacementVerdict
    {
        Ok,
        OutsideOwnHalf,   // 내 진영 절반 밖(스펙 §8: 배치 가능 영역 = 내 진영 절반)
        TooClose,         // 다른 선수와 최소 간격 미만
    }
}
