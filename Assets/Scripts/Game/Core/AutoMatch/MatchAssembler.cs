using System;
using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Placement;
using Game.Core.Tactics;

namespace Game.Core.AutoMatch
{
    // 데이터 → 경기 조립(09-23). 매니저(MatchManager.ResetMatch + StageManager.SpawnRow)와 같은 재료로 시뮬을 만든다.
    // 러너·테스트가 공유하고, 엔진이 없으니 EditMode·배치에서 그대로 돈다. 파일 읽기는 호출자 몫(순수 코어는 Resources를 모른다).
    public static class MatchAssembler
    {
        public static MatchSimulation Create(IReadOnlyList<PlayerStats> table, IReadOnlyList<StageEntry> rows, TeamTactics home, TeamTactics away, int seed, BehaviorNode? tree = null)
        {
            if (rows.Count == 0) { throw new InvalidOperationException("[MatchAssembler] 편성 행이 없다"); }

            var rng = new Random(seed);
            var sim = new MatchSimulation(() => (float)rng.NextDouble(), tree ?? PlayerTreeBuilder.Build()) { ResetAfterEveryShot = false };
            sim.SetTactics(0, home);
            sim.SetTactics(1, away);

            int nextId = 0;   // 스폰 순서 = PlayerId(PlayerManager와 같은 규칙. 타이브레이크의 근원)
            for (int i = 0; i < rows.Count; i++)
            {
                StageEntry row = rows[i];
                PlayerStats? stats = FindVariant(table, row.Id);
                if (stats == null) { throw new InvalidOperationException($"[MatchAssembler] PlayerTable에 없는 variantId: {row.Id}"); }

                // count > 1이면 폭(Z) 방향으로 최소 간격씩 벌린다(StageManager.SpawnRow와 같은 식)
                float spread = FieldBounds.MinSpacing;
                float attackStartZ = row.PosZ - (row.Count - 1) * spread * 0.5f;
                float defendStartZ = row.DefendZ - (row.Count - 1) * spread * 0.5f;
                for (int k = 0; k < row.Count; k++)
                {
                    sim.AddPlayer(new PlayerState(nextId++, row.Team, stats, row.PosX, attackStartZ + k * spread, row.DefendX, defendStartZ + k * spread));
                }
            }

            sim.Kickoff();
            return sim;
        }

        private static PlayerStats? FindVariant(IReadOnlyList<PlayerStats> table, string variantId)
        {
            for (int i = 0; i < table.Count; i++)
            {
                if (string.Equals(table[i].VariantId, variantId, StringComparison.OrdinalIgnoreCase)) { return table[i]; }
            }
            return null;
        }
    }
}
