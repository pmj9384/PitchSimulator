using System;
using Game.Core.Data;

namespace Game.Core.League
{
    public enum SeasonOutcomeKind { Promoted, Stayed, Relegated, Champion }

    // 시즌 끝 내 팀의 판정(09-28 시즌 종료 표시): 무엇이 됐나 · 최종 순위(1부터) · 다음 시즌 부. 결과 화면 문구와 다음 시즌 부가 같은 값을 본다
    public readonly struct SeasonOutcome
    {
        public readonly SeasonOutcomeKind Kind;
        public readonly int FinalRank;
        public readonly int NextTier;

        public SeasonOutcome(SeasonOutcomeKind kind, int finalRank, int nextTier)
        {
            Kind = kind;
            FinalRank = finalRank;
            NextTier = nextTier;
        }
    }

    // 시즌 흐름 판정 순수 함수(09-27 확정 스펙 ③): 끝났나, 다음 부는 어디인가, 세이브로 이어 갈 수 있나.
    // 승강전(2·3위 PO)은 플랜 10-05. 그때까지 PO 대상은 잔류로 본다
    public static class SeasonProgress
    {
        public static bool IsOver(SeasonState state, TierRule tier)
        {
            return state.RoundsPlayed >= tier.Matches;
        }

        // 승강 판정(LeagueRules.Decide) 위에 내 순위를 얹는다. 2·3위(승강전 대상)는 승강전(10-05)이 생기기 전까지 잔류다.
        // 1부 1위는 올라갈 곳이 없어 우승으로 따로 부른다(부는 그대로)
        public static SeasonOutcome Outcome(SeasonState state, TierRule tier)
        {
            if (!IsOver(state, tier)) { throw new InvalidOperationException($"[SeasonProgress] 시즌이 안 끝났다({state.RoundsPlayed}/{tier.Matches})"); }

            LeagueTable table = state.Table(tier.Teams);
            PromotionDecision decision = LeagueRules.Decide(table, tier.Tier);
            int rank = table.RankOf(SeasonState.MyTeamId) + 1;
            if (decision.AutoPromoted == SeasonState.MyTeamId) { return new SeasonOutcome(SeasonOutcomeKind.Promoted, rank, tier.Tier - 1); }
            if (decision.Relegated == SeasonState.MyTeamId) { return new SeasonOutcome(SeasonOutcomeKind.Relegated, rank, tier.Tier + 1); }
            if (tier.Tier == LeagueRules.TopTier && rank == 1) { return new SeasonOutcome(SeasonOutcomeKind.Champion, rank, tier.Tier); }
            return new SeasonOutcome(SeasonOutcomeKind.Stayed, rank, tier.Tier);
        }

        // 다음 시즌 부. 판정은 Outcome 한 곳(결과 화면 문구와 실제 다음 부가 갈리지 않게, 09-28)
        public static int NextTier(SeasonState state, TierRule tier)
        {
            return Outcome(state, tier).NextTier;
        }

        // 세이브가 없거나 생성기 버전이 다르면(상대 팀을 같게 재현 못 함) 새 시즌. 09-26 확정 스펙 #1
        public static bool NeedsNewSeason(SeasonSave? save)
        {
            return save == null || save.generatorVersion != TeamGenerator.Version;
        }
    }
}
