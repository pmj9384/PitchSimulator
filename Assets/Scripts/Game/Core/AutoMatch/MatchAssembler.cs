using System;
using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Placement;
using Game.Core.Tactics;

namespace Game.Core.AutoMatch
{
    // 데이터 → 경기 조립. 러너·테스트·시즌(SeasonRunner)·내 경기(MyMatchAssembler)·새 시즌 로스터(SeasonState.NewSeason)가 같은 경로로 시뮬·라인업을 만든다.
    // 러너·테스트가 공유하고, 엔진이 없으니 EditMode·배치에서 그대로 돈다. 파일 읽기는 호출자 몫(순수 코어는 Resources를 모른다): xT 격자도 호출자가 읽어 넘긴다.
    public static class MatchAssembler
    {
        // 편성 행(StageComposition) → 라인업 2개 → 시뮬. 러너·테스트가 쓰는 입구
        public static MatchSimulation Create(IReadOnlyList<PlayerStats> table, IReadOnlyList<StageEntry> rows, TeamTactics home, TeamTactics away, int seed, ThreatGrid threat, BehaviorNode? tree = null, MatchLength? length = null)
        {
            if (rows.Count == 0) { throw new InvalidOperationException("[MatchAssembler] 편성 행이 없다"); }
            return Create(ToLineup(table, rows, 0), ToLineup(table, rows, 1), home, away, seed, threat, tree, length);
        }

        // 라인업(선수 + 자리 2쌍) → 시뮬, 시작 킥오프 팀은 시드 홀짝으로 교대(동전 던지기 대신. 주사위를 안 써 기존 난수 수열이 안 밀린다).
        // 러너·상대끼리 헤드리스 경기(SeasonRunner)는 밸런스 측정·중립 경기라 교대가 맞다. 내 경기는 MyMatchAssembler가 일정상 홈에게 준다
        public static MatchSimulation Create(IReadOnlyList<LineupSlot> home, IReadOnlyList<LineupSlot> away, TeamTactics homeTactics, TeamTactics awayTactics, int seed, ThreatGrid threat, BehaviorNode? tree = null, MatchLength? length = null)
        {
            return Create(home, away, homeTactics, awayTactics, seed, KickoffTeamBySeed(seed), threat, tree, length);
        }

        // 라인업 → 시뮬, 시작 킥오프 팀을 부르는 쪽이 정한다. 조립(선수 등록 순서·전술·추가시간·킥오프)은 이 함수 하나뿐이다
        public static MatchSimulation Create(IReadOnlyList<LineupSlot> home, IReadOnlyList<LineupSlot> away, TeamTactics homeTactics, TeamTactics awayTactics, int seed, int kickoffTeam, ThreatGrid threat, BehaviorNode? tree = null, MatchLength? length = null)
        {
            if (home.Count == 0 || away.Count == 0) { throw new InvalidOperationException("[MatchAssembler] 라인업이 비었다"); }
            if (kickoffTeam != 0 && kickoffTeam != 1) { throw new ArgumentOutOfRangeException(nameof(kickoffTeam), kickoffTeam, "[MatchAssembler] 킥오프 팀은 0 또는 1"); }

            var rng = new Random(seed);
            var sim = new MatchSimulation(() => (float)rng.NextDouble(), tree ?? PlayerTreeBuilder.Build(), threat, length) { ResetAfterEveryShot = false };
            sim.SetTactics(0, homeTactics);
            sim.SetTactics(1, awayTactics);

            int nextId = 0;   // 등록 순서 = PlayerId(타이브레이크 기준). 홈 먼저, 그다음 원정. 인게임 뷰는 이 번호로 자기 상태를 찾는다(MyMatchAssembler.SlotOf)
            for (int i = 0; i < home.Count; i++) { sim.AddPlayer(new PlayerState(nextId++, 0, home[i].Stats, home[i].AttackX, home[i].AttackZ, home[i].DefendX, home[i].DefendZ)); }
            for (int i = 0; i < away.Count; i++) { sim.AddPlayer(new PlayerState(nextId++, 1, away[i].Stats, away[i].AttackX, away[i].AttackZ, away[i].DefendX, away[i].DefendZ)); }

            sim.SetAddedTime(AddedTime.FromSeed(seed));   // 표시 추가시간(하프타임 틱). 같은 시드에서 같은 값
            sim.KickoffBy(kickoffTeam);
            return sim;
        }

        // 러너·상대끼리 경기의 시작 킥오프 팀(짝수 시드 = 팀 0). 테스트가 내 경기 규칙(일정상 홈)과 갈리는 판을 고를 때도 읽는다
        public static int KickoffTeamBySeed(int seed)
        {
            return seed % 2 == 0 ? 0 : 1;
        }

        // 편성 행 중 한 팀 몫을 라인업으로. count > 1이면 폭(Z) 방향으로 최소 간격씩 벌린다. 행 → 자리 펼치기는 여기 한 곳뿐이다
        public static List<LineupSlot> ToLineup(IReadOnlyList<PlayerStats> table, IReadOnlyList<StageEntry> rows, int team)
        {
            var slots = new List<LineupSlot>();
            for (int i = 0; i < rows.Count; i++)
            {
                StageEntry row = rows[i];
                if (row.Team != team) { continue; }
                PlayerStats? stats = PlayerTableLookup.FindVariant(table, row.Id);
                if (stats == null) { throw new InvalidOperationException($"[MatchAssembler] PlayerTable에 없는 variantId: {row.Id}"); }

                float spread = FieldBounds.MinSpacing;
                float attackStartZ = row.PosZ - (row.Count - 1) * spread * 0.5f;
                float defendStartZ = row.DefendZ - (row.Count - 1) * spread * 0.5f;
                for (int k = 0; k < row.Count; k++)
                {
                    slots.Add(new LineupSlot(stats, row.PosX, attackStartZ + k * spread, row.DefendX, defendStartZ + k * spread, PortraitRules.Of(team, slots.Count)));   // 편성 행 경기(러너·기본 로스터)의 얼굴은 자리 순
                }
            }
            return slots;
        }

    }
}
