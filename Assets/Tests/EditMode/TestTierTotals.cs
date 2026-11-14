using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;

// 테스트용 (부 → 총점) 조회. 게임에서는 SeasonFactory·SeasonSystem.Load가 TierRuleRepository로 같은 짝을 만든다.
// 테스트가 숫자(260 등)를 직접 쓰지 않고 TierRules.csv를 읽는 이유: 부 총점을 고치면 테스트 기댓값도 같이 따라가야 한다
public static class TestTierTotals
{
    private static List<TierRule> tiers;

    public static int Of(int tier)
    {
        if (tiers == null) { tiers = TierRuleParser.Parse(File.ReadAllText("Assets/Resources/Tables/TierRules.csv")); }
        for (int i = 0; i < tiers.Count; i++)
        {
            if (tiers[i].Tier == tier) { return tiers[i].TotalPoints; }
        }
        throw new ArgumentOutOfRangeException(nameof(tier), $"[TestTierTotals] TierRules.csv에 {tier}부가 없다");
    }

    // 세이브 → 상태. 총점은 세이브의 부에서 얻는다(SeasonSystem.Load와 같은 짝)
    public static SeasonState Load(SeasonSave save, IReadOnlyList<PlayerStats> table)
    {
        return SeasonState.FromSave(save, table, Of(save.tier));
    }
}
