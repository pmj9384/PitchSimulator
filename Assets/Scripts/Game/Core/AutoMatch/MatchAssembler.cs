using System;
using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Placement;
using Game.Core.Tactics;

namespace Game.Core.AutoMatch
{
    // 데이터 → 경기 조립(09-23). 매니저(MatchManager.ResetMatch + StageManager.SpawnRow)와 같은 재료로 시뮬을 만든다.
    // 러너·테스트가 공유하고, 엔진이 없으니 EditMode·배치에서 그대로 돈다. 파일 읽기는 호출자 몫(순수 코어는 Resources를 모른다).
    public static class MatchAssembler
    {
        // 편성 행(StageComposition) → 라인업 2개 → 시뮬. 러너·테스트가 쓰는 입구
        public static MatchSimulation Create(IReadOnlyList<PlayerStats> table, IReadOnlyList<StageEntry> rows, TeamTactics home, TeamTactics away, int seed, BehaviorNode? tree = null)
        {
            if (rows.Count == 0) { throw new InvalidOperationException("[MatchAssembler] 편성 행이 없다"); }
            return Create(ToLineup(table, rows, 0), ToLineup(table, rows, 1), home, away, seed, tree);
        }

        // 라인업(선수 + 자리 2쌍) → 시뮬. 생성 팀(부 총점으로 축소된 빌드)·내 로스터가 이 입구를 쓴다(09-26). 조립 경로는 이것 하나다
        public static MatchSimulation Create(IReadOnlyList<LineupSlot> home, IReadOnlyList<LineupSlot> away, TeamTactics homeTactics, TeamTactics awayTactics, int seed, BehaviorNode? tree = null)
        {
            if (home.Count == 0 || away.Count == 0) { throw new InvalidOperationException("[MatchAssembler] 라인업이 비었다"); }

            var rng = new Random(seed);
            var sim = new MatchSimulation(() => (float)rng.NextDouble(), tree ?? PlayerTreeBuilder.Build()) { ResetAfterEveryShot = false };
            sim.SetTactics(0, homeTactics);
            sim.SetTactics(1, awayTactics);

            int nextId = 0;   // 스폰 순서 = PlayerId(PlayerManager와 같은 규칙. 타이브레이크의 근원). 홈 먼저, 그다음 원정
            for (int i = 0; i < home.Count; i++) { sim.AddPlayer(new PlayerState(nextId++, 0, home[i].Stats, home[i].AttackX, home[i].AttackZ, home[i].DefendX, home[i].DefendZ)); }
            for (int i = 0; i < away.Count; i++) { sim.AddPlayer(new PlayerState(nextId++, 1, away[i].Stats, away[i].AttackX, away[i].AttackZ, away[i].DefendX, away[i].DefendZ)); }

            // 시작 킥오프 팀은 시드 홀짝으로 교대(동전 던지기 대신. 주사위를 안 써 기존 난수 수열이 안 밀린다).
            // 러너는 밸런스 측정이라 교대가 맞고, 인게임 한 판은 매니저가 홈에게 준다
            sim.KickoffBy(seed % 2 == 0 ? 0 : 1);
            return sim;
        }

        // 편성 행 중 한 팀 몫을 라인업으로. count > 1이면 폭(Z) 방향으로 최소 간격씩 벌린다(StageManager.SpawnRow와 같은 식)
        public static List<LineupSlot> ToLineup(IReadOnlyList<PlayerStats> table, IReadOnlyList<StageEntry> rows, int team)
        {
            var slots = new List<LineupSlot>();
            for (int i = 0; i < rows.Count; i++)
            {
                StageEntry row = rows[i];
                if (row.Team != team) { continue; }
                PlayerStats? stats = FindVariant(table, row.Id);
                if (stats == null) { throw new InvalidOperationException($"[MatchAssembler] PlayerTable에 없는 variantId: {row.Id}"); }

                float spread = FieldBounds.MinSpacing;
                float attackStartZ = row.PosZ - (row.Count - 1) * spread * 0.5f;
                float defendStartZ = row.DefendZ - (row.Count - 1) * spread * 0.5f;
                for (int k = 0; k < row.Count; k++)
                {
                    slots.Add(new LineupSlot(stats, row.PosX, attackStartZ + k * spread, row.DefendX, defendStartZ + k * spread));
                }
            }
            return slots;
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
