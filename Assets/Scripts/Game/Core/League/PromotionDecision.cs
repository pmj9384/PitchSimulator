namespace Game.Core.League
{
    // 시즌 끝 승강 판정(스펙 §10, K리그 현행 축소). 여기는 순위표에서 나오는 자리(1위·2위·3위·최하위)까지. 승강전 진행·합산·승부차기는 PlayoffRules(10-08)
    public sealed class PromotionDecision
    {
        public const int None = -1;

        public int Tier { get; }
        public int AutoPromoted { get; }      // 1위. 1부는 None
        public int PlayoffHome { get; }       // 2위(홈). 1부는 None
        public int PlayoffAway { get; }       // 3위
        public int Relegated { get; }         // 최하위. 4부(최하위 부)는 None

        public PromotionDecision(int tier, int autoPromoted, int playoffHome, int playoffAway, int relegated)
        {
            Tier = tier;
            AutoPromoted = autoPromoted;
            PlayoffHome = playoffHome;
            PlayoffAway = playoffAway;
            Relegated = relegated;
        }
    }
}
