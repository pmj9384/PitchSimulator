using Game.Core.Match;
using Game.Core.Placement;

namespace Game.Core.AI
{
    // 전 선수 공통 행동 트리의 조립처. 22명이 이 트리 인스턴스 하나를 공유하고 판정값(Stats 다이얼)만 다르다(스펙 축).
    // 1주차 리트머스 판(스펙 §6의 "공 소유 중"과 "자유 공" 분기만). 아군 소유·상대 소유 분기는 2주차에 끼어든다.
    // 판단은 전부 MatchRules에 위임하고 여기선 사다리 순서만 정한다.
    public static class PlayerTreeBuilder
    {
        public static BehaviorNode BuildLitmus()
        {
            return new SelectorNode(

                // ① 소유 중 & 슛 확률이 성향(shotBias) 이상 → 쏜다. shotBias = "확률이 얼마 이상이면 쏘나"(스펙 §4-2)
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall && ShotChance(ctx) >= ctx.Stats.ShotBias),
                    new ActionNode(ctx => ctx.Shoot())),

                // ② 소유 중 → 상대 골 중심으로 드리블
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall),
                    new ActionNode(ctx => ctx.MoveToward(FieldBounds.HalfLength * ctx.AttackSign, 0f))),

                // ③ 자유 공 → 쫓는다. GK는 자기 페널티 박스 안에 있는 공만(스펙 §5 "박스 밖으로 안 나감")
                new SequenceNode(
                    new ConditionNode(ctx => ctx.BallPhase == BallPhase.Free
                        && (!ctx.IsGoalkeeper || MatchRules.IsInOwnPenaltyBox(ctx.BallX, ctx.BallZ, ctx.AttackSign))),
                    new ActionNode(ctx => ctx.MoveToward(ctx.BallX, ctx.BallZ))),

                // ④ 그 외 → 자리 유지(아무것도 안 함)
                new ActionNode(ctx => { }));
        }

        // 슛 확률은 GK 스탯이 필요한데 트리는 상대를 모른다. 소유자 기준 "정면에 GK가 있다"고 가정하지 않고
        // 순수 xG(능력치 보정 전)로 성향을 판단한다. 실제 판정(GK 보정 포함)은 시뮬이 Shoot 때 한다
        private static float ShotChance(IPlayerContext ctx)
        {
            return MatchRules.ShotProbability(ctx.X, ctx.Z, ctx.AttackSign, ctx.Stats.Shot, keeperReflexes: 50, keeperDiving: 50);
        }
    }
}
