namespace Game.Core.Match
{
    public enum BallPhase { Free, Owned, Flight }

    // 공 하나의 상태. 순수 값이라 EditMode에서 엔진 없이 굴린다(스펙 §5, 09-16 확정).
    // 소유자는 참조가 아니라 PlayerId(-1 = 없음). 실체 조회는 PlayerManager.Find가 맡는다.
    // 판정은 X·Z 평면이 기본이다(TargetInfo와 같은 축). 높이 Y는 띄운 공(로빙)만 쓴다(10-29 ④-1). 땅볼·슛·소유 중엔 늘 0
    public struct BallState
    {
        public const int NoOwner = -1;

        public BallPhase Phase;
        public int OwnerId;
        public float X;
        public float Z;
        public float VelX;   // 비행·굴림 속도. Owned에선 소유자 이동 속도(09-23, Carry가 기록). 서 있으면 0
        public float VelZ;
        public float Y;      // 지면 위 높이(m). 0 = 땅
        public float VelY;   // 세로 속도(m/s). 위가 +. 땅에선 0
        public bool Airborne => Y > 0f || VelY > 0f;

        public static BallState FreeAt(float x, float z)
        {
            BallState ball = default;
            ball.Phase = BallPhase.Free;
            ball.OwnerId = NoOwner;
            ball.X = x;
            ball.Z = z;
            return ball;
        }
    }
}
