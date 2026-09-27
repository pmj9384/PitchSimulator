using System;
using Game.Core.Data;

namespace Game.Core.League
{
    // 시즌 흐름 판정 순수 함수(09-27 확정 스펙 ③): 끝났나, 다음 부는 어디인가, 세이브로 이어 갈 수 있나.
    // 승강전(2·3위 PO)은 플랜 10-05. 그때까지 PO 대상은 잔류로 본다
    public static class SeasonProgress
    {
        public static bool IsOver(SeasonState state, TierRule tier)
        {
            return state.RoundsPlayed >= tier.Matches;
        }

        public static int NextTier(SeasonState state, TierRule tier)
        {
            if (!IsOver(state, tier)) { throw new InvalidOperationException($"[SeasonProgress] 시즌이 안 끝났다({state.RoundsPlayed}/{tier.Matches})"); }

            PromotionDecision decision = LeagueRules.Decide(state.Table(tier.Teams), tier.Tier);
            if (decision.AutoPromoted == SeasonState.MyTeamId) { return tier.Tier - 1; }
            if (decision.Relegated == SeasonState.MyTeamId) { return tier.Tier + 1; }
            return tier.Tier;
        }

        // 세이브가 없거나 생성기 버전이 다르면(상대 팀을 같게 재현 못 함) 새 시즌. 09-26 확정 스펙 #1
        public static bool NeedsNewSeason(SeasonSave? save)
        {
            return save == null || save.generatorVersion != TeamGenerator.Version;
        }
    }
}
