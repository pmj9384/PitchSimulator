using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace Game.Core.AutoMatch
{
    // 러너 결과 → CSV 텍스트. 다른 파서와 같은 CsvHelper + ClassMap 관례(자작 문자열 조립 금지). 줄바꿈 \n 고정
    public static class AutoMatchCsv
    {
        public static string Serialize(IEnumerable<MatchSummary> rows)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture) { NewLine = "\n" };
            using var writer = new StringWriter();
            using var csv = new CsvWriter(writer, config);
            csv.Context.RegisterClassMap<MatchSummaryMap>();
            csv.WriteRecords(rows);
            return writer.ToString();
        }

        private sealed class MatchSummaryMap : ClassMap<MatchSummary>
        {
            public MatchSummaryMap()
            {
                Map(m => m.Seed).Name("seed");
                Map(m => m.HomePreset).Name("homePreset");
                Map(m => m.AwayPreset).Name("awayPreset");
                Map(m => m.HomeGoals).Name("homeGoals");
                Map(m => m.AwayGoals).Name("awayGoals");
                Map(m => m.Winner).Name("winner");
                Map(m => m.HomeShots).Name("homeShots");
                Map(m => m.AwayShots).Name("awayShots");
                Map(m => m.Passes).Name("passes");
                Map(m => m.Intercepts).Name("intercepts");
                Map(m => m.TackleAttempts).Name("tackleAttempts");
                Map(m => m.TackleSuccesses).Name("tackleSuccesses");
                Map(m => m.Turnovers).Name("turnovers");
                Map(m => m.HomeOwnedTicks).Name("homeOwnedTicks");
                Map(m => m.AwayOwnedTicks).Name("awayOwnedTicks");
                Map(m => m.HomeOppThirdTicks).Name("homeOppThirdTicks");
                Map(m => m.AwayOppThirdTicks).Name("awayOppThirdTicks");
                Map(m => m.WideOwnedTicks).Name("wideOwnedTicks");
                Map(m => m.TopRole).Name("topRole");
                Map(m => m.TopRoleShare).Name("topRoleShare");
                Map(m => m.MeanShotAbsZ).Name("meanShotAbsZ");
            }
        }
    }
}
