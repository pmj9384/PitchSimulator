using System;
using System.Collections.Generic;
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
    // 승강전(10-08): 정규 시즌이 끝난 뒤 내가 걸린 PO·승강전이 남아 있으면 시즌은 아직 안 끝난 것이다
    public static class SeasonProgress
    {
        public static bool IsRegularSeasonOver(SeasonState state, TierRule tier)
        {
            return state.RoundsPlayed >= tier.Matches;
        }

        // 내 승강전 계획. 정규 시즌이 끝나기 전엔 없다
        public static PlayoffPlan Plan(SeasonState state, TierRule tier)
        {
            if (!IsRegularSeasonOver(state, tier)) { return PlayoffPlan.None; }
            return PlayoffRules.Plan(state.Table(tier.Teams), tier.Tier, SeasonState.MyTeamId);
        }

        // 다음에 치를 승강전 단계. None = 대상 아님, Done = 다 치름
        public static PlayoffStage PendingStage(SeasonState state, TierRule tier)
        {
            return PlayoffRules.NextStage(Plan(state, tier), state.PlayoffResults, SeasonState.MyTeamId);
        }

        public static bool IsOver(SeasonState state, TierRule tier)
        {
            if (!IsRegularSeasonOver(state, tier)) { return false; }
            PlayoffStage pending = PendingStage(state, tier);
            return pending == PlayoffStage.None || pending == PlayoffStage.Done;
        }

        // 승강 판정(LeagueRules.Decide) 위에 내 순위와 승강전 결과를 얹는다.
        // 1부 1위는 올라갈 곳이 없어 우승으로 따로 부른다(부는 그대로)
        public static SeasonOutcome Outcome(SeasonState state, TierRule tier)
        {
            if (!IsOver(state, tier)) { throw new InvalidOperationException($"[SeasonProgress] 시즌이 안 끝났다({state.RoundsPlayed}/{tier.Matches}, 승강전 {PendingStage(state, tier)})"); }

            LeagueTable table = state.Table(tier.Teams);
            PromotionDecision decision = LeagueRules.Decide(table, tier.Tier);
            int rank = table.RankOf(SeasonState.MyTeamId) + 1;
            if (decision.AutoPromoted == SeasonState.MyTeamId) { return new SeasonOutcome(SeasonOutcomeKind.Promoted, rank, tier.Tier - 1); }
            if (decision.Relegated == SeasonState.MyTeamId) { return new SeasonOutcome(SeasonOutcomeKind.Relegated, rank, tier.Tier + 1); }
            if (tier.Tier == LeagueRules.TopTier && rank == 1) { return new SeasonOutcome(SeasonOutcomeKind.Champion, rank, tier.Tier); }

            PlayoffPlan plan = PlayoffRules.Plan(table, tier.Tier, SeasonState.MyTeamId);
            if (plan.Role != PlayoffRole.None && WonPlayoff(plan, state.PlayoffResults))
            {
                // 도전자가 이기면 승격, 수성 팀이 이기면 잔류
                if (plan.Role == PlayoffRole.Challenger) { return new SeasonOutcome(SeasonOutcomeKind.Promoted, rank, tier.Tier - 1); }
                return new SeasonOutcome(SeasonOutcomeKind.Stayed, rank, tier.Tier);
            }
            if (plan.Role == PlayoffRole.Defender) { return new SeasonOutcome(SeasonOutcomeKind.Relegated, rank, tier.Tier + 1); }
            return new SeasonOutcome(SeasonOutcomeKind.Stayed, rank, tier.Tier);
        }

        // 승강전을 내가 이겼나. 도전자는 단판 PO를 지면 거기서 끝이다
        public static bool WonPlayoff(PlayoffPlan plan, IReadOnlyList<PlayoffResult> results)
        {
            if (plan.Role == PlayoffRole.None) { return false; }
            int legStart = plan.Role == PlayoffRole.Challenger ? 1 : 0;
            if (plan.Role == PlayoffRole.Challenger)
            {
                if (results.Count == 0) { return false; }
                if (PlayoffRules.SemifinalWinner(results[0]) != SeasonState.MyTeamId) { return false; }
            }
            if (results.Count < legStart + 2) { return false; }
            return PlayoffRules.TieWinner(results[legStart], results[legStart + 1]) == SeasonState.MyTeamId;
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
