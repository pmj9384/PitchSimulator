using System;

namespace Game.Core.AI
{
    // Selector: 자식을 위에서부터 시도, 처음으로 Failure가 아닌 결과를 낸 자식이 이긴다. "우선순위 사다리"
    public sealed class SelectorNode : BehaviorNode
    {
        private readonly BehaviorNode[] children;
        public SelectorNode(params BehaviorNode[] children) { this.children = children; }

        public override NodeState Tick(IPlayerContext ctx)
        {
            for (int i = 0; i < children.Length; i++)
            {
                NodeState result = children[i].Tick(ctx);
                if (result != NodeState.Failure) { return result; }
            }
            return NodeState.Failure;
        }
    }

    // Sequence: 자식을 차례로 실행, 하나라도 Success가 아니면 거기서 중단. "조건 만족 시 행동"의 짝
    public sealed class SequenceNode : BehaviorNode
    {
        private readonly BehaviorNode[] children;
        public SequenceNode(params BehaviorNode[] children) { this.children = children; }

        public override NodeState Tick(IPlayerContext ctx)
        {
            for (int i = 0; i < children.Length; i++)
            {
                NodeState result = children[i].Tick(ctx);
                if (result != NodeState.Success) { return result; }
            }
            return NodeState.Success;
        }
    }

    // Condition: 판단은 여기서도 안 한다. 받은 함수(MatchRules 호출)를 실행할 뿐
    public sealed class ConditionNode : BehaviorNode
    {
        private readonly Func<IPlayerContext, bool> predicate;
        public ConditionNode(Func<IPlayerContext, bool> predicate) { this.predicate = predicate; }

        public override NodeState Tick(IPlayerContext ctx)
        {
            return predicate(ctx) ? NodeState.Success : NodeState.Failure;
        }
    }

    // Action: 선수에게 행동을 시키고 성공 보고. 실행 결과 판정이 필요해지면 Func<ctx,NodeState> 버전을 추가
    public sealed class ActionNode : BehaviorNode
    {
        private readonly Action<IPlayerContext> action;
        public ActionNode(Action<IPlayerContext> action) { this.action = action; }

        public override NodeState Tick(IPlayerContext ctx)
        {
            action(ctx);
            return NodeState.Success;
        }
    }
}
