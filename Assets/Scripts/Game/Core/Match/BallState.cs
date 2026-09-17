namespace Game.Core.Match
{
    public enum BallPhase { Free, Owned, Flight }

    // 공 하나의 상태. 순수 값이라 EditMode에서 엔진 없이 굴린다(스펙 §5, 09-16 확정).
    // 소유자는 참조가 아니라 PlayerId(-1 = 없음). 실체 조회는 PlayerManager.Find가 맡는다.
    // 높이(y)는 없다. 필드는 평면이고 판정은 전부 X·Z로 한다(TargetInfo와 같은 축).
    public struct BallState
    {
        public const int NoOwner = -1;

        public BallPhase Phase;
        public int OwnerId;
        public float X;
        public float Z;
        public float VelX;   // 비행 중에만 의미. Owned에선 0
        public float VelZ;

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
