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

        // 자유 공을 잡는 판정: 가장 가까운 선수(TargetSelector, 동률은 PlayerId 작은 쪽)가 radius 안이면 그 PlayerId, 아니면 NoOwner.
        // 경계 포함(<=): "반경 안"이니 정확히 반경에 닿아도 잡는다. 후보 목록(정의역)은 호출자가 정한다.
        public static int TryCapture(in BallState ball, IReadOnlyList<TargetInfo>? candidates, float radius)
        {
            if (ball.Phase != BallPhase.Free) { return BallState.NoOwner; }

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
            return ball;
        }

        // 소유 중 공은 소유자 발에 붙어 간다. MatchManager 틱이 소유자 위치를 넣어 준다(09-16 결정)
        public static BallState Carry(BallState ball, float ownerX, float ownerZ)
        {
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
