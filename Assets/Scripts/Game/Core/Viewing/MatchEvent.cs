using Game.Core.Match;

namespace Game.Core.Viewing
{
    public enum MatchEventKind
    {
        Shot,           // 슛 하나(찬 틱 + 결과)
        PossessionWon   // 패스 받기가 아닌 소유 변화(잡기·가로채기·뺏기). 공격 전개의 시작점을 찾는 재료
    }

    // 하이라이트 재료가 되는 사건 하나. 틱은 시뮬 TickCount(그 틱 안에서 이벤트가 났을 때의 값, 1부터)
    public readonly struct MatchEvent
    {
        public readonly MatchEventKind Kind;
        public readonly int Tick;          // 슛이면 찬 틱(BallKicked), 소유면 바뀐 틱
        public readonly int ResolveTick;   // 슛 결과가 난 틱. 소유 사건은 Tick과 같다
        public readonly int Team;
        public readonly int PlayerId;
        public readonly float Probability; // 슛의 골 확률 p(ShotReport). 소유 사건은 0
        public readonly ShotOutcome Outcome; // 슛만 의미가 있다

        public MatchEvent(MatchEventKind kind, int tick, int resolveTick, int team, int playerId, float probability, ShotOutcome outcome)
        {
            Kind = kind;
            Tick = tick;
            ResolveTick = resolveTick;
            Team = team;
            PlayerId = playerId;
            Probability = probability;
            Outcome = outcome;
        }

        public bool IsGoal => Kind == MatchEventKind.Shot && Outcome == ShotOutcome.Goal;

        // 골대로 간 슛(StatsBomb 유효 슛 정의): 골·캐치·튕김
        public bool IsOnTarget
        {
            get
            {
                if (Kind != MatchEventKind.Shot) { return false; }
                return Outcome == ShotOutcome.Goal || Outcome == ShotOutcome.Caught || Outcome == ShotOutcome.Parried;
            }
        }
    }
}
