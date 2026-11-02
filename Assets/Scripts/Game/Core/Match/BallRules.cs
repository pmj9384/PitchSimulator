using System;
using System.Collections.Generic;

namespace Game.Core.Match
{
    // 공 이동·소유 규칙의 순수 함수. 상태 없음, 엔진 없음. MatchSimulation이 틱마다 부른다.
    // Rigidbody를 쓰지 않는 이유(스펙 §7): 고정 스텝 안에서 같은 입력이면 같은 결과여야 하고, 잡기 순서를 물리 엔진이 아니라 우리가 정해야 한다.
    public static class BallRules
    {
        // 비행 1틱: 속도 방향은 유지하고 크기만 deceleration·dt만큼 줄인다. 0 아래로는 안 내려가고, 멈추면 Free가 된다.
        // 위치는 줄인 뒤 속도로 옮긴다(반암시적 오일러). 명시적보다 정지 거리가 해석값 v²/2a에 가깝다.
        public static BallState Step(BallState ball, float deltaTime, float deceleration)
        {
            if (ball.Phase != BallPhase.Flight) { return ball; }
            if (ball.Airborne) { return StepAir(ball, deltaTime); }

            float speed = (float)Math.Sqrt(ball.VelX * ball.VelX + ball.VelZ * ball.VelZ);
            float next = speed - deceleration * deltaTime;
            if (next <= 0f)
            {
                ball.VelX = 0f;
                ball.VelZ = 0f;
                ball.Phase = BallPhase.Free;
                return ball;
            }

            float scale = next / speed;
            ball.VelX *= scale;
            ball.VelZ *= scale;
            ball.X += ball.VelX * deltaTime;
            ball.Z += ball.VelZ * deltaTime;
            return ball;
        }

        // 공중: 중력만 받는다(공기저항·양력은 무시, 10-29 ④-1 [가정]). 수평 속도는 그대로 가고, 땅에 닿으면 높이 0에서 굴림으로 이어진다.
        // 착지 때 잔디가 수평 속도를 깎는다(LobBounceSpeedRetention). 안 깎으면 25m 로빙이 12m/s로 내려앉아 60m를 굴렀다(11-02 리뷰). 세로로 튀지는 않는다 [가정]
        private static BallState StepAir(BallState ball, float deltaTime)
        {
            ball.VelY -= MatchTuning.Gravity * deltaTime;
            ball.Y += ball.VelY * deltaTime;
            ball.X += ball.VelX * deltaTime;
            ball.Z += ball.VelZ * deltaTime;
            if (ball.Y > 0f) { return ball; }

            ball.Y = 0f;
            ball.VelY = 0f;
            ball.VelX *= MatchTuning.LobBounceSpeedRetention;
            ball.VelZ *= MatchTuning.LobBounceSpeedRetention;
            return ball;
        }

        // 거리 distance에 떨어지는 로빙의 수평·세로 초속과 체공 시간. 발사각 고정, 공기저항 없음: v² = d·g / sin2θ
        public static (float horizontal, float vertical, float flightTime) LobVelocity(float distance, float angleDeg, float gravity)
        {
            float rad = angleDeg * (float)Math.PI / 180f;
            float speed = (float)Math.Sqrt(distance * gravity / Math.Sin(2f * rad));
            float vertical = speed * (float)Math.Sin(rad);
            return (speed * (float)Math.Cos(rad), vertical, 2f * vertical / gravity);
        }

        // 이 높이의 공에 닿을 수 있나(10-29 ④-1). 높이 상수 선택은 ReachHeight 한 곳
        public static bool CanReachHeight(float ballY, bool keeperInOwnBox)
        {
            return ballY <= ReachHeight(keeperInOwnBox);
        }

        // 닿는 높이: 필드 선수는 머리·발 뻗는 높이, GK는 자기 페널티 박스 안에서 손을 뻗는 높이(IFAB 12조: 손은 자기 박스 안에서만).
        // 잡기 판정과 궤적 질의(BelowHeightFrom)가 같은 값을 봐야 "닿을 수 있다고 본 자리에서 못 잡는" 어긋남이 없다(11-02 S1)
        public static float ReachHeight(bool keeperInOwnBox)
        {
            return keeperInOwnBox ? MatchTuning.KeeperReachHeight : MatchTuning.FieldReachHeight;
        }

        // ── 공 궤적 질의(11-02 S1, 통합설계 코어 1). 지금 공 상태에서 앞날을 해석식으로 읽는다. 틱을 돌리지 않고 같은 식을 쓰는 이유는
        // 판정(패스 위험)과 실행(추격·포획)이 같은 궤적을 보게 하려는 것이다. 슛·패스·로빙·튕김이 한 식을 쓴다.
        // 시뮬 Step은 반암시적 오일러라 틱 결과가 해석식과 조금 다르다(위치 오차 ≈ 중력·dt·t/2, 감속·dt·t/2, 착지 틱 한 칸). 테스트가 그 크기를 잰다

        // 지금부터 t초 뒤의 위치(x, 높이, z). 공중은 StepAir와 같은 포물선(수평 감속 없음), 착지 순간 수평 속도 × LobBounceSpeedRetention,
        // 그 뒤 땅은 Step과 같은 감속(BallDeceleration)으로 멈출 때까지. 비행이 아닌 공(Free·Owned)은 Step처럼 움직이지 않는다
        public static (float x, float y, float z) PositionAt(in BallState ball, float t)
        {
            if (ball.Phase != BallPhase.Flight) { return (ball.X, ball.Y, ball.Z); }
            if (t <= 0f) { return (ball.X, ball.Y, ball.Z); }

            if (!ball.Airborne)
            {
                (float x, float z) rolled = RollPosition(ball.X, ball.Z, ball.VelX, ball.VelZ, t);
                return (rolled.x, 0f, rolled.z);
            }

            float landing = LandingTime(ball);
            if (t < landing)
            {
                float y = ball.Y + ball.VelY * t - MatchTuning.Gravity * t * t / 2f;
                return (ball.X + ball.VelX * t, y, ball.Z + ball.VelZ * t);
            }

            float landX = ball.X + ball.VelX * landing;
            float landZ = ball.Z + ball.VelZ * landing;
            float keep = MatchTuning.LobBounceSpeedRetention;
            (float x, float z) after = RollPosition(landX, landZ, ball.VelX * keep, ball.VelZ * keep, t - landing);
            return (after.x, 0f, after.z);
        }

        // 땅 위에서 초속 (velX, velZ)로 굴러 t초 뒤 위치. Step과 같은 감속이고 멈춘 뒤엔 그 자리에 있는다
        private static (float x, float z) RollPosition(float x, float z, float velX, float velZ, float t)
        {
            float speed = (float)Math.Sqrt(velX * velX + velZ * velZ);
            if (speed <= 0f) { return (x, z); }

            float deceleration = MatchTuning.BallDeceleration;
            float rolling = Math.Min(t, speed / deceleration);
            float distance = speed * rolling - deceleration * rolling * rolling / 2f;
            return (x + velX / speed * distance, z + velZ / speed * distance);
        }

        // 지금부터 땅에 닿을 때까지 걸리는 시간. 땅볼이면 0
        public static float LandingTime(in BallState ball)
        {
            if (!ball.Airborne) { return 0f; }
            return (float)((ball.VelY + Math.Sqrt((double)ball.VelY * ball.VelY + 2.0 * MatchTuning.Gravity * ball.Y)) / MatchTuning.Gravity);
        }

        // 공이 height 아래에 있어 닿을 수 있게 되는 가장 이른 시각(t ≥ 0). 지금 이미 height 아래면(상승 중 발사 구간 포함) 0, 땅볼도 0.
        // height 위에서 내려오는 공은 Y = height가 되는 하강 시각. 상승 중이어도 height 위면 정점을 지나 내려올 때다
        public static float BelowHeightFrom(in BallState ball, float height)
        {
            if (!ball.Airborne) { return 0f; }
            if (ball.Y <= height) { return 0f; }

            double disc = (double)ball.VelY * ball.VelY + 2.0 * MatchTuning.Gravity * (ball.Y - height);
            return (float)((ball.VelY + Math.Sqrt(disc)) / MatchTuning.Gravity);
        }

        // 공이 height를 처음 넘어서는 시각. 지금 이미 height 위면 0. 끝까지 못 넘으면 착지 시각(땅볼은 0).
        // 로빙의 발사 구간(낮아서 경로 가까운 선수가 잡을 수 있는 구간)의 끝이다. v·t − g·t²/2 = height의 작은 근
        public static float FirstAboveHeight(in BallState ball, float height)
        {
            if (!ball.Airborne) { return 0f; }
            if (ball.Y > height) { return 0f; }
            if (ball.VelY <= 0f) { return LandingTime(ball); }

            double disc = (double)ball.VelY * ball.VelY - 2.0 * MatchTuning.Gravity * (height - ball.Y);
            if (disc < 0.0) { return LandingTime(ball); }
            return (float)((ball.VelY - Math.Sqrt(disc)) / MatchTuning.Gravity);
        }

        // 자유 공을 잡는 판정: 가장 가까운 선수(TargetSelector, 동률은 PlayerId 작은 쪽)가 radius 안이면 그 PlayerId, 아니면 NoOwner.
        // 경계 포함(<=): "반경 안"이니 정확히 반경에 닿아도 잡는다. 후보 목록은 호출자가 정한다.
        public static int TryCapture(in BallState ball, IReadOnlyList<TargetInfo>? candidates, float radius)
        {
            return TryCapture(ball, candidates, radius, allowFlight: false);
        }

        // allowFlight = 비행 중(패스)에도 잡는다. 슛 비행은 골라인·세이브 판정이 따로 있어 호출자가 끈다
        public static int TryCapture(in BallState ball, IReadOnlyList<TargetInfo>? candidates, float radius, bool allowFlight)
        {
            if (ball.Phase == BallPhase.Owned) { return BallState.NoOwner; }
            if (ball.Phase == BallPhase.Flight && !allowFlight) { return BallState.NoOwner; }

            int nearest = TargetSelector.SelectNearest(ball.X, ball.Z, candidates);
            if (nearest == BallState.NoOwner) { return BallState.NoOwner; }

            TargetInfo who = Find(candidates!, nearest);
            float dx = who.X - ball.X;
            float dz = who.Z - ball.Z;
            return dx * dx + dz * dz <= radius * radius ? nearest : BallState.NoOwner;
        }

        public static BallState Own(BallState ball, int ownerId, float ownerX, float ownerZ)
        {
            ball.Phase = BallPhase.Owned;
            ball.OwnerId = ownerId;
            ball.X = ownerX;
            ball.Z = ownerZ;
            ball.VelX = 0f;
            ball.VelZ = 0f;
            ball.Y = 0f;
            ball.VelY = 0f;
            return ball;
        }

        // 소유 중 공은 소유자 발에 붙어 간다. 시뮬이 소유자 위치를 넣어 준다(09-16 결정).
        // 속도도 기록한다: 소유 중 공 속도 = 소유자 이동 속도. 압박·자유 공 추격이 공의 앞을 향해 달리는 데 쓴다(비행·굴림·소유 모두 같은 필드)
        public static BallState Carry(BallState ball, float ownerX, float ownerZ)
        {
            ball.VelX = (ownerX - ball.X) / MatchTuning.FixedStep;
            ball.VelZ = (ownerZ - ball.Z) / MatchTuning.FixedStep;
            ball.X = ownerX;
            ball.Z = ownerZ;
            return ball;
        }

        // 찬다: 소유를 풀고 (dirX, dirZ) 방향으로 speed의 비행 시작. 방향 벡터는 여기서 정규화한다
        public static BallState Kick(BallState ball, float dirX, float dirZ, float speed)
        {
            float len = (float)Math.Sqrt(dirX * dirX + dirZ * dirZ);
            if (len <= 0f) { throw new ArgumentException("킥 방향이 영벡터"); }

            ball.Phase = BallPhase.Flight;
            ball.OwnerId = BallState.NoOwner;
            ball.VelX = dirX / len * speed;
            ball.VelZ = dirZ / len * speed;
            ball.Y = 0f;
            ball.VelY = 0f;
            return ball;
        }

        // 띄워 찬다(10-29 ④-1): 수평은 (dirX, dirZ) 방향으로 horizontalSpeed, 세로는 verticalSpeed로 출발한다. 착지 지점·시간은 중력으로 정해진다
        public static BallState KickLob(BallState ball, float dirX, float dirZ, float horizontalSpeed, float verticalSpeed)
        {
            ball = Kick(ball, dirX, dirZ, horizontalSpeed);
            ball.VelY = verticalSpeed;
            return ball;
        }

        private static TargetInfo Find(IReadOnlyList<TargetInfo> candidates, int playerId)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].PlayerId == playerId) { return candidates[i]; }
            }
            throw new InvalidOperationException($"후보 목록에 PlayerId {playerId}가 없다");
        }
    }
}
