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
        [ThreadStatic] private static IPlayerContext? valuedFor;   // 이번 틱에 운반·패스 값을 낸 선수. ⓪이 틱마다 비운다
        [ThreadStatic] private static float valuedCarry;
        [ThreadStatic] private static ValuedPass valuedPass;
        [ThreadStatic] private static float[]? ceilingBuffer;

        public static BehaviorNode Build()
        {
            return new SelectorNode(

                // ⓪ 틱마다 맨 앞에서 값 비교 메모를 비운다(Failure만 돌려 아래로 넘어간다). 메모는 ②·②c·④가 같은 틱 안에서만 잇는다
                new ConditionNode(ClearValueMemo),

                // ── 공 소유 중 ────────────────────────────────────────────
                // ① GK가 잡았으면 배급(팀 GK 배급 설정). 슛·드리블 대신 늘 패스
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall && ctx.IsGoalkeeper),
                    new ActionNode(ctx => PassUnchecked(ctx, KeeperTarget(ctx)))),

                // ①b 킥오프 키커는 첫 행동으로 옆·뒤 동료에게 내준다(09-29, IFAB 8조 + 실제 킥오프 관행). 슛·드리블보다 먼저
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall && ctx.IsKickoffTaker),
                    new ActionNode(ctx => PassUnchecked(ctx, PassRules.KickoffReceiver(ctx.X, ctx.Z, ctx.Teammates, ctx.AttackSign)))),

                // ② 슛 값(압박 보정 xG × 슛 성향 배율 − 지금 xT)이 운반·최선 패스 값보다 크면 슛(11-03 P1+ [가정 P-G17])
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall && ShouldShoot(ctx)),
                    new ActionNode(ctx => ctx.Shoot())),

                // ②c 운반(10-08 결정 7, 09-30 B 실험): 운반이 패스보다 값이 크면 골 쪽으로 몬다.
                // 전엔 슛 문턱 밖에서 받은 공격수가 늘 다시 패스해서 "ST가 정면 11m에 서 있을 때"만 득점이 났다(ST 전진 폭 20이면 0.17골, 25면 0.47).
                // 11-03 P1: 문턱(앞 4m 무상대)을 없애고 ④ 패스와 한 값 비교로 묶었다. ④도 같은 CarryValue·BestPass로 같은 후보를 고른다
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall && CarryBeatsPass(ctx)),
                    new ActionNode(ctx => ctx.RunToward(FieldBounds.HalfLength * ctx.AttackSign, 0f))),

                // ③ 역습 중 → 나보다 확실히 앞선 아군 중 가장 앞선 이에게, 안전 검사 없이(스펙 §6 "첫 패스 전방"). 앞선 아군이 없으면 ④·⑤로
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall && ctx.IsCountering && CounterTarget(ctx) != -1),
                    new ActionNode(ctx => PassUnchecked(ctx, CounterTarget(ctx)))),

                // ③b 돌파: 이번 소유에 돌파 의도가 있고 앞 8m 안에 상대가 있으면 패스 대신 제치러 간다. 방향은 상대가 먼저 못 닿는 쪽(HELIOS식).
                // 앞에 상대가 없으면(제쳤으면) 아래 평소 판단으로. 의도는 시뮬이 소유 때 드리블 성향으로 굴린다(윙어 0.9·타깃맨 0.2)
                new SequenceNode(
                    new ConditionNode(ctx => ShouldTakeOn(ctx)),
                    new ActionNode(ctx => TakeOn(ctx))),

                // ④ ②c와 같은 값 비교에서 패스가 이기면 그 패스를 찬다. 착지점은 받는 선수 앞 후보 중 안전한 가장 긴 것
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall),
                    new TryActionNode(ctx => TryPass(ctx))),

                // ⑤ 아니면 드리블. 드리블 성향이 낮으면 가장 가까운 아군 쪽으로 방향을 틀어 다음 틱 패스를 노린다
                new SequenceNode(
                    new ConditionNode(ctx => ctx.OwnsBall),
                    new ActionNode(ctx => DribbleTarget(ctx))),

                // ── 아군 소유 ────────────────────────────────────────────
                // ⑥ 나한테 오는 패스면 도착점으로 마중
                new SequenceNode(
                    new ConditionNode(ctx => ctx.IsPassTarget),
                    new ActionNode(ctx => ReceivePass(ctx))),

                // ⑦ 아군이 공을 가졌으면 공격 시 자리(+ 전진·폭 오프셋)로
                new SequenceNode(
                    new ConditionNode(ctx => ctx.BallOwnerTeam == ctx.Team),
                    new ActionNode(ctx => MoveToAttackHome(ctx))),

                // ── 상대 소유 ────────────────────────────────────────────
                // ⑧ 압박: 팀 안 압박 순위가 상한 안이면 공으로. 누가 후보인지(팀 압박 시작[상대 공 서드] × 개인 압박 거리, 역압박 배율, 라인 앞 뒷줄 전진)는
                // 시뮬이 한 곳에서 정해 순위(PressRank)로 준다(트리가 거리를 다시 재면 시뮬이 넣은 CB를 도로 걸러 판정과 실행이 갈렸다).
                // 나머지는 ⑨ 수비 자리(슬라이드가 블록을 좁힌다)(09-23 뭉침)
                new SequenceNode(
                    new ConditionNode(ctx => ctx.BallOwnerTeam == 1 - ctx.Team && ctx.PressRank < MatchTuning.MaxPressers),
                    new ActionNode(ctx => ChaseBall(ctx))),

                // ⑧b 리시버 마크(11-06 S3 ①): 공이 우리 골 40m 안이면 압박자 외 뒷줄(LineRules, 모자라면 둘째 줄 + tracking)이 상대 리시버의 골 쪽 MarkDistance에 선다. 누가 누구를 맡는지는 시뮬이
                // MarkRules로 정해 IsMarker·마크 지점으로 준다(압박 순위와 같은 구조). 지역 자리(⑨)는 마커가 아닌 선수만 간다
                new SequenceNode(
                    new ConditionNode(ctx => ctx.BallOwnerTeam == 1 - ctx.Team && ctx.IsMarker),
                    new ActionNode(ctx => ctx.ArriveToward(ctx.MarkX, ctx.MarkZ))),

                // ⑨ 아니면 수비 시 자리(+ 라인 높이)로
                new SequenceNode(
                    new ConditionNode(ctx => ctx.BallOwnerTeam == 1 - ctx.Team),
                    new ActionNode(ctx => MoveToDefendHome(ctx))),

                // ⑨a GK 슛 대기(10-25): 내 골로 유효 슛이 오는데 아직 반응 차례가 아니면 제자리. 자리 규칙으로 미리 움직이면 골 조준 간격·닿음 판정의 전제("지연 뒤에만 움직임")가 깨진다
                new SequenceNode(
                    new ConditionNode(ctx => ctx.IsGoalkeeper && ctx.WaitingForShot),
                    new ActionNode(ctx => HoldForShot(ctx))),

                // ⑨b GK 슛 대응(10-23): 내 골로 오는 유효 슛엔 자리 규칙 대신 공이 내 라인을 지나는 점으로 전력 이동(10-25 교차점). 반응 지연·골 여부는 시뮬이 IncomingShot으로 준다.
                // 슛 비행 중엔 소유 팀이 없어(OwnerTeam -1) 자유 공 분기로 떨어지므로 그 앞에 둔다
                new SequenceNode(
                    new ConditionNode(ctx => ctx.IsGoalkeeper && ctx.IncomingShot),
                    new ActionNode(ctx => MoveToShotLine(ctx))),

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
                    new ActionNode(ctx => ctx.ArriveToward(FieldBounds.HalfLength * ctx.AttackSign, 0f))),
                new SequenceNode(
                    new ConditionNode(ctx => ChasesFreeBall(ctx)),
                    new ActionNode(ctx => ctx.ArriveToward(ctx.BallX, ctx.BallZ))),
                new ActionNode(ctx => { }));
        }

        // ── 판정 어댑터. 컨텍스트 값을 순수 함수 인자로 옮기기만 한다

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
            return PassRules.RiskAllow(ctx.Tactics.PassRisk[Third(ctx)], ctx.Stats.PassRisk);
        }

        // 스루(런 제안 착지점) 허용치: 실측 맞춘 값에 팀·개인 허용치 편차를 얹는다(PassRules.ThroughAllow, 11-03 ⑤)
        private static float ThroughAllow(IPlayerContext ctx)
        {
            return PassRules.ThroughAllow(ctx.Tactics.PassRisk[Third(ctx)], ctx.Stats.PassRisk);
        }

        // 패스의 평균 속도(서드별 팀 템포, 10-25 실측 기반). 킥 초속은 거리로 역산한다(시뮬 Pass와 같은 식)
        private static float PassAverageSpeed(IPlayerContext ctx)
        {
            return MatchTuning.PassAverageSpeed[ctx.Tactics.Tempo[Third(ctx)]];
        }

        // 행동 선택(11-03 P1+): 슛·운반·패스 후보 전원을 한 값(ThreatRules)으로 비교하고 가장 큰 것을 늘 실행한다(보유 값 0 기준선 없음).
        // 운반 자격도 없다: 전 지역·전 역할이 운반 위험이 든 CarryValue로 패스와 겨룬다. 돌파·역습은 ②c 아래 노드가 따로 본다.
        // ②(ShouldShoot)·②c(CarryBeatsPass)·④(TryPass)가 같은 값(ValueCarryAndPass)을 이어받으니 셋이 다른 후보를 보는 일이 없다(R2: 판정과 실행이 같은 후보)
        // 값 메모는 한 틱의 한 선수 것이라 ⓪이 틱마다 비운다. 조건 노드 자리에 두려고 늘 false를 돌려 다음 노드로 넘긴다(판정이 아니다)
        private static bool ClearValueMemo(IPlayerContext ctx)
        {
            valuedFor = null;
            return false;
        }

        private static void ValueCarryAndPass(IPlayerContext ctx)
        {
            if (ReferenceEquals(valuedFor, ctx)) { return; }

            valuedCarry = float.NegativeInfinity;
            if (CanCarry(ctx)) { valuedCarry = CarryValue(ctx); }
            valuedPass = BestPass(ctx, valuedCarry);
            valuedFor = ctx;
        }

        // 슛 값 = xG(압박 보정) × (기본 + 이득 × (1 − 슛 성향)) − 지금 xT. 운반·패스 중 가장 큰 값보다 클 때만 쏜다 [가정 P-G17]. 준비 시간 중이면 비교 전에 거짓.
        // 슛 성향은 낮을수록 자주 쏘는 옛 의미 그대로다(포처 0.2, 수비수 0.8)
        private static bool ShouldShoot(IPlayerContext ctx)
        {
            if (!ctx.ShotReady) { return false; }   // 준비 시간 중엔 운반·패스 값 비교만 한다(11-08 S3 1단계). 시뮬에서 막으면 트리는 슛을 원하는데 선수가 멈춰 선다

            float chance = ShotChance(ctx);
            if (chance <= 0f) { return false; }

            ValueCarryAndPass(ctx);
            float shotValue = ThreatRules.ShotValue(ctx.Threat, ctx.X, ctx.Z, ctx.AttackSign, chance, ShotDial(ctx));
            float best = valuedCarry;
            if (valuedPass.Found) { best = Math.Max(best, valuedPass.Value); }
            return shotValue > best;
        }

        private static float ShotDial(IPlayerContext ctx)
        {
            return MatchTuning.ShotValueBase + MatchTuning.ShotValueBiasGain * (1f - ctx.Stats.ShotBias);
        }

        private static bool CarryBeatsPass(IPlayerContext ctx)
        {
            if (!CanCarry(ctx)) { return false; }

            ValueCarryAndPass(ctx);
            return !valuedPass.Found;
        }

        // ②c가 운반을 접었으면(패스가 더 크다) 같은 값 비교의 그 패스를 찬다. 운반 자격이 없는 선수(GK)는 후보가 있으면 늘 패스한다
        private static bool TryPass(IPlayerContext ctx)
        {
            ValueCarryAndPass(ctx);
            ValuedPass pass = valuedPass;
            if (!pass.Found) { return false; }
            PassTo(ctx, pass.Pass);
            return true;
        }

        // 패스 후보 중 값이 가장 큰 것. floor 이하면 None(Found가 거짓). 서드별 팀 값(패스 방식·리스크·폭)에 개인 패스 길이·리스크가 얹힌다.
        // 앞·옆·뒤 전원이 후보다(전엔 옆·뒤는 압박받을 때만이라 압박 없는 맨 앞줄이 줄 곳이 없었다). 오프사이드 위치 아군에겐 안 준다
        private static ValuedPass BestPass(IPlayerContext ctx, float floor)
        {
            int third = Third(ctx);
            TeamTactics t = ctx.Tactics;
            float riskAllow = RiskAllow(ctx);
            float average = PassAverageSpeed(ctx);
            float onsideLine = OnsideLine(ctx);

            IReadOnlyList<TargetInfo> mates = ctx.Teammates;
            float[] ceilings = CeilingBuffer(mates.Count);
            for (int i = 0; i < mates.Count; i++)
            {
                ceilings[i] = float.NegativeInfinity;
                if (OffsideRules.IsOffsidePosition(mates[i].X, ctx.AttackSign, onsideLine)) { continue; }
                ceilings[i] = float.PositiveInfinity;
                if (PruneByCeiling) { ceilings[i] = ValueCeiling(ctx, mates[i], OfferIndexOf(ctx, mates[i].PlayerId), t.Width[third]); }
            }

            // 상한이 큰 후보부터 본다: 최고 값이 정해지면 나머지는 상한만 보고 건너뛴다(착지·위험 계산이 비싸다). 상한 이하면 값이 같을 수도 없어 결과가 같다
            ValuedPass best = ValuedPass.None;
            float bestValue = floor;
            for (int round = 0; round < mates.Count; round++)
            {
                int next = IndexOfMax(ceilings, mates.Count);
                if (ceilings[next] <= bestValue) { break; }

                ceilings[next] = float.NegativeInfinity;
                TargetInfo m = mates[next];

                // 후보 = (리시버, 착지점). 사전 런을 하는 러너면 그 목표점(11-03 ⑤ A1), 아니면 받는 선수 앞 후보 중 안전한 가장 긴 점
                int offerIndex = OfferIndexOf(ctx, m.PlayerId);
                var kick = KickFor(ctx, m, average, riskAllow, offerIndex, t.PassStyle[third]);
                float fit = PassRules.ReceiverFit(ctx.X, ctx.Z, kick.landing.X, kick.landing.Z, t.PassStyle[third], ctx.Stats.PassLength, t.Width[third]);
                float value = ThreatRules.PassValue(ctx.Threat, ctx.X, ctx.Z, kick.landing.X, kick.landing.Z, ctx.AttackSign, kick.weight, MatchTuning.ReceiverFitWeight * fit);
                value -= ReceiverRules.ReturnPenaltyFor(ctx.X * ctx.AttackSign, kick.landing.X * ctx.AttackSign, m.PlayerId == ctx.LastPasserId);
                if (value <= bestValue) { continue; }

                best = new ValuedPass(value, new PassChoice(m.PlayerId, kick.landing.X, kick.landing.Z, kick.lob, offerIndex != NoOffer));
                bestValue = value;
            }
            return best;
        }

        private static float[] CeilingBuffer(int count)
        {
            if (ceilingBuffer == null || ceilingBuffer.Length < count) { ceilingBuffer = new float[count]; }
            return ceilingBuffer;
        }

        private static int IndexOfMax(float[] values, int count)
        {
            int best = 0;
            for (int i = 1; i < count; i++)
            {
                if (values[i] > values[best]) { best = i; }
            }
            return best;
        }

        // 가지치기 켜짐(기본). 테스트가 끄고 같은 결과가 나오는지 본다
        internal static bool PruneByCeiling = true;

        // 이 후보 패스 값의 상한(성공 확률 1, 되돌림 감점 0). 착지점은 리시버 자리에서 앞으로 PassLeadMax 사이(런 제안이면 제안 점)라 그 구간 xT 최댓값(열 중심 포함)을 쓴다.
        // 착지·위험 계산이 비싸서(후보 10명 × 롤아웃) 이 값이 지금 최고 이하인 후보는 건너뛴다. 결과는 안 바뀌고 계산만 준다
        private static float ValueCeiling(IPlayerContext ctx, TargetInfo receiver, int offerIndex, int widthLevel)
        {
            float fromX = receiver.X;
            float toX = receiver.X + MatchTuning.PassLeadMax * ctx.AttackSign;
            float landingZ = receiver.Z;
            if (offerIndex != NoOffer)
            {
                fromX = ctx.RunOffers[offerIndex].X;
                toX = fromX;
                landingZ = ctx.RunOffers[offerIndex].Z;
            }
            float threat = ctx.Threat.MaxAlongX(fromX, toX, landingZ, ctx.AttackSign);
            float fitCeiling = PassRules.ReceiverFitCeiling(landingZ, widthLevel);
            return threat + MatchTuning.ReceiverFitWeight * fitCeiling - ctx.Threat.Value(ctx.X, ctx.Z, ctx.AttackSign);
        }

        // 운반 후보 자격: 필드 선수 전원(11-03 P1+ [가정 P-G12]). GK는 ①이 먼저 배급한다
        private static bool CanCarry(IPlayerContext ctx)
        {
            return !ctx.IsGoalkeeper;
        }

        // 운반 값 = (1 − 운반 위험) × 골 쪽으로 CarryHorizon 간 점의 xT − 지금 xT
        private static float CarryValue(IPlayerContext ctx)
        {
            float goalX = FieldBounds.HalfLength * ctx.AttackSign;
            (float x, float z) end = DribbleRules.CarryEnd(ctx.X, ctx.Z, goalX, 0f, MatchTuning.CarryHorizon, FieldBounds.HalfLength, FieldBounds.HalfWidth);
            float risk = DribbleRules.CarryRisk(ctx.X, ctx.Z, end.x, end.z, ctx.Opponents, MatchTuning.CarryComfortZone, MatchTuning.PressedRadius, MatchTuning.CarryRiskScale);
            return ThreatRules.CarryValue(ctx.Threat, ctx.X, ctx.Z, end.x, end.z, ctx.AttackSign, risk);
        }

        private const int NoOffer = -1;   // OfferIndexOf: 사전 런 제안이 없다

        // 이 선수가 사전 런 제안을 받았으면 그 위치(RunOffers 번호), 아니면 NoOffer
        private static int OfferIndexOf(IPlayerContext ctx, int playerId)
        {
            IReadOnlyList<RunOffer> offers = ctx.RunOffers;
            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i].RunnerId == playerId) { return i; }
            }
            return NoOffer;
        }

        // 후보에게 찰 착지점과 그 점을 판정한 허용치. 런 제안이 있으면 제안 점(스루 허용치), 없으면 일반 착지점(팀·개인 허용치)
        private static (PassLanding landing, float weight, bool lob, float allow) KickFor(IPlayerContext ctx, TargetInfo receiver, float average, float riskAllow, int offerIndex, int passStyle)
        {
            if (offerIndex == NoOffer)
            {
                var normal = ChooseLanding(ctx, receiver, average, riskAllow, passStyle);
                return (normal.landing, normal.weight, normal.lob, riskAllow);
            }

            float throughAllow = ThroughAllow(ctx);
            var run = RunLanding(ctx, offerIndex, throughAllow);
            return (run.landing, run.weight, run.lob, throughAllow);
        }

        // 러너에게 찰 착지점 = 런 목표점 그대로(땅볼). 위험은 시뮬이 이번 틱에 다시 잰 값(러너의 현재 속도 반영)이다. 가중은 일반 후보와 같은 식
        private static (PassLanding landing, float weight, bool lob) RunLanding(IPlayerContext ctx, int offerIndex, float riskAllow)
        {
            RunOffer offer = ctx.RunOffers[offerIndex];
            var landing = new PassLanding(offer.X, offer.Z, offer.Risk);
            float weight = PassRules.SafetyWeight(offer.Risk, riskAllow, MatchTuning.PassSafetyBand);
            return (landing, weight, false);
        }

        // 한 리시버에게 찰 착지점과 안전 가중, 띄울지. 땅볼 착지점과 로빙 착지점 중 가중이 큰 쪽(동률은 땅볼).
        // 안전 판정은 실제 착지점까지(09-21 3분 계측: 리시버 위치까지만 보면 그 앞 8m에 선 수비수가 걸러져 롱패스가 늘 먹혔다).
        // 땅볼 착지점은 후보 중 안전한 가장 긴 것. 고른 점을 그대로 시뮬에 넘겨 판정과 실행이 같은 점을 본다.
        // 로빙(10-29 ④-1)은 경로가 아니라 도달 경쟁으로 잰다(PassRules.LobLanding, InterceptRules). 패스 방식이 짧게인 서드거나 로빙 범위 밖이면 땅볼만
        private static (PassLanding landing, float weight, bool lob) ChooseLanding(IPlayerContext ctx, TargetInfo receiver, float average, float riskAllow, int passStyle)
        {
            PassLanding ground = PassRules.PickLanding(ctx.X, ctx.Z, receiver.X, receiver.Z, ctx.AttackSign, average, ctx.Opponents, riskAllow);
            float groundWeight = PassRules.SafetyWeight(ground.Risk, riskAllow, MatchTuning.PassSafetyBand);
            if (passStyle < MatchTuning.LobMinPassStyle) { return (ground, groundWeight, false); }

            PassLanding lob = PassRules.LobLanding(ctx.X, ctx.Z, receiver.X, receiver.Z, ctx.AttackSign, ctx.Opponents, ctx.OpponentKeeperId);
            if (!lob.Found) { return (ground, groundWeight, false); }

            float lobWeight = PassRules.SafetyWeight(lob.Risk, riskAllow, MatchTuning.PassSafetyBand);
            if (lobWeight > groundWeight) { return (lob, lobWeight, true); }
            return (ground, groundWeight, false);
        }

        // 공을 쫓을 땐 공의 앞을 향해(추격 예측). 압박(⑧)·자유 공(⑩)·런 패스 수신(⑥)이 같이 쓴다.
        // internal: 판정/실행 계약 테스트의 대본이 같은 함수를 부른다(복제하면 실행 식이 바뀌어도 테스트가 옛 식으로 통과한다)
        internal static void ChaseBall(IPlayerContext ctx)
        {
            (float x, float z) aim = PressRules.PursuitPoint(ctx.X, ctx.Z, MatchRules.SpeedMps(ctx.Stats.Speed), ctx.BallX, ctx.BallZ, ctx.BallVelX, ctx.BallVelZ);
            ctx.RunToward(aim.x, aim.z);
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
            // 팀 설정(짧게·섞어·길게). "섞어"의 교대 스위치는 상태라 시뮬이 들고 스냅샷(IsKeeperAlternate)으로 준다(09-23 R2. 그전엔 섞어가 짧게로 고정).
            // 발 뻗는 범위 안에 상대가 붙어 있으면 짧은 배급은 릴리스 지점에서 끊긴다(GK 배급엔 안전 판정이 없다) → 길게 찬다(09-23 탐지 E)
            int level = HasOpponentWithin(ctx, MatchTuning.InterceptReach) ? 2 : ctx.Tactics.GkDistribution;
            return PassRules.KeeperDistributionTarget(ctx.X, ctx.Z, ctx.Teammates, ctx.AttackSign, level, ctx.PlayerId, ctx.IsKeeperAlternate);
        }

        private static void PassTo(IPlayerContext ctx, PassChoice choice)
        {
            if (!choice.Found)
            {
                DribbleTarget(ctx);
                return;
            }
            if (choice.IsLob)
            {
                ctx.PassLob(choice.ReceiverId, choice.X, choice.Z);
                return;
            }
            if (choice.IsRun)
            {
                ctx.PassRun(choice.ReceiverId, choice.X, choice.Z);
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
            (float x, float z) landing = PassRules.LandingPoint(ctx.X, ctx.Z, m.X, m.Z, ctx.AttackSign, PassAverageSpeed(ctx), MatchTuning.PassLeadMax);
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
                    ctx.RunToward(m.X, m.Z);
                    return;
                }
            }
            ctx.RunToward(FieldBounds.HalfLength * ctx.AttackSign, 0f);
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
                MatchTuning.InterceptRunSpeed, MatchTuning.TackleRange, MatchTuning.ReactionTime, MatchTuning.TakeOnLookahead, MatchTuning.TakeOnAngleStep,
                FieldBounds.HalfLength, FieldBounds.HalfWidth - MatchTuning.TouchlineMargin);
            ctx.RunToward(aim.x, aim.z);
        }

        // 서는 자리(홈 + 슬라이드)는 PositionRules.AttackStand 한 곳. 전술판 미니 피치가 같은 함수로 미리 보인다(10-29 ③d).
        // 사전 런 제안을 받았으면 그 점으로 먼저 달린다(RunToSpot)
        private static void MoveToAttackHome(IPlayerContext ctx)
        {
            int myOffer = OfferIndexOf(ctx, ctx.PlayerId);
            if (myOffer != NoOffer)
            {
                RunToSpot(ctx, ctx.RunOffers[myOffer]);
                return;
            }
            (float x, float z) slid = PositionRules.AttackStand(ctx.AttackHomeX, ctx.AttackHomeZ, ctx.AttackSign, ctx.Tactics, ctx.Stats, ctx.BallX, ctx.BallZ, ctx.IsGoalkeeper);
            float x = slid.x;
            if (ctx.BallOwnerTeam == ctx.Team && !ctx.OwnsBall && !ctx.IsGoalkeeper)
            {
                // 아군 소유 중엔 온사이드 선 뒤에 선다. 라인 뒤에 서 있으면 받아도 오프사이드라 애초에 안 간다
                x = OffsideRules.ClampOnside(x, ctx.AttackSign, OffsideRules.OnsideLine(ctx.Opponents, ctx.AttackSign, ctx.BallX), MatchTuning.OnsideMargin);
            }
            if (IsForwardRun(ctx, x))
            {
                ctx.ArriveToward(x, slid.z);
                return;
            }
            ctx.JogToward(x, slid.z);
        }

        // 나에게 오는 패스 받기. 런 패스(스루)면 감속 없이 공을 쫓아 달려 들어가며 받는다: 공간에 서서 기다리지 않는다(FM "runs onto").
        // 목표점이 아니라 공의 앞(추격점)을 향한다. 고정 목표점 Seek는 러너와 공의 선이 겹쳐야만 만나 측면에서 비스듬히 들어오는 W가 못 받았다(11-06).
        // 런 판정(RunRules.RunnerMeetsBall)도 같은 추격으로 굴려 판정과 실행이 같은 식을 쓴다. 일반 패스는 도착점에서 멈춰 받는다
        private static void ReceivePass(IPlayerContext ctx)
        {
            if (ctx.IsRunPass)
            {
                ChaseBall(ctx);
                return;
            }
            ctx.ArriveToward(ctx.PassTargetX, ctx.PassTargetZ);
        }

        // 사전 런(11-03 ⑤): 패스가 오기 전에 런 목표점으로 먼저 달린다. 위치는 온사이드 선 −OnsideMargin까지만 간다(킥 순간 온사이드여야 후보가 되므로).
        // 선 너머는 킥 뒤 ⑥ 리시버 마중이 이어받는다. 패스가 안 오면 선에서 멈춘다(허탕 런)
        private static void RunToSpot(IPlayerContext ctx, RunOffer offer)
        {
            float line = OffsideRules.OnsideLine(ctx.Opponents, ctx.AttackSign, ctx.BallX);
            float x = OffsideRules.ClampOnside(offer.X, ctx.AttackSign, line, MatchTuning.OnsideMargin);
            ctx.ArriveToward(x, offer.Z);
        }

        // 공격 전진 런(10-29 가속 C): 아군 소유 중 앞쪽 자리로 가는 이동은 전력, 뒤·옆으로 돌아오는 건 조깅.
        // 공격 자리까지 조깅으로 두면 윙어가 빈 자리를 못 만들어 W 소유 16 → 6%, ST 57 → 68%로 쏠렸다(스펙 8~10절)
        private static bool IsForwardRun(IPlayerContext ctx, float spotX)
        {
            if (ctx.BallOwnerTeam != ctx.Team) { return false; }
            return (spotX - ctx.X) * ctx.AttackSign > MatchTuning.ForwardRunMargin;
        }

        private static void MoveToDefendHome(IPlayerContext ctx)
        {
            (float x, float z) slid = PositionRules.DefendStand(ctx.DefendHomeX, ctx.DefendHomeZ, ctx.AttackSign, ctx.Stats, ctx.BallZ, ctx.DefendLineShift, ctx.DefendDepthCap, ctx.IsGoalkeeper);
            ctx.JogToward(slid.x, slid.z);
        }

        // GK: 슛에 반응하기 전엔 아무 의도도 내지 않는다(이동 없음). 시뮬의 대기 판정과 짝
        private static void HoldForShot(IPlayerContext ctx)
        {
        }

        // GK: 수비 자리 x(라인)를 지키며 공이 그 라인을 지나는 z(시뮬이 IncomingShotTargetZ로 준다)로. 시뮬의 닿는 범위 판정(PullAimIntoKeeperReach)도 이 점까지의 거리·시간을 본다
        private static void MoveToShotLine(IPlayerContext ctx)
        {
            float lineX = PositionRules.KeeperLineX(ctx.DefendHomeX, ctx.DefendHomeZ, ctx.AttackSign, ctx.Stats.LineHeight);
            ctx.ArriveToward(lineX, ctx.IncomingShotTargetZ);
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
            if (!MatchRules.IsInOwnPenaltyBox(ctx.BallX, ctx.BallZ, ctx.AttackSign)) { return false; }
            float dx = ctx.BallX - ctx.X;
            float dz = ctx.BallZ - ctx.Z;
            return dx * dx + dz * dz <= ctx.Stats.GkRushRadius * ctx.Stats.GkRushRadius;
        }

        // 리트머스 트리의 자유 공 조건: 필드 플레이어는 늘, GK는 내 박스 안 공만
        private static bool ChasesFreeBall(IPlayerContext ctx)
        {
            if (ctx.BallPhase != BallPhase.Free) { return false; }
            if (!ctx.IsGoalkeeper) { return true; }
            return MatchRules.IsInOwnPenaltyBox(ctx.BallX, ctx.BallZ, ctx.AttackSign);
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
