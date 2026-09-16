namespace Game.Core.AI
{
    public enum NodeState { Success, Failure, Running }
    // Running은 아직 안 쓴다. 재평가식(매 틱 루트부터)이라 노드가 진행 상태를 기억할 일이 없다.
    // 슛 모션·다이브 같은 "여러 틱짜리 행동"이 생기면 그때 쓴다.

    // BT 노드의 뿌리. 무상태가 규칙이다: 노드는 아무것도 기억하지 않고, 선수의 상태는 전부 ctx(IPlayerContext) 쪽에 있다.
    // 그래서 트리 인스턴스 하나를 22명이 공유한다(스펙 축 "전 선수가 같은 트리, 판정값만 다름").
    public abstract class BehaviorNode
    {
        public abstract NodeState Tick(IPlayerContext ctx);
    }
}
