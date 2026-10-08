using System;
using System.Collections.Generic;
using Game.Core.Match;
using Game.Core.Placement;
using Game.Core.Tactics;

namespace Game.Core.AI
{
    // 전 선수 공통 행동 트리의 조립처. 22명이 이 트리 인스턴스 하나를 공유하고 판정값(팀 전술·개인 다이얼)만 다르다(스펙 축).
    // 4국면(스펙 §6, 09-18 확정 스펙 표): 공 소유 중 / 아군 소유 / 상대 소유 / 자유 공. 판단은 전부 순수 함수에 위임하고
    // 여기선 사다리 순서만 정한다. 노드는 무상태라 후보 목록 같은 작업 버퍼는 스레드 정적으로 둔다(EditMode·러너는 단일 스레드, 인게임 결과 보고의 나머지 경기는 백그라운드 스레드 09-28 G1).
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
                    new ActionNode(ctx => PassUnchecked(ctx, KeeperTarget(ctx)))),

                // ①b 킥오프 키커는 첫 행동으로 옆·뒤 동료에게 내준다(09-29, IFAB 8조 + 실제 킥오프 관행). 슛·드리블보다 먼저
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall && ctx.IsKickoffTaker),
                    new ActionNode(ctx => PassUnchecked(ctx, PassRules.KickoffReceiver(ctx.X, ctx.Z, ctx.Teammates, ctx.AttackSign)))),

                // ② 슛 확률(진짜 상대 GK 스탯) ≥ 슛 성향 → 슛
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall && WantsShot(ctx)),
                    new ActionNode(ctx => ctx.Shoot())),

                // ②c 운반(10-08 결정 7, 09-30 B 실험): 공격수(ST·AM·W)가 슛 사거리 안·우리 진영 밖에서 앞 CarryComfortZone 안에 상대가 없으면
                // 역습·돌파·패스보다 먼저 골 쪽으로 몬다. 전엔 슛 문턱 밖에서 받은 공격수가 늘 다시 패스해서 "ST가 정면 11m에 서 있을 때"만 득점이 났다
                // (ST 전진 폭 20이면 0.17골, 25면 0.47). 돌파 뒤·패스 앞에 두면 전진 20이 0.10으로 더 죽었다(MatchTuning.CarryComfortZone 주석)
                new SequenceNode(
                    new ConditionNode(ctx => ShouldCarry(ctx)),
                    new ActionNode(ctx => ctx.MoveToward(FieldBounds.HalfLength * ctx.AttackSign, 0f))),

                // ③ 역습 중 → 나보다 확실히 앞선 아군 중 가장 앞선 이에게, 안전 검사 없이(스펙 §6 "첫 패스 전방"). 앞선 아군이 없으면 ④·⑤로
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall && ctx.IsCountering && CounterTarget(ctx) != -1),
                    new ActionNode(ctx => PassUnchecked(ctx, CounterTarget(ctx)))),

                // ③b 돌파(09-29): 이번 소유에 돌파 의도가 있고 앞 8m 안에 상대가 있으면 패스 대신 제치러 간다. 방향은 상대가 먼저 못 닿는 쪽(HELIOS식).
                // 앞에 상대가 없으면(제쳤으면) 아래 평소 판단으로. 의도는 시뮬이 소유 때 드리블 성향으로 굴린다(윙어 0.9·타깃맨 0.2)
                new SequenceNode(
                    new ConditionNode(ctx => ShouldTakeOn(ctx)),
                    new ActionNode(ctx => TakeOn(ctx))),

                // ④ 안전한 앞선 아군이 있으면 최고점에 패스. 착지점은 받는 선수 앞 후보 중 안전한 가장 긴 것(09-30)
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall && BestSafeReceiver(ctx).Found),
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
                // ⑧ 압박: 팀 안 압박 순위가 상한 안이면 공으로. 누가 후보인지(팀 압박 시작[상대 공 서드] × 개인 압박 거리, 역압박 배율, 박스 앞 CB 전진)는
                // 시뮬이 한 곳에서 정해 순위(PressRank)로 준다(09-29: 트리가 거리를 다시 재면 시뮬이 넣은 CB를 도로 걸러 판정과 실행이 갈렸다).
                // 나머지는 ⑨ 수비 자리(슬라이드가 블록을 좁힌다)(09-23 뭉침)
                new SequenceNode(
                    new ConditionNode(ctx => ctx.BallOwnerTeam == 1 - ctx.Team && ctx.PressRank < MatchTuning.MaxPressers),
                    new ActionNode(ctx => ChaseBall(ctx))),

                // ⑨ 아니면 수비 시 자리(+ 라인 높이)로
                new SequenceNode(
                    new ConditionNode(ctx => ctx.BallOwnerTeam == 1 - ctx.Team),
                    new ActionNode(ctx => MoveToDefendHome(ctx))),

                // ── 자유 공 ──────────────────────────────────────────────
                // ⑩ 팀마다 가장 가까운 1명이 쫓는다(09-28, Simple Soccer isClosestTeamMemberToBall). 22명 중 1명만 쫓으면 루즈볼을 양 팀이 다투지 않았다(20판 계측: 양 팀 동시 추격 1.8%).
                // GK는 개인 출격 반경 안(박스 상한) 공만. 필드 플레이어 추격자는 시뮬이 우리 GK를 뺀 후보에서 고른다(09-23 리뷰 R3: 최근접이 출격 못 하는 GK면 아무도 안 쫓아 파링 공이 박스 밖에 멈추면 영구 정지였다).
                // 담당은 도전자가 2m 넘게 가까워야 바뀐다(09-28 F1b): 틱마다 새로 뽑으니 거의 같은 거리의 두 동료가 번갈아 붙다 말다 했다
                new SequenceNode(
                    new ConditionNode(ctx => ctx.BallPhase != BallPhase.Owned && MayChaseFreeBall(ctx)),
                    new ActionNode(ctx => ChaseBall(ctx))),

                // ⑪ 나머지는 자리 유지(공격 시 자리 기준)
                new ActionNode(ctx => MoveToAttackHome(ctx)));
        }

        // 1주차 리트머스 트리(ST 1 vs GK 1 회귀 테스트가 쓴다)
        public static BehaviorNode BuildLitmus()
        {
            return new SelectorNode(
                new SequenceNode(
                    // 리트머스는 옛 비교식 그대로(테스트 전용. 주사위 수열 테스트가 슛 위치에 묶여 있어 스케일을 안 건다)
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

        // 슛 성향은 0~1 다이얼, xG는 0~0.3이 실용 범위라 스케일을 맞춰 비교한다(MatchTuning.ShotBiasXgScale)
        private static bool WantsShot(IPlayerContext ctx)
        {
            return ShotChance(ctx) >= ctx.Stats.ShotBias * MatchTuning.ShotBiasXgScale;
        }

        private static float ShotChance(IPlayerContext ctx)
        {
            int reflexes = ctx.OpponentKeeper != null ? ctx.OpponentKeeper.Reflexes : 50;
            int diving = ctx.OpponentKeeper != null ? ctx.OpponentKeeper.Diving : 50;
            float chance = MatchRules.ShotProbability(ctx.X, ctx.Z, ctx.AttackSign, ctx.Stats.Shot, reflexes, diving);
            if (!IsPressed(ctx)) { return chance; }
            return MatchRules.PressuredShotProbability(chance, MatchTuning.ShotPressureLogit);   // 시뮬 Shoot과 같은 식(09-29 D3)
        }

        private static int Third(IPlayerContext ctx)
        {
            return (int)ctx.BallThird;
        }

        // 패스 리스크 허용치: 서드별 팀 값과 개인 다이얼 중 큰 쪽
        private static float RiskAllow(IPlayerContext ctx)
        {
            return Math.Max(MatchTuning.PassRiskAllow[ctx.Tactics.PassRisk[Third(ctx)]], ctx.Stats.PassRisk);
        }

        // 패스가 목표점에 닿을 때 남는 속도(서드별 팀 템포). 킥 초속은 거리로 역산한다(시뮬 Pass와 같은 식)
        private static float ArrivalSpeed(IPlayerContext ctx)
        {
            return MatchTuning.PassArrivalSpeed[ctx.Tactics.Tempo[Third(ctx)]];
        }

        // 리시버 후보 중 최고점이면서 안전한 아군과 그 착지점. 서드별 팀 값(패스 방식·리스크·폭)에 개인 패스 길이·리스크가 얹힌다
        private static PassChoice BestSafeReceiver(IPlayerContext ctx)
        {
            int third = Third(ctx);
            TeamTactics t = ctx.Tactics;
            float riskAllow = RiskAllow(ctx);
            float arrival = ArrivalSpeed(ctx);

            // 옆·뒤 돌리기는 압박받을 때만(09-21 3분 계측: 압박 없이도 돌리니 앞·뒤 패스가 158·157로 교대하고 아무도 몰지 않아 박스에 못 들어감).
            // 압박이 없으면 앞 후보가 없을 때 ⑤ 드리블로 떨어진다
            bool pressed = IsPressed(ctx);

            float onsideLine = OnsideLine(ctx);

            PassChoice best = PassChoice.None;
            float bestScore = 0f;
            PassChoice returnFallback = PassChoice.None;   // 방금 나에게 준 선수에게 곧바로 뒤로 되돌리는 패스는 다른 후보가 없을 때만(09-23: W↔ST 측면 왕복 22회)
            IReadOnlyList<TargetInfo> mates = ctx.Teammates;
            for (int i = 0; i < mates.Count; i++)
            {
                TargetInfo m = mates[i];
                bool backward = (m.X - ctx.X) * ctx.AttackSign <= 0f;
                if (!pressed && backward) { continue; }
                bool isReturn = backward && m.PlayerId == ctx.LastPasserId;
                if (OffsideRules.IsOffsidePosition(m.X, ctx.AttackSign, onsideLine)) { continue; }   // 오프사이드 위치 아군에겐 안 준다(09-23)
                float score = PassRules.ScoreReceiver(ctx.X, ctx.Z, m.X, m.Z, ctx.AttackSign, t.PassStyle[third], ctx.Stats.PassLength, t.Width[third]);
                if (score <= bestScore) { continue; }

                // 안전 판정은 실제 착지점까지(09-21 3분 계측: 리시버 위치까지만 보면 그 앞 8m에 선 수비수가 걸러져 롱패스가 늘 먹혔다).
                // 착지점은 후보 중 안전한 가장 긴 것(09-30). 고른 점을 그대로 시뮬에 넘겨 판정과 실행이 같은 점을 본다
                PassLanding landing = PassRules.PickLanding(ctx.X, ctx.Z, m.X, m.Z, ctx.AttackSign, arrival, ctx.Opponents, riskAllow);
                if (!PassRules.IsPassSafe(landing.Risk, riskAllow)) { continue; }

                var choice = new PassChoice(m.PlayerId, landing.X, landing.Z);
                if (isReturn)
                {
                    if (!returnFallback.Found) { returnFallback = choice; }
                    continue;
                }
                best = choice;
                bestScore = score;
            }
            return best.Found ? best : returnFallback;
        }

        // 공을 쫓을 땐 공의 앞을 향해(추격 예측). 압박(⑧)과 자유 공(⑩)이 같이 쓴다
        private static void ChaseBall(IPlayerContext ctx)
        {
            (float x, float z) aim = PressRules.PursuitPoint(ctx.X, ctx.Z, MatchRules.SpeedMps(ctx.Stats.Speed), ctx.BallX, ctx.BallZ, ctx.BallVelX, ctx.BallVelZ);
            ctx.MoveToward(aim.x, aim.z);
        }

        // 온사이드 선(공격 방향 좌표). GK 배급은 골킥이라 오프사이드가 없다
        private static float OnsideLine(IPlayerContext ctx)
        {
            return ctx.IsGoalkeeper ? OffsideRules.NoLine : OffsideRules.OnsideLine(ctx.Opponents, ctx.AttackSign, ctx.BallX);
        }

        private static int CounterTarget(IPlayerContext ctx)
        {
            return PassRules.CounterReceiver(ctx.X, ctx.Teammates, ctx.AttackSign, ctx.PlayerId, OnsideLine(ctx));
        }

        private static bool IsPressed(IPlayerContext ctx)
        {
            return HasOpponentWithin(ctx, MatchTuning.PressedRadius);
        }

        private static bool HasOpponentWithin(IPlayerContext ctx, float radius)
        {
            IReadOnlyList<TargetInfo> opp = ctx.Opponents;
            float r2 = radius * radius;
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
            // 팀 설정(짧게·섞어·길게). "섞어"의 교대 스위치는 상태라 시뮬이 들고 스냅샷(KeeperAlternate)으로 준다(09-23 R2. 그전엔 섞어가 짧게로 고정).
            // 발 뻗는 범위 안에 상대가 붙어 있으면 짧은 배급은 릴리스 지점에서 끊긴다(GK 배급엔 안전 판정이 없다) → 길게 찬다(09-23 탐지 E)
            int level = HasOpponentWithin(ctx, MatchTuning.InterceptReach) ? 2 : ctx.Tactics.GkDistribution;
            return PassRules.KeeperDistributionTarget(ctx.X, ctx.Z, ctx.Teammates, ctx.AttackSign, level, ctx.PlayerId, ctx.KeeperAlternate);
        }

        private static void PassTo(IPlayerContext ctx, PassChoice choice)
        {
            if (!choice.Found)
            {
                DribbleTarget(ctx);
                return;
            }
            ctx.Pass(choice.ReceiverId, choice.X, choice.Z);
        }

        // 안전 검사 없이 주는 패스(GK 배급 ①·킥오프 ①b·역습 ③). 착지점은 받는 선수 앞 긴 리드 그대로
        private static void PassUnchecked(IPlayerContext ctx, int receiverId)
        {
            if (receiverId == -1)
            {
                DribbleTarget(ctx);
                return;
            }
            TargetInfo m = Find(ctx.Teammates, receiverId);
            (float x, float z) landing = PassRules.LandingPoint(ctx.X, ctx.Z, m.X, m.Z, ctx.AttackSign, ArrivalSpeed(ctx), MatchTuning.PassLeadMax);
            ctx.Pass(receiverId, landing.x, landing.z);
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

        // 운반 조건: 공격수가 공을 갖고 있고, 우리 진영 서드가 아니고, 슛 사거리 안이고, 앞 여유 거리 안에 상대가 없다. 역할은 PlayerTable의 자리 id로 본다
        private static bool ShouldCarry(IPlayerContext ctx)
        {
            if (!ctx.OwnsBall || ctx.IsGoalkeeper) { return false; }
            if (ctx.BallThird == Tactics.Third.Own) { return false; }
            if (!IsCarryRole(ctx.Stats.RoleId)) { return false; }
            if (MatchRules.ShotDistance(ctx.X, ctx.Z, ctx.AttackSign) > MatchTuning.MaxShotRange) { return false; }
            return !DribbleRules.HasDefenderAhead(ctx.X, ctx.Z, ctx.AttackSign, ctx.Opponents, MatchTuning.CarryComfortZone);
        }

        private static bool IsCarryRole(string roleId)
        {
            for (int i = 0; i < MatchTuning.CarryRoles.Length; i++)
            {
                if (string.Equals(MatchTuning.CarryRoles[i], roleId, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        // 돌파 조건: 공을 갖고 있고, 이번 소유에 돌파 의도가 있고, 앞 8m 안에 상대가 있다
        private static bool ShouldTakeOn(IPlayerContext ctx)
        {
            if (!ctx.OwnsBall || !ctx.WantsTakeOn) { return false; }
            return DribbleRules.HasDefenderAhead(ctx.X, ctx.Z, ctx.AttackSign, ctx.Opponents, MatchTuning.TakeOnEngageRange);
        }

        // 돌파 목표. 내 속도는 공을 몬 속도(시뮬 Apply와 같은 DribbleFactor), 상대는 평균 달리기 속도.
        // 상대가 "닿는" 거리는 실제로 공을 뺏는 태클 사거리(시뮬 ResolveTackles와 같은 TackleRange). 09-29 리뷰: 발 뻗는 1.2m로 판정하면
        // 안전하다고 고른 방향에서도 2m 태클이 걸린다(판정과 실행이 다른 전제)
        private static void TakeOn(IPlayerContext ctx)
        {
            float carry = MatchRules.SpeedMps(ctx.Stats.Speed) * MatchTuning.DribbleFactor;
            (float x, float z) aim = DribbleRules.TakeOnTarget(ctx.X, ctx.Z, ctx.AttackSign, ctx.Opponents, carry,
                MatchTuning.InterceptRunSpeed, MatchTuning.TackleRange, MatchTuning.TakeOnLookahead, MatchTuning.TakeOnAngleStep,
                FieldBounds.HalfLength, FieldBounds.HalfWidth - MatchTuning.TouchlineMargin);
            ctx.MoveToward(aim.x, aim.z);
        }

        private static void MoveToAttackHome(IPlayerContext ctx)
        {
            int third = Third(ctx);
            (float x, float z) home = PositionRules.AttackHome(ctx.AttackHomeX, ctx.AttackHomeZ, ctx.AttackSign,
                ctx.Tactics.Mentality, ctx.Stats.PushUp, ctx.Tactics.Width[third], ctx.Stats.Width);
            (float x, float z) slid = PositionRules.SlideTowardBall(home.x, home.z, ctx.BallX, ctx.BallZ, defending: false, ctx.IsGoalkeeper);
            float x = slid.x;
            if (ctx.BallOwnerTeam == ctx.Team && !ctx.OwnsBall && !ctx.IsGoalkeeper)
            {
                // 아군 소유 중엔 온사이드 선 뒤에 선다(09-23). 라인 뒤에 서 있으면 받아도 오프사이드라 애초에 안 간다
                x = OffsideRules.ClampOnside(x, ctx.AttackSign, OffsideRules.OnsideLine(ctx.Opponents, ctx.AttackSign, ctx.BallX), MatchTuning.OnsideMargin);
            }
            ctx.MoveToward(x, slid.z);
        }

        private static void MoveToDefendHome(IPlayerContext ctx)
        {
            (float x, float z) home = PositionRules.DefendHome(ctx.DefendHomeX, ctx.DefendHomeZ, ctx.AttackSign, ctx.Stats.LineHeight);
            (float x, float z) slid = PositionRules.SlideTowardBall(home.x, home.z, ctx.BallX, ctx.BallZ, defending: true, ctx.IsGoalkeeper);
            ctx.MoveToward(slid.x, slid.z);
        }

        // GK: 우리 팀 전원 중 최근접이고 출격 가능할 때. 필드 플레이어: 시뮬이 정한 우리 팀 추격자일 때(팀별 최근접 + 히스테리시스, 09-28 F1b). 상대 팀은 보지 않는다(팀마다 1명)
        private static bool MayChaseFreeBall(IPlayerContext ctx)
        {
            if (ctx.IsGoalkeeper) { return IsNearestInTeam(ctx) && KeeperMayRush(ctx); }
            return ctx.IsLooseBallChaser;
        }

        private static bool IsNearestInTeam(IPlayerContext ctx)
        {
            List<TargetInfo> team = candidateBuffer ??= new List<TargetInfo>();
            team.Clear();
            team.Add(new TargetInfo(ctx.PlayerId, ctx.X, ctx.Z));
            for (int i = 0; i < ctx.Teammates.Count; i++) { team.Add(ctx.Teammates[i]); }
            return TargetSelector.SelectNearest(ctx.BallX, ctx.BallZ, team) == ctx.PlayerId;
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
