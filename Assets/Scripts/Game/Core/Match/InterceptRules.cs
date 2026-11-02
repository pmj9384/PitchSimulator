using System;

namespace Game.Core.Match
{
    // 도달 경쟁(11-02 S2, 통합설계 코어 2). "누가 공에 먼저 닿나"를 선수 한 명의 가장 이른 포획 시각 T로 재고, 두 T의 차를 확률로 바꾼다.
    // 판정(패스 위험)과 실행(포획)이 같은 반경(MatchTuning.CaptureRadius)과 같은 가속 등가 반응(MatchTuning.ReactionTime)을 쓰게 한다.
    // 판정만 반경 1.2m·반응 0이면 판정이 실행보다 낙관적이어서 "안전"이라 본 패스가 실제로 끊겼다(11-02 재검증 A~D).
    // 상태 없음, 엔진 없음, 난수 없음.
    public static class InterceptRules
    {
        // 지금 공 상태에서 공이 움직일 때, 이 선수가 공을 잡을 수 있는 가장 이른 시각과 그 점.
        // t를 0부터 FixedStep 간격으로 늘리며 그 시각 공 위치가 (닿는 높이 이하) 이면서 (선수가 거기 도착해 있는) 첫 t를 고른다.
        // 도착 시각 = 공이 포획 반경 안이면 0, 아니면 reaction + (거리 − 반경) ÷ 속도. reaction은 정지 출발 가속 손실의 등가값이라 리시버도 똑같이 잃는다.
        // GK는 그 점이 자기 박스 안일 때만 손이 닿는 높이(KeeperReachHeight)를 쓴다. 박스 밖이면 필드 선수와 같다.
        // 반환의 arrival은 선수가 그 점에 도착하는 시각이다. 잡는 시각(time)이 같은 틱인 두 선수를 가를 때 쓴다(Contest).
        // 공이 멈출 때까지 아무도 못 닿으면 정지점에서 max(정지 시각, 도착 시각)로 끝낸다. 땅 감속이 있어 정지 시각이 유한하니 상한 시간을 따로 두지 않는다.
        public static (float time, float x, float z, float arrival) EarliestCatch(in BallState ball, float playerX, float playerZ, float runSpeed, float reaction,
            float captureRadius, bool isKeeper, int keeperAttackSign)
        {
            return EarliestCatchBefore(ball, playerX, playerZ, runSpeed, reaction, captureRadius, isKeeper, keeperAttackSign, float.PositiveInfinity);
        }

        // EarliestCatch의 상대용. 호출자가 이미 아는 최선 상대 시각 bestOpponentTime이 있으면 그보다 한 틱 넘게 늦는 상대는 볼 필요가 없다.
        // 그런 상대는 IsBetterOpponent가 시각이 같은 틱도 아니고 늦다는 이유로 어차피 탈락시켜서(도착 시각을 보지도 않는다) 위험값이 한 비트도 안 바뀐다.
        // 그래서 t가 bestOpponentTime + FixedStep을 넘는 순간 스캔을 멈추고 time을 무한대로 돌려준다(한 틱 여유는 부동소수 오차로 반 틱 경계를 잘못 넘지 않게).
        // 리시버 시각 + 3σ 컷은 위험이 0.995 근처에서 조금 달라지니 같은 결과가 아니라서 쓰지 않는다
        public static (float time, float x, float z, float arrival) EarliestCatchBefore(in BallState ball, float playerX, float playerZ, float runSpeed, float reaction,
            float captureRadius, bool isKeeper, int keeperAttackSign, float bestOpponentTime)
        {
            float limit = bestOpponentTime + MatchTuning.FixedStep;
            float landing = BallRules.LandingTime(ball);   // t와 무관하니 스캔 밖에서 한 번만 잰다
            float stop = StopTime(ball, landing);
            int steps = (int)Math.Floor(stop / MatchTuning.FixedStep);
            for (int i = 0; i <= steps; i++)
            {
                float t = i * MatchTuning.FixedStep;
                if (t > limit) { return NoCatch; }

                (float x, float y, float z) at = BallRules.PositionAt(ball, t, landing);
                bool keeperHands = isKeeper && MatchRules.IsInOwnPenaltyBox(at.x, at.z, keeperAttackSign);
                if (at.y > BallRules.ReachHeight(keeperHands)) { continue; }

                float arrival = ArrivalTime(Distance(playerX, playerZ, at.x, at.z), runSpeed, reaction, captureRadius);
                if (arrival <= t) { return (t, at.x, at.z, arrival); }
            }

            (float x, float y, float z) rest = BallRules.PositionAt(ball, stop, landing);
            float restArrival = ArrivalTime(Distance(playerX, playerZ, rest.x, rest.z), runSpeed, reaction, captureRadius);
            float restTime = Math.Max(stop, restArrival);
            if (restTime > limit) { return NoCatch; }
            return (restTime, rest.x, rest.z, restArrival);
        }

        private static readonly (float time, float x, float z, float arrival) NoCatch =
            (float.PositiveInfinity, 0f, 0f, float.PositiveInfinity);

        // 두 잡는 시각이 같은 틱인가(반 틱 안). 같은 틱에 잡을 수 있으면 시각이 아니라 도착 시각으로 가른다.
        // Contest와 최선 상대 고르기(IsBetterOpponent)가 같은 기준을 쓰게 여기 한 곳에 둔다
        public static bool IsSameTick(float timeA, float timeB)
        {
            return Math.Abs(timeA - timeB) < MatchTuning.FixedStep * 0.5f;
        }

        // 가장 위험한 상대 고르기: 잡는 시각이 빠른 쪽, 같은 틱이면 먼저 도착한 쪽(Contest가 같은 틱을 도착 시각으로 가르므로)
        public static bool IsBetterOpponent(float time, float arrival, float bestTime, float bestArrival)
        {
            if (IsSameTick(time, bestTime)) { return arrival < bestArrival; }
            return time < bestTime;
        }

        // 상대가 먼저 잡을 확률. 로지스틱 변환의 π/√3은 sigma를 로지스틱 분포의 표준편차로 읽는 값이다(Spearman 2017 pitch control의 tti_sigma).
        // 동시 = 0.5. 상대가 없으면(무한대) 0
        public static float Risk(float receiverTime, float bestOpponentTime, float sigma)
        {
            if (float.IsPositiveInfinity(bestOpponentTime)) { return 0f; }

            double x = (receiverTime - bestOpponentTime) / sigma * Math.PI / Math.Sqrt(3.0);
            return (float)(1.0 / (1.0 + Math.Exp(-x)));
        }

        // 리시버 대 상대 한 명의 경쟁 위험(11-02 S2 동률 처리). 잡는 시각 차가 반 틱 안이면 같은 틱에 잡을 수 있는 것이라
        // 시각 차 대신 그 점에 도착한 시각 차를 같은 로지스틱에 넣는다. 첫 판정이 시각 차만 보면 공이 2.2m 아래로 내려오는 틱에
        // 근처 선수가 전부 동률(0.5)이 되어 누가 가까운지가 위험에 안 보였다(보정 probe: 위험 0.5 구간 81%).
        // 실행 포획(TryCapture)은 같은 틱이면 공에 가장 가까운 선수가 잡고, 먼저 와서 자리 잡은 쪽이 더 가깝다(자리 선점)
        public static float Contest(float receiverTime, float receiverArrival, float opponentTime, float opponentArrival, float sigma)
        {
            if (float.IsPositiveInfinity(opponentTime)) { return 0f; }
            if (IsSameTick(receiverTime, opponentTime)) { return Risk(receiverArrival, opponentArrival, sigma); }
            return Risk(receiverTime, opponentTime, sigma);
        }

        // 공이 멈추는 시각. 공중은 착지 시각 + 착지 뒤 수평 속도(× LobBounceSpeedRetention)가 땅 감속으로 줄어드는 시간,
        // 땅은 속도 ÷ 감속. 비행이 아닌 공(Free·Owned)은 움직이지 않으니 0
        private static float StopTime(in BallState ball, float landing)
        {
            if (ball.Phase != BallPhase.Flight) { return 0f; }

            float speed = (float)Math.Sqrt(ball.VelX * ball.VelX + ball.VelZ * ball.VelZ);
            if (!ball.Airborne) { return speed / MatchTuning.BallDeceleration; }

            float rollSpeed = speed * MatchTuning.LobBounceSpeedRetention;
            return landing + rollSpeed / MatchTuning.BallDeceleration;
        }

        private static float ArrivalTime(float distance, float runSpeed, float reaction, float captureRadius)
        {
            if (distance <= captureRadius) { return 0f; }
            if (runSpeed <= 0f) { return float.PositiveInfinity; }
            return reaction + (distance - captureRadius) / runSpeed;
        }

        private static float Distance(float fromX, float fromZ, float toX, float toZ)
        {
            float dx = toX - fromX;
            float dz = toZ - fromZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
