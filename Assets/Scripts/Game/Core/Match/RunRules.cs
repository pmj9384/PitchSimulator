using System;
using System.Collections.Generic;
using Game.Core.Placement;

namespace Game.Core.Match
{
    // 사전 런 판정(11-03 ⑤, 설계초안-침투-스루 3절 C). 패스가 오기 전에 러너가 목표점으로 먼저 달린다.
    // 판정과 실행이 갈리지 않도록 두 가지를 지킨다:
    //  - 시간 창이 없다. 시작 조건은 "지금은 그 패스가 위험하지만 내가 달리고 있다면 가능한가"를 패서와 같은 판정(LandingRisk + 도달 경쟁)으로 묻는다
    //  - 러너의 목표점이 곧 패서의 착지점이다(같은 점을 패서가 스냅샷으로 받아 평가한다)
    // 상태 없음, 엔진 없음, 난수 없음
    public static class RunRules
    {
        public const int StyleNone = 0;     // 침투 성향 없음: 지금까지와 같은 자리 규칙
        public const int StyleBehind = 1;   // 뒤공간 침투
        public const int StyleShort = 2;    // 내려와 받기
        public const int StyleLate = 3;     // 박스에 늦게 도착. ⑤에선 값만 두고 동작은 ⑦ 컷백 단계에서 켠다

        // 러너가 이 패스를 받을 수 있는가(실행과 같은 이동으로 굴린다). 두 조건을 같이 본다:
        //  1) 목표점에 deadline(공 도착 시각 − 여유) 전에 든다. 러너가 먼저 자리를 잡고 있어야 한다
        //  2) 실제로 공과 만난다: 어느 틱에서든 러너~공 거리가 잡기 반경 이내(공은 목표점 너머까지 굴러간다)
        // 판정 롤아웃의 이동 모드 = 런 패스 수신 실행 모드(Seek, 감속 없음)다. 그래서 2)가 필요하다:
        // 목표점에 일찍 닿은 러너가 멈추지 않고 지나가 버리면 공이 도착할 때 러너가 거기 없다(설계 A1 3절)
        public static bool RunnerMeetsBall(RunCandidate runner, float velX, float velZ, float targetX, float targetZ, in BallState ball, float deadline)
        {
            if (runner.MaxSpeed <= 0f) { return false; }

            float accel = MovementRules.AccelerationFor(runner.MaxSpeed, MatchTuning.ReactionTime);
            float speed = runner.TopSpeed;
            float x = runner.X;
            float z = runner.Z;
            (float vx, float vz) = MovementRules.ClampSpeed(velX, velZ, runner.MaxSpeed);
            bool arrived = deadline >= 0f && Distance(x, z, targetX, targetZ) <= MatchTuning.CaptureRadius;
            int steps = (int)Math.Floor(BallRules.StopTime(ball, BallRules.LandingTime(ball)) / MatchTuning.FixedStep);
            int arriveLimit = (int)Math.Floor(deadline / MatchTuning.FixedStep);
            for (int i = 1; i <= steps; i++)
            {
                var next = MovementRules.Step(x, z, vx, vz, targetX, targetZ, speed, accel, MatchTuning.FixedStep, arrive: false);
                x = next.x;
                z = next.z;
                vx = next.vx;
                vz = next.vz;
                if (!arrived && i <= arriveLimit) { arrived = Distance(x, z, targetX, targetZ) <= MatchTuning.CaptureRadius; }
                if (!arrived && i >= arriveLimit) { return false; }

                var at = BallRules.PositionAt(ball, i * MatchTuning.FixedStep);
                if (arrived && Distance(x, z, at.x, at.z) <= MatchTuning.CaptureRadius) { return true; }
            }
            return false;
        }

        // 그 점으로 패스가 올 때의 위험 둘: 러너의 지금 상태 그대로(now)와 이미 최고 속도로 달리는 상태(run).
        // 위험 = 상대 가로채기 위험(PassRules.LandingRisk, 땅볼 판정 그대로)과 러너 도달 불가(1)의 큰 쪽.
        // 도달 가능 = RunnerMeetsBall. 실행의 이동(MovementRules.Step)과 수신 모드를 그대로 굴려서 판정과 실행이 같은 식을 쓴다.
        // 상대 위험이 허용치를 못 넘으면 도달은 안 잰다(비용)
        public static (float now, float run) SpotRisks(float carrierX, float carrierZ, float spotX, float spotZ, float averageSpeed, float allow,
            IReadOnlyList<TargetInfo> opponents, RunCandidate runner)
        {
            float opponentRisk = PassRules.LandingRisk(carrierX, carrierZ, spotX, spotZ, averageSpeed, opponents);
            if (opponentRisk > allow) { return (opponentRisk, opponentRisk); }

            float distance = Distance(carrierX, carrierZ, spotX, spotZ);
            if (distance <= 0f) { return (1f, 1f); }

            float kickSpeed = PassRules.KickSpeed(distance, averageSpeed, MatchTuning.BallDeceleration, MatchTuning.PassSpeedMax);
            BallState ball = BallRules.Kick(BallState.FreeAt(carrierX, carrierZ), spotX - carrierX, spotZ - carrierZ, kickSpeed);
            float deadline = BallRules.TimeToDistance(kickSpeed, distance) - MatchTuning.RunArrivalMargin;

            bool now = RunnerMeetsBall(runner, runner.VelX, runner.VelZ, spotX, spotZ, ball, deadline);
            float toSpot = Distance(runner.X, runner.Z, spotX, spotZ);
            float runSpeed = runner.TopSpeed;
            float runVelX = toSpot > 0f ? (spotX - runner.X) / toSpot * runSpeed : 0f;
            float runVelZ = toSpot > 0f ? (spotZ - runner.Z) / toSpot * runSpeed : 0f;
            bool run = RunnerMeetsBall(runner, runVelX, runVelZ, spotX, spotZ, ball, deadline);
            return (now ? opponentRisk : 1f, run ? opponentRisk : 1f);
        }

        private static float Distance(float fromX, float fromZ, float toX, float toZ)
        {
            float dx = toX - fromX;
            float dz = toZ - fromZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        // 이미 있는 제안을 다시 잰다. 허용치 안이면 유지(지금 위험·달릴 때 위험 둘 다 갱신), 아니면 None.
        // 유지 조건은 "달리고 있다면 가능"(run)이다. 지금 위험(now)이 허용치를 넘어도 러너가 속도를 내는 중일 수 있어서다
        public static RunOffer Reevaluate(RunOffer offer, float carrierX, float carrierZ, float averageSpeed, float allow, IReadOnlyList<TargetInfo> opponents, RunCandidate runner)
        {
            var risks = SpotRisks(carrierX, carrierZ, offer.X, offer.Z, averageSpeed, allow, opponents, runner);
            if (risks.run > allow) { return RunOffer.None; }
            return new RunOffer(offer.RunnerId, offer.X, offer.Z, risks.now, risks.run);
        }

        // 새 제안 하나 찾기: 후보 러너마다 목표점을 고르고, 시작 조건(지금은 위험, 달리면 가능)을 만족하는 것 중
        // 패서가 가장 높게 값 매길 목표를 가진 러너를 낸다. 값은 패서가 후보를 고를 때(PlayerTreeBuilder.BestPass)와 같은 함수다: 성공 확률 = PassRules.SafetyWeight(달릴 때 위험 RunRisk), 값 = ThreatRules.PassValue (11-03 P1 [가정 P-G8], 11-06 #17).
        // 되돌림 감점만 빠진다(FindOffer는 직전 패서를 모른다). 동률은 PlayerId 작은 쪽.
        // 가장 앞선 목표로 고르지 않는 이유: 수비 라인에 늘 가까운 스트라이커가 이겨 윙어가 달리지 않는다
        public static RunOffer FindOffer(float carrierX, float carrierZ, int attackSign, float averageSpeed, float allow, bool carrierPressed,
            IReadOnlyList<TargetInfo> opponents, IReadOnlyList<RunCandidate> candidates, IReadOnlyList<RunOffer> existing, int carrierId,
            ThreatGrid grid, int passStyle, float passLength, int widthLevel)
        {
            RunOffer best = RunOffer.None;
            float bestValue = float.MinValue;
            float defenderLine = OffsideRules.LastDefenderLine(opponents, attackSign);
            float onsideLine = OffsideRules.OnsideLine(opponents, attackSign, carrierX);
            for (int i = 0; i < candidates.Count; i++)
            {
                RunCandidate runner = candidates[i];
                if (runner.PlayerId == carrierId) { continue; }
                if (HasOffer(existing, runner.PlayerId)) { continue; }
                if (OffsideRules.IsOffsidePosition(runner.X, attackSign, onsideLine)) { continue; }

                RunOffer offer = FindSpot(carrierX, carrierZ, attackSign, averageSpeed, allow, carrierPressed, opponents, runner, defenderLine);
                if (offer.RunnerId == RunOffer.NoRunner) { continue; }
                float fit = PassRules.ReceiverFit(carrierX, carrierZ, offer.X, offer.Z, passStyle, passLength, widthLevel);
                float success = PassRules.SafetyWeight(offer.RunRisk, allow, MatchTuning.PassSafetyBand);   // 달릴 때의 위험으로 비교한다(Risk는 생성 순간 늘 1, 11-06 #17)
                float value = ThreatRules.PassValue(grid, carrierX, carrierZ, offer.X, offer.Z, attackSign, success, MatchTuning.ReceiverFitWeight * fit);
                if (value < bestValue) { continue; }
                if (value == bestValue && offer.RunnerId > best.RunnerId) { continue; }
                best = offer;
                bestValue = value;
            }
            return best;
        }

        private static bool HasOffer(IReadOnlyList<RunOffer> offers, int runnerId)
        {
            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i].RunnerId == runnerId) { return true; }
            }
            return false;
        }

        // 한 러너의 목표점. 뒤공간은 방향·거리 후보를 긴 것부터 보고(PickLanding과 같은 "안전한 가장 긴 것"), 내려와 받기는 패서 앞 한 점
        private static RunOffer FindSpot(float carrierX, float carrierZ, int attackSign, float averageSpeed, float allow, bool carrierPressed,
            IReadOnlyList<TargetInfo> opponents, RunCandidate runner, float defenderLine)
        {
            if (runner.Style == StyleBehind) { return FindBehindSpot(carrierX, carrierZ, attackSign, averageSpeed, allow, opponents, runner, defenderLine); }
            if (runner.Style == StyleShort) { return FindShortSpot(carrierX, carrierZ, attackSign, averageSpeed, allow, carrierPressed, opponents, runner); }
            return RunOffer.None;
        }

        private static RunOffer FindBehindSpot(float carrierX, float carrierZ, int attackSign, float averageSpeed, float allow,
            IReadOnlyList<TargetInfo> opponents, RunCandidate runner, float defenderLine)
        {
            if (defenderLine == OffsideRules.NoLine) { return RunOffer.None; }
            float needForward = defenderLine - MatchTuning.RunBehindMargin;
            for (int d = 0; d < MatchTuning.RunDistances.Length; d++)
            {
                for (int a = 0; a < MatchTuning.RunDirectionCount; a++)
                {
                    (float x, float z) spot = SpotAt(runner.X, runner.Z, attackSign, MatchTuning.RunDistances[d], DirectionAngle(a));
                    if (spot.x * attackSign < needForward) { continue; }
                    var risks = SpotRisks(carrierX, carrierZ, spot.x, spot.z, averageSpeed, allow, opponents, runner);
                    if (risks.run > allow) { continue; }
                    if (risks.now <= allow) { return RunOffer.None; }   // 달리지 않아도 이미 가장 긴 안전한 패스가 된다. 런이 필요 없다
                    return new RunOffer(runner.PlayerId, spot.x, spot.z, risks.now, risks.run);
                }
            }
            return RunOffer.None;
        }

        // 내려와 받기: 압박받는 운반자 앞 RunSupportDistance, 자기 레인(z)은 유지(11-03 ⑤. 값 근거는 MatchTuning)
        private static RunOffer FindShortSpot(float carrierX, float carrierZ, int attackSign, float averageSpeed, float allow, bool carrierPressed,
            IReadOnlyList<TargetInfo> opponents, RunCandidate runner)
        {
            if (!carrierPressed) { return RunOffer.None; }
            float x = carrierX + MatchTuning.RunSupportDistance * attackSign;
            return TryStart(carrierX, carrierZ, x, runner.Z, averageSpeed, allow, opponents, runner);
        }

        // 시작 조건: 지금은 허용치를 넘고(패스가 아직 안 된다) 달리면 허용치 안(달리면 된다)
        private static RunOffer TryStart(float carrierX, float carrierZ, float spotX, float spotZ, float averageSpeed, float allow,
            IReadOnlyList<TargetInfo> opponents, RunCandidate runner)
        {
            var risks = SpotRisks(carrierX, carrierZ, spotX, spotZ, averageSpeed, allow, opponents, runner);
            if (risks.run > allow) { return RunOffer.None; }
            if (risks.now <= allow) { return RunOffer.None; }
            return new RunOffer(runner.PlayerId, spotX, spotZ, risks.now, risks.run);
        }

        // 방향 후보 a번째: 0은 정면, 이후 +, −를 번갈아 바깥으로 간다(1·3·5개에서 같은 앞부분을 공유)
        public static float DirectionAngle(int index)
        {
            if (index == 0) { return 0f; }
            int ring = (index + 1) / 2;
            int rings = (MatchTuning.RunDirectionCount - 1) / 2;
            float step = MatchTuning.RunAngleDeg / rings;
            float angle = ring * step;
            return index % 2 == 1 ? angle : -angle;
        }

        // 러너 앞 distance m, 공격 방향에서 angle도 틀어진 점. 필드 안(가장자리 여유)으로 자른다
        public static (float x, float z) SpotAt(float runnerX, float runnerZ, int attackSign, float distance, float angleDeg)
        {
            float rad = angleDeg * (float)Math.PI / 180f;
            float x = runnerX + distance * (float)Math.Cos(rad) * attackSign;
            float z = runnerZ + distance * (float)Math.Sin(rad);
            float xLimit = FieldBounds.HalfLength - FieldBounds.EdgeMargin;
            float zLimit = FieldBounds.HalfWidth - MatchTuning.TouchlineMargin;
            return (Math.Clamp(x, -xLimit, xLimit), Math.Clamp(z, -zLimit, zLimit));
        }
    }
}
