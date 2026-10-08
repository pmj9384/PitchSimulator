using System;
using System.Collections.Generic;
using Game.Core.Match;

namespace Game.Core.League
{
    // 승강전 단계. 내 팀이 걸린 경기만 실제로 치른다(스펙 §10 10-08 구현 설계): 다른 팀끼리의 승강은 다음 시즌 부가 새로 생성되므로 결과가 필요 없다
    public enum PlayoffStage
    {
        None = 0,        // 승강전 없음(정규 시즌 순위가 대상 밖)
        Semifinal = 1,   // 2·3위 단판 PO(2위 홈, 무승부면 2위)
        LegOne = 2,      // 승강전 1차전
        LegTwo = 3,      // 승강전 2차전(합산, 동점이면 승부차기)
        Done = 4,        // 끝남(탈락 포함)
    }

    public enum PlayoffRole
    {
        None,
        Challenger,   // 하위 부 2·3위: PO를 이기면 상위 부 팀과 승강전. 1차전 홈, 2차전 원정
        Defender,     // 상위 부 최하위 바로 위: 하위 부 PO 승자와 승강전. 1차전 원정, 2차전 홈
    }

    // 정규 시즌이 끝난 뒤 내 팀의 승강전 계획. 순위표에서만 나온다
    public readonly struct PlayoffPlan
    {
        public readonly PlayoffRole Role;
        public readonly int SemifinalOpponentId;   // Challenger일 때 같은 부의 상대(2위면 3위, 3위면 2위). 아니면 -1
        public readonly bool SemifinalAtHome;      // 2위가 홈
        public readonly int OtherTier;             // 승강전 상대가 있는 부. None이면 0

        public PlayoffPlan(PlayoffRole role, int semifinalOpponentId, bool semifinalAtHome, int otherTier)
        {
            Role = role;
            SemifinalOpponentId = semifinalOpponentId;
            SemifinalAtHome = semifinalAtHome;
            OtherTier = otherTier;
        }

        public static readonly PlayoffPlan None = new PlayoffPlan(PlayoffRole.None, -1, false, 0);
    }

    // 승강전 한 경기의 결과. 승부차기 점수는 2차전에서 합산 동점일 때만 0이 아니다
    public readonly struct PlayoffResult
    {
        public readonly PlayoffStage Stage;
        public readonly int HomeTeamId;
        public readonly int AwayTeamId;
        public readonly int HomeGoals;
        public readonly int AwayGoals;
        public readonly int HomePenalties;
        public readonly int AwayPenalties;

        public PlayoffResult(PlayoffStage stage, int homeTeamId, int awayTeamId, int homeGoals, int awayGoals, int homePenalties = 0, int awayPenalties = 0)
        {
            Stage = stage;
            HomeTeamId = homeTeamId;
            AwayTeamId = awayTeamId;
            HomeGoals = homeGoals;
            AwayGoals = awayGoals;
            HomePenalties = homePenalties;
            AwayPenalties = awayPenalties;
        }
    }

    public readonly struct ShootoutResult
    {
        public readonly int HomeScore;
        public readonly int AwayScore;

        public ShootoutResult(int homeScore, int awayScore)
        {
            HomeScore = homeScore;
            AwayScore = awayScore;
        }

        public bool HomeWon => HomeScore > AwayScore;
    }

    // 승강전 규칙 순수 함수(스펙 §10: K리그 현행 축소). 상태·엔진 없음. 누가 뛰나·누가 이겼나·승부차기만 답한다
    public static class PlayoffRules
    {
        public const int ForeignTeamIdBase = 1000;   // 다른 부 팀 id = 1000 + 그 부 생성 id. 승점표 id(0~11)와 안 겹친다
        public const int ShootoutRounds = 5;
        public const int ShootoutKickers = 5;

        // [가정] 승부차기 성공 확률 = 0.76 + (슈터 shot − GK handling) × 0.002, 0.5~0.95. 실제 성공률 약 75~78%가 기준, 스탯 차 50이면 ±0.1
        public const float PenaltyBaseChance = 0.76f;
        public const float PenaltyStatScale = 0.002f;
        public const float PenaltyMinChance = 0.5f;
        public const float PenaltyMaxChance = 0.95f;

        // 순위표 → 내 승강전 계획. 1부 2·3위는 올라갈 곳이 없고, 4부 최하위 바로 위는 내려올 팀이 없다
        public static PlayoffPlan Plan(LeagueTable table, int tier, int myTeamId)
        {
            int rank = table.RankOf(myTeamId);
            if (rank < 0) { throw new ArgumentException($"[PlayoffRules] 순위표에 팀 {myTeamId}이 없다", nameof(table)); }

            bool canPromote = tier > LeagueRules.TopTier;
            if (canPromote && (rank == 1 || rank == 2))
            {
                int opponent = table[rank == 1 ? 2 : 1].TeamId;
                return new PlayoffPlan(PlayoffRole.Challenger, opponent, rank == 1, tier - 1);
            }

            bool canRelegate = tier < LeagueRules.BottomTier;
            if (canRelegate && rank == table.Count - 2)
            {
                return new PlayoffPlan(PlayoffRole.Defender, -1, false, tier + 1);
            }
            return PlayoffPlan.None;
        }

        // 지금까지의 결과로 다음 단계. Challenger: 단판 → (이기면) 1차전 → 2차전 → 끝. Defender: 1차전 → 2차전 → 끝
        public static PlayoffStage NextStage(PlayoffPlan plan, IReadOnlyList<PlayoffResult> results, int myTeamId)
        {
            if (plan.Role == PlayoffRole.None) { return PlayoffStage.None; }

            if (plan.Role == PlayoffRole.Challenger)
            {
                if (results.Count == 0) { return PlayoffStage.Semifinal; }
                PlayoffResult semi = results[0];
                if (SemifinalWinner(semi) != myTeamId) { return PlayoffStage.Done; }
                if (results.Count == 1) { return PlayoffStage.LegOne; }
                if (results.Count == 2) { return PlayoffStage.LegTwo; }
                return PlayoffStage.Done;
            }

            if (results.Count == 0) { return PlayoffStage.LegOne; }
            if (results.Count == 1) { return PlayoffStage.LegTwo; }
            return PlayoffStage.Done;
        }

        // 내가 승강전을 이겼나. 도전자는 단판을 지면 거기서 끝. NextStage와 같은 결과 해석을 쓴다(진행과 승자가 한 곳)
        public static bool IsWinner(PlayoffPlan plan, IReadOnlyList<PlayoffResult> results, int myTeamId)
        {
            if (plan.Role == PlayoffRole.None) { return false; }
            if (NextStage(plan, results, myTeamId) != PlayoffStage.Done) { return false; }

            int legStart = plan.Role == PlayoffRole.Challenger ? 1 : 0;
            if (results.Count < legStart + 2) { return false; }   // 도전자가 단판에서 떨어진 경우
            return TieWinner(results[legStart], results[legStart + 1]) == myTeamId;
        }

        // 한 팀의 2경기 합산 골. 1차전 홈이 2차전 원정이다
        public static int AggregateGoals(PlayoffResult legOne, PlayoffResult legTwo, int teamId)
        {
            int inOne = legOne.HomeTeamId == teamId ? legOne.HomeGoals : legOne.AwayGoals;
            int inTwo = legTwo.HomeTeamId == teamId ? legTwo.HomeGoals : legTwo.AwayGoals;
            return inOne + inTwo;
        }

        // 단판 PO: 무승부면 홈(2위)
        public static int SemifinalWinner(PlayoffResult semi)
        {
            return LeagueRules.PlayoffWinner(semi.HomeTeamId, semi.AwayTeamId, semi.HomeGoals, semi.AwayGoals);
        }

        // 2경기 합산. 1차전 홈이 2차전 원정이다. 동점이면 -1(부르는 쪽이 승부차기로 정한다)
        public static int AggregateWinner(PlayoffResult legOne, PlayoffResult legTwo)
        {
            if (legOne.HomeTeamId != legTwo.AwayTeamId || legOne.AwayTeamId != legTwo.HomeTeamId)
            {
                throw new ArgumentException($"[PlayoffRules] 2차전은 1차전의 홈·원정을 바꾼 경기여야 한다 ({legOne.HomeTeamId}-{legOne.AwayTeamId} / {legTwo.HomeTeamId}-{legTwo.AwayTeamId})");
            }
            int a = legOne.HomeGoals + legTwo.AwayGoals;   // 1차전 홈 팀
            int b = legOne.AwayGoals + legTwo.HomeGoals;   // 1차전 원정 팀
            if (a > b) { return legOne.HomeTeamId; }
            if (b > a) { return legOne.AwayTeamId; }
            return -1;
        }

        // 승강전 최종 승자. 2차전에 승부차기 점수가 있으면 그것으로 가른다
        public static int TieWinner(PlayoffResult legOne, PlayoffResult legTwo)
        {
            int byGoals = AggregateWinner(legOne, legTwo);
            if (byGoals != -1) { return byGoals; }
            if (legTwo.HomePenalties == legTwo.AwayPenalties) { throw new InvalidOperationException("[PlayoffRules] 합산 동점인데 승부차기 점수가 없거나 같다"); }
            return legTwo.HomePenalties > legTwo.AwayPenalties ? legTwo.HomeTeamId : legTwo.AwayTeamId;
        }

        public static float PenaltyChance(int shooterShot, int keeperHandling)
        {
            float p = PenaltyBaseChance + (shooterShot - keeperHandling) * PenaltyStatScale;
            return Math.Max(PenaltyMinChance, Math.Min(PenaltyMaxChance, p));
        }

        // 승부차기: 5명씩 번갈아(홈 먼저), 5회 뒤 동점이면 같은 순서로 서든데스. 주사위 하나로 결정적이다.
        // 조기 종료(남은 킥으로 못 뒤집으면 끝)는 안 넣는다: 결과가 같고 테스트가 쉽다
        public static ShootoutResult Shootout(IReadOnlyList<int> homeShots, int homeKeeperHandling, IReadOnlyList<int> awayShots, int awayKeeperHandling, Func<float> roll)
        {
            if (homeShots.Count == 0 || awayShots.Count == 0) { throw new ArgumentException("[PlayoffRules] 승부차기 키커가 없다"); }

            int home = 0;
            int away = 0;
            int round = 0;
            while (true)
            {
                if (roll() < PenaltyChance(homeShots[round % homeShots.Count], awayKeeperHandling)) { home++; }
                if (roll() < PenaltyChance(awayShots[round % awayShots.Count], homeKeeperHandling)) { away++; }
                round++;
                if (IsDecided(round, home, away)) { return new ShootoutResult(home, away); }
                if (round > 100) { throw new InvalidOperationException("[PlayoffRules] 승부차기가 100회를 넘었다"); }
            }
        }

        // 5회를 다 찼고 점수가 다르면 끝
        private static bool IsDecided(int round, int home, int away)
        {
            if (round < ShootoutRounds) { return false; }
            return home != away;
        }

        // 키커 = 필드 선수 중 shot 높은 순 5명. 동률은 라인업 순서. GK는 뺀다
        public static List<int> TopShooters(IReadOnlyList<LineupSlot> lineup, int count)
        {
            var order = new List<int>();
            for (int i = 0; i < lineup.Count; i++)
            {
                if (lineup[i].Stats.IsKeeper) { continue; }
                order.Add(i);
            }
            order.Sort((x, y) => CompareByShotThenIndex(lineup, x, y));
            var shots = new List<int>(count);
            for (int i = 0; i < order.Count && i < count; i++) { shots.Add(lineup[order[i]].Stats.Shot); }
            if (shots.Count == 0) { throw new InvalidOperationException("[PlayoffRules] 라인업에 필드 선수가 없다"); }
            return shots;
        }

        // shot 내림차순, 같으면 라인업 순서(앞이 먼저)
        private static int CompareByShotThenIndex(IReadOnlyList<LineupSlot> lineup, int x, int y)
        {
            int byShot = lineup[y].Stats.Shot.CompareTo(lineup[x].Stats.Shot);
            if (byShot != 0) { return byShot; }
            return x.CompareTo(y);
        }

        public static int KeeperHandling(IReadOnlyList<LineupSlot> lineup)
        {
            for (int i = 0; i < lineup.Count; i++)
            {
                if (lineup[i].Stats.IsKeeper) { return lineup[i].Stats.Handling; }
            }
            return 50;
        }
    }
}
