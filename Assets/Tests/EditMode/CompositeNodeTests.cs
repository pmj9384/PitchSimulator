using Game.Core.AI;
using NUnit.Framework;

// BT 엔진 검증. 가짜 선수(FakePlayer)에게 노드를 틱해 "어떤 명령을 내렸는가"만 본다.
// 트리는 무상태라 인스턴스 하나를 전 선수가 공유한다. 마지막 테스트가 그 축을 증명한다.
// 축구 트리 내용(스펙 §6)은 설계 뒤 PlayerTreeBuilder 테스트로 따로 붙는다.
public class CompositeNodeTests
{
    // 기록장 달린 가짜 선수. 노드가 시킨 횟수를 센다
    private class FakePlayer : IPlayerContext
    {
        public bool Flag;
        public int ACalls;
        public int BCalls;
    }

    private static BehaviorNode Cond(bool value)
    {
        return new ConditionNode(ctx => value);
    }

    [Test]
    public void Selector는_처음_성공한_자식에서_멈춘다()
    {
        var player = new FakePlayer();
        var tree = new SelectorNode(
            new SequenceNode(Cond(false), new ActionNode(ctx => ((FakePlayer)ctx).ACalls++)),
            new ActionNode(ctx => ((FakePlayer)ctx).BCalls++),
            new ActionNode(ctx => ((FakePlayer)ctx).ACalls++));

        Assert.AreEqual(NodeState.Success, tree.Tick(player));
        Assert.AreEqual(0, player.ACalls, "첫 자식은 조건 실패로 행동 안 함, 셋째는 도달 안 함");
        Assert.AreEqual(1, player.BCalls);
    }

    [Test]
    public void Selector는_전부_실패하면_실패다()
    {
        var tree = new SelectorNode(Cond(false), Cond(false));
        Assert.AreEqual(NodeState.Failure, tree.Tick(new FakePlayer()));
    }

    [Test]
    public void Sequence는_조건이_깨진_자리에서_멈춘다()
    {
        var player = new FakePlayer();
        var tree = new SequenceNode(
            new ActionNode(ctx => ((FakePlayer)ctx).ACalls++),
            Cond(false),
            new ActionNode(ctx => ((FakePlayer)ctx).BCalls++));

        Assert.AreEqual(NodeState.Failure, tree.Tick(player));
        Assert.AreEqual(1, player.ACalls);
        Assert.AreEqual(0, player.BCalls, "조건 뒤 행동은 실행되지 않는다");
    }

    [Test]
    public void Condition은_컨텍스트를_읽어_판정한다()
    {
        var node = new ConditionNode(ctx => ((FakePlayer)ctx).Flag);
        Assert.AreEqual(NodeState.Failure, node.Tick(new FakePlayer { Flag = false }));
        Assert.AreEqual(NodeState.Success, node.Tick(new FakePlayer { Flag = true }));
    }

    [Test]
    public void 트리_하나를_여러_선수가_공유해도_서로_안_섞인다()
    {
        // "전 선수가 같은 트리" 축의 증명. 같은 인스턴스로 상태 다른 두 선수를 교차 틱
        BehaviorNode shared = new SelectorNode(
            new SequenceNode(new ConditionNode(ctx => ((FakePlayer)ctx).Flag), new ActionNode(ctx => ((FakePlayer)ctx).ACalls++)),
            new ActionNode(ctx => ((FakePlayer)ctx).BCalls++));
        var striker = new FakePlayer { Flag = true };
        var keeper = new FakePlayer { Flag = false };

        shared.Tick(striker);
        shared.Tick(keeper);
        shared.Tick(striker);   // 노드에 상태가 남아 있으면 여기서 오염된다

        Assert.AreEqual(2, striker.ACalls);
        Assert.AreEqual(0, striker.BCalls);
        Assert.AreEqual(0, keeper.ACalls);
        Assert.AreEqual(1, keeper.BCalls);
    }
}
