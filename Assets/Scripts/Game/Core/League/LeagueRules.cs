using System;

namespace Game.Core.League
{
    // 승강 규칙 순수 함수(스펙 §10). 부는 1~4, 4가 최하위(시민구단 출발). 팀 수(6/8/10/12)는 TierRules.csv 몫이라 여기선 안 본다
    public static class LeagueRules
    {
        public const int TopTier = 1;
        public const int BottomTier = 4;

        public static PromotionDecision Decide(LeagueTable table, int tier)
        {
            if (tier < TopTier || tier > BottomTier) { throw new ArgumentOutOfRangeException(nameof(tier), $"부는 {TopTier}~{BottomTier} ({tier})"); }
            if (table.Count < 3) { throw new ArgumentException($"승강 판정엔 3팀 이상 필요 ({table.Count})", nameof(table)); }

            bool canPromote = tier > TopTier;
            bool canRelegate = tier < BottomTier;
            int autoPromoted = canPromote ? table[0].TeamId : PromotionDecision.None;
            int playoffHome = canPromote ? table[1].TeamId : PromotionDecision.None;
            int playoffAway = canPromote ? table[2].TeamId : PromotionDecision.None;
            int relegated = canRelegate ? table[table.Count - 1].TeamId : PromotionDecision.None;
            return new PromotionDecision(tier, autoPromoted, playoffHome, playoffAway, relegated);
        }

        // 2·3위 단판 PO: 2위 홈, 무승부면 2위(상위 순위 우선, K리그 준PO 규정과 같다)
        public static int PlayoffWinner(int homeTeamId, int awayTeamId, int homeGoals, int awayGoals)
        {
            return awayGoals > homeGoals ? awayTeamId : homeTeamId;
        }
    }
}
