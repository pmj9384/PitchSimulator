namespace Game.Core.Match
{
    // 사전 런 제안 하나(11-03 ⑤): 러너가 달려가는 목표점과 그 점으로 패스가 올 때의 위험.
    // 시뮬이 팀마다 한 곳에서 정해 스냅샷으로 준다(PressRank·IsLooseBallChaser와 같은 방식). 러너는 이 점으로 달리고,
    // 패서는 같은 점을 스루 후보로 평가해서 런 목표점과 착지점이 같은 점이 된다(설계 A1 3절)
    public readonly struct RunOffer
    {
        public const int NoRunner = -1;
        public static readonly RunOffer None = new RunOffer(NoRunner, 0f, 0f, 1f);

        public readonly int RunnerId;
        public readonly float X;
        public readonly float Z;
        public readonly float Risk;   // 지금 상태(러너의 현재 속도)로 패스가 올 때의 위험. 러너가 붙을수록 내려간다

        public RunOffer(int runnerId, float x, float z, float risk)
        {
            RunnerId = runnerId;
            X = x;
            Z = z;
            Risk = risk;
        }
    }

    // 사전 런 후보 선수 한 명의 판정 입력(시뮬이 PlayerState에서 뽑는다)
    public readonly struct RunCandidate
    {
        public readonly int PlayerId;
        public readonly float X;
        public readonly float Z;
        public readonly float VelX;
        public readonly float VelZ;
        public readonly float MaxSpeed;
        public readonly int Style;
        public readonly float Effort;   // 피로로 깎인 최고 속도 비율(FatigueRules.Effort). 실행의 속도 상한에 곱해진다

        public float TopSpeed => MaxSpeed * Effort;   // 피로를 반영한 실제 최고 속도

        public RunCandidate(int playerId, float x, float z, float velX, float velZ, float maxSpeed, int style, float effort = 1f)
        {
            PlayerId = playerId;
            X = x;
            Z = z;
            VelX = velX;
            VelZ = velZ;
            MaxSpeed = maxSpeed;
            Style = style;
            Effort = effort;
        }
    }
}
