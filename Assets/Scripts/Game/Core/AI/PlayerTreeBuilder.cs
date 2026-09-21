using System;
using System.Collections.Generic;
using Game.Core.Match;
using Game.Core.Placement;
using Game.Core.Tactics;

namespace Game.Core.AI
{
    // 전 선수 공통 행동 트리의 조립처. 22명이 이 트리 인스턴스 하나를 공유하고 판정값(팀 전술·개인 다이얼)만 다르다(스펙 축).
    // 4국면(스펙 §6, 09-18 확정 스펙 표): 공 소유 중 / 아군 소유 / 상대 소유 / 자유 공. 판단은 전부 순수 함수에 위임하고
    // 여기선 사다리 순서만 정한다. 노드는 무상태라 후보 목록 같은 작업 버퍼는 스레드 정적으로 둔다(EditMode·러너는 단일 스레드).
    public static class PlayerTreeBuilder
    {
        [ThreadStatic] private static List<TargetInfo>? candidateBuffer;

        public static BehaviorNode Build()
        {
            return new SelectorNode(

                // ── 공 소유 중 ────────────────────────────────────────────
                // ① GK가 잡았으면 배급(팀 GK 배급 설정). 슛·드리블 대신 늘 패스
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall && ctx.IsGoalkeeper),
                    new ActionNode(ctx => PassTo(ctx, KeeperTarget(ctx)))),

                // ② 슛 확률(진짜 상대 GK 스탯) ≥ 슛 성향 → 슛
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall && ShotChance(ctx) >= ctx.Stats.ShotBias),
                    new ActionNode(ctx => ctx.Shoot())),

                // ③ 역습 중 → 가장 앞선 아군에게, 안전 검사 없이
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall && ctx.IsCountering),
                    new ActionNode(ctx => PassTo(ctx, PassRules.CounterReceiver(ctx.Teammates, ctx.AttackSign, ctx.PlayerId)))),

                // ④ 안전한 앞선 아군이 있으면 최고점에 패스
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall && BestSafeReceiver(ctx) != -1),
                    new ActionNode(ctx => PassTo(ctx, BestSafeReceiver(ctx)))),

                // ⑤ 아니면 드리블. 드리블 성향이 낮으면 가장 가까운 아군 쪽으로 방향을 틀어 다음 틱 패스를 노린다
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall),
                    new ActionNode(ctx => DribbleTarget(ctx))),

                // ── 아군 소유 ────────────────────────────────────────────
                // ⑥ 나한테 오는 패스면 도착점으로 마중
                new SequenceNode(
                    new ConditionNode(ctx => ctx.IsPassTarget),
                    new ActionNode(ctx => ctx.MoveToward(ctx.PassTargetX, ctx.PassTargetZ))),

                // ⑦ 아군이 공을 가졌으면 공격 시 자리(+ 전진·폭 오프셋)로
                new SequenceNode(
                    new ConditionNode(ctx => ctx.BallOwnerTeam == ctx.Team),
                    new ActionNode(ctx => MoveToAttackHome(ctx))),

                // ── 상대 소유 ────────────────────────────────────────────
                // ⑧ 압박: 팀 압박 시작[상대 공 서드] × 개인 압박 거리(역압박 중 배율 ↑) 안이면 공으로
                new SequenceNode(
                    new ConditionNode(ctx => ctx.BallOwnerTeam == 1 - ctx.Team && ShouldPress(ctx)),
                    new ActionNode(ctx => ctx.MoveToward(ctx.BallX, ctx.BallZ))),

                // ⑨ 아니면 수비 시 자리(+ 라인 높이)로
                new SequenceNode(
                    new ConditionNode(ctx => ctx.BallOwnerTeam == 1 - ctx.Team),
                    new ActionNode(ctx => MoveToDefendHome(ctx))),

                // ── 자유 공 ──────────────────────────────────────────────
                // ⑩ 가장 가까운 선수가 쫓는다. GK는 개인 출격 반경 안(박스 상한) 공만
                new SequenceNode(
                    new ConditionNode(ctx => ctx.BallPhase != BallPhase.Owned && IsNearestToBall(ctx) && KeeperMayRush(ctx)),
                    new ActionNode(ctx => ctx.MoveToward(ctx.BallX, ctx.BallZ))),

                // ⑪ 나머지는 자리 유지(공격 시 자리 기준)
                new ActionNode(ctx => MoveToAttackHome(ctx)));
        }

        // 1주차 리트머스 트리(ST 1 vs GK 1 회귀 테스트가 쓴다)
        public static BehaviorNode BuildLitmus()
        {
            return new SelectorNode(
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall && ShotChance(ctx) >= ctx.Stats.ShotBias),
                    new ActionNode(ctx => ctx.Shoot())),
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall),
                    new ActionNode(ctx => ctx.MoveToward(FieldBounds.HalfLength * ctx.AttackSign, 0f))),
                new SequenceNode(
                    new ConditionNode(ctx => ctx.BallPhase == BallPhase.Free
                        && (!ctx.IsGoalkeeper || MatchRules.IsInOwnPenaltyBox(ctx.BallX, ctx.BallZ, ctx.AttackSign))),
                    new ActionNode(ctx => ctx.MoveToward(ctx.BallX, ctx.BallZ))),
                new ActionNode(ctx => { }));
        }

        // ── 판정 어댑터. 컨텍스트 값을 순수 함수 인자로 옮기기만 한다

        private static float ShotChance(IPlayerContext ctx)
        {
            int reflexes = ctx.OpponentKeeper != null ? ctx.OpponentKeeper.Reflexes : 50;
            int diving = ctx.OpponentKeeper != null ? ctx.OpponentKeeper.Diving : 50;
            return MatchRules.ShotProbability(ctx.X, ctx.Z, ctx.AttackSign, ctx.Stats.Shot, reflexes, diving);
        }

        private static int Third(IPlayerContext ctx)
        {
            return (int)ctx.BallThird;
        }

        // 리시버 후보 중 최고점이면서 안전한 아군. 서드별 팀 값(패스 방식·리스크·폭)에 개인 패스 길이·리스크가 얹힌다
        private static int BestSafeReceiver(IPlayerContext ctx)
        {
            int third = Third(ctx);
            TeamTactics t = ctx.Tactics;
            float riskAllow = Math.Max(MatchTuning.PassRiskAllow[t.PassRisk[third]], ctx.Stats.PassRisk);
            float ballSpeed = MatchTuning.PassSpeed[t.Tempo[third]];

            // 옆·뒤 돌리기는 압박받을 때만(09-21 3분 계측: 압박 없이도 돌리니 앞·뒤 패스가 158·157로 교대하고 아무도 몰지 않아 박스에 못 들어감).
            // 압박이 없으면 앞 후보가 없을 때 ⑤ 드리블로 떨어진다
            bool pressed = IsPressed(ctx);

            int best = -1;
            float bestScore = 0f;
            IReadOnlyList<TargetInfo> mates = ctx.Teammates;
            for (int i = 0; i < mates.Count; i++)
            {
                TargetInfo m = mates[i];
                if (!pressed && (m.X - ctx.X) * ctx.AttackSign <= 0f) { continue; }
                float score = PassRules.ScoreReceiver(ctx.X, ctx.Z, m.X, m.Z, ctx.AttackSign, t.PassStyle[third], ctx.Stats.PassLength, t.Width[third]);
                if (score <= bestScore) { continue; }

                // 안전 판정은 실제 착지점(리드 목표)까지(09-21 3분 계측: 리시버 위치까지만 보면 그 앞 8m에 선 수비수가 걸러져
                // 롱패스가 늘 먹혔다). 리시버 속도는 트리가 개인 스탯을 모르니 평균(speed 50)으로, 킥은 시뮬이 실제 스탯으로 찬다
                float ddx = m.X - ctx.X;
                float ddz = m.Z - ctx.Z;
                float passDistance = (float)Math.Sqrt(ddx * ddx + ddz * ddz);
                (float x, float z) landing = PassRules.LeadTarget(ctx.X, m.X, m.Z, ctx.AttackSign, passDistance, ballSpeed, MatchTuning.SpeedMpsAt50);
                float risk = PassRules.InterceptRisk(ctx.X, ctx.Z, landing.x, landing.z, ctx.Opponents, MatchTuning.InterceptRunSpeed, ballSpeed);
                if (!PassRules.IsPassSafe(risk, riskAllow)) { continue; }

                best = m.PlayerId;
                bestScore = score;
            }
            return best;
        }

        private static bool IsPressed(IPlayerContext ctx)
        {
            IReadOnlyList<TargetInfo> opp = ctx.Opponents;
            float r2 = MatchTuning.PressedRadius * MatchTuning.PressedRadius;
            for (int i = 0; i < opp.Count; i++)
            {
                float dx = opp[i].X - ctx.X;
                float dz = opp[i].Z - ctx.Z;
                if (dx * dx + dz * dz <= r2) { return true; }
            }
            return false;
        }

        private static int KeeperTarget(IPlayerContext ctx)
        {
            // 배급 대상 선정은 팀 설정(짧게·섞어·길게)이 필요한데 "섞어"의 교대는 상태라 시뮬이 든다.
            // 트리는 팀 값으로 짧게/길게만 고르고, 섞어면 가까운 쪽(안전)으로 둔다. 교대는 시뮬 KeeperDistributionTarget이 맡는다(2차)
            int level = ctx.Tactics.GkDistribution;
            return PassRules.KeeperDistributionTarget(ctx.X, ctx.Z, ctx.Teammates, ctx.AttackSign, level == 1 ? 0 : level, ctx.PlayerId, alternate: false);
        }

        private static void PassTo(IPlayerContext ctx, int receiverId)
        {
            if (receiverId == -1) { DribbleTarget(ctx); return; }
            ctx.Pass(receiverId);
        }

        // 드리블 목표: 기본은 상대 골 중심. 드리블 성향이 낮으면 가장 가까운 아군 쪽(다음 틱 패스 후보를 만든다)
        private static void DribbleTarget(IPlayerContext ctx)
        {
            if (ctx.Stats.Dribble < MatchTuning.DribbleReluctance)
            {
                int nearest = TargetSelector.SelectNearest(ctx.X, ctx.Z, ctx.Teammates);
                if (nearest != -1)
                {
                    TargetInfo m = Find(ctx.Teammates, nearest);
                    ctx.MoveToward(m.X, m.Z);
                    return;
                }
            }
            ctx.MoveToward(FieldBounds.HalfLength * ctx.AttackSign, 0f);
        }

        private static void MoveToAttackHome(IPlayerContext ctx)
        {
            int third = Third(ctx);
            (float x, float z) home = PositionRules.AttackHome(ctx.AttackHomeX, ctx.AttackHomeZ, ctx.AttackSign,
                ctx.Tactics.Mentality, ctx.Stats.PushUp, ctx.Tactics.Width[third], ctx.Stats.Width);
            ctx.MoveToward(home.x, home.z);
        }

        private static void MoveToDefendHome(IPlayerContext ctx)
        {
            (float x, float z) home = PositionRules.DefendHome(ctx.DefendHomeX, ctx.DefendHomeZ, ctx.AttackSign, ctx.Stats.LineHeight);
            ctx.MoveToward(home.x, home.z);
        }

        private static bool ShouldPress(IPlayerContext ctx)
        {
            float dx = ctx.BallX - ctx.X;
            float dz = ctx.BallZ - ctx.Z;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            // 상대 공이 있는 서드 = 내 팀 기준 서드 그대로(공 위치는 하나). 압박 시작 열은 "상대 공이 어디 있나"로 읽는다
            return PressRules.ShouldPress(dist, ctx.Stats.PressRange, ctx.Tactics.PressStart[Third(ctx)], ctx.IsCounterPressing);
        }

        private static bool IsNearestToBall(IPlayerContext ctx)
        {
            List<TargetInfo> all = candidateBuffer ??= new List<TargetInfo>();
            all.Clear();
            all.Add(new TargetInfo(ctx.PlayerId, ctx.X, ctx.Z));
            for (int i = 0; i < ctx.Teammates.Count; i++) { all.Add(ctx.Teammates[i]); }
            for (int i = 0; i < ctx.Opponents.Count; i++) { all.Add(ctx.Opponents[i]); }
            return TargetSelector.SelectNearest(ctx.BallX, ctx.BallZ, all) == ctx.PlayerId;
        }

        private static bool KeeperMayRush(IPlayerContext ctx)
        {
            if (!ctx.IsGoalkeeper) { return true; }
            float dx = ctx.BallX - ctx.X;
            float dz = ctx.BallZ - ctx.Z;
            return dx * dx + dz * dz <= ctx.Stats.GkRushRadius * ctx.Stats.GkRushRadius
                && MatchRules.IsInOwnPenaltyBox(ctx.BallX, ctx.BallZ, ctx.AttackSign);
        }

        private static TargetInfo Find(IReadOnlyList<TargetInfo> list, int playerId)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].PlayerId == playerId) { return list[i]; }
            }
            throw new InvalidOperationException($"PlayerId {playerId}가 목록에 없다");
        }
    }
}
