namespace Game.Core.AI
{
    // BT가 선수에게 묻고(읽기) 시키는(행동) 유일한 창구. 트리는 MonoBehaviour를 모르고 이 계약만 본다.
    // 가짜 구현만 있으면 엔진 없이 트리를 테스트한다(CompositeNodeTests).
    //
    // 1차 이식(09-16)엔 멤버가 없다. "공 소유 중 / 아군 소유 / 상대 소유 / 자유 공"에서 무엇을 묻고 시킬지는
    // 공·소유·슛 설계 루프(스펙 §5·§6)에서 정한 뒤 채운다. 쓰지 않는 계약을 미리 두지 않는다.
    public interface IPlayerContext
    {
    }
}
