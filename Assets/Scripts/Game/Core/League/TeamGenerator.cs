using System;
using System.Collections.Generic;
using System.Text;
using Game.Core.Data;

namespace Game.Core.League
{
    // 상대 팀 하나. 편성은 StageComposition 행(side enemy, stage = 팀 id)과 같은 형식이라 러너·매니저가 그대로 읽는다(스펙 §10).
    // 선수 능력치는 부 총점(260~300)으로 축소한 복사본. PlayerTable 원본은 300 고정이라 여기서만 줄인다
    public sealed class GeneratedTeam
    {
        public int TeamId { get; }
        public string Name { get; }
        public string FormationId { get; }
        public string PresetId { get; }
        public IReadOnlyList<StageEntry> Rows { get; }
        public IReadOnlyList<PlayerStats> Players { get; }   // Rows와 같은 순서. 축소된 빌드

        public GeneratedTeam(int teamId, string name, string formationId, string presetId, List<StageEntry> rows, List<PlayerStats> players)
        {
            TeamId = teamId;
            Name = name;
            FormationId = formationId;
            PresetId = presetId;
            Rows = rows;
            Players = players;
        }

        // 같은 부 안 중복 판정용. 포메이션·프리셋·변형 11개가 전부 같으면 같은 팀으로 본다
        public string Signature
        {
            get
            {
                var sb = new StringBuilder(FormationId).Append('|').Append(PresetId);
                for (int i = 0; i < Rows.Count; i++) { sb.Append('|').Append(Rows[i].Id); }
                return sb.ToString();
            }
        }

        public IReadOnlyList<LineupSlot> ToLineup()
        {
            var slots = new List<LineupSlot>(Rows.Count);
            for (int i = 0; i < Rows.Count; i++)
            {
                StageEntry r = Rows[i];
                slots.Add(new LineupSlot(Players[i], r.PosX, r.PosZ, r.DefendX, r.DefendZ));
            }
            return slots;
        }
    }

    // 경기 조립 단위: 선수 1명 + 자리 2쌍. 편성 행(StageEntry)과 생성 팀·내 로스터가 모두 이걸로 떨어져 MatchAssembler가 하나의 경로만 가진다
    public readonly struct LineupSlot
    {
        public readonly PlayerStats Stats;
        public readonly float AttackX;
        public readonly float AttackZ;
        public readonly float DefendX;
        public readonly float DefendZ;

        public LineupSlot(PlayerStats stats, float attackX, float attackZ, float defendX, float defendZ)
        {
            Stats = stats;
            AttackX = attackX;
            AttackZ = attackZ;
            DefendX = defendX;
            DefendZ = defendZ;
        }
    }

    // 시드·부 → 상대 팀들(스펙 §10 "상대 팀은 손으로 안 만든다"). 손 데이터 = 부별 규칙·포메이션 템플릿·이름 표.
    // 결정적: 같은 (시즌 시드, 부, 팀 번호)면 같은 팀. 세이브는 시드만 들고 매번 다시 만든다(09-26 확정 스펙 #1).
    // 생성 규칙이 바뀌면 Version을 올린다. 세이브의 버전과 다르면 시즌을 새로 시작한다(1차 출시 허용)
    public static class TeamGenerator
    {
        public const int Version = 1;
        public const int MaxRetries = 100;

        public static List<GeneratedTeam> Generate(int seasonSeed, TierRule tier, IReadOnlyList<PlayerStats> table,
            IReadOnlyList<FormationTemplate> formations, TeamNameTable names)
        {
            int opponents = tier.Teams - 1;   // 팀 수는 나를 포함한다(스펙 §10 "팀(나 포함)")
            // 구성 유일성은 변주가 가능한 부에서만(4부는 규칙 표가 4-4-2·balanced·기본 변형 하나라 전 팀이 같은 구성인 게 스펙 "프리셋 그대로"). 이름은 늘 유일
            bool canVary = tier.DialVariance > 0 || tier.Formations.Count > 1 || tier.Presets.Count > 1;
            var teams = new List<GeneratedTeam>(opponents);
            var signatures = new HashSet<string>(StringComparer.Ordinal);
            var usedNames = new HashSet<string>(StringComparer.Ordinal);

            for (int index = 1; index <= opponents; index++)
            {
                GeneratedTeam? team = null;
                for (int attempt = 0; attempt < MaxRetries; attempt++)
                {
                    GeneratedTeam candidate = GenerateOne(seasonSeed, tier, index, attempt, table, formations, names);
                    if ((canVary && signatures.Contains(candidate.Signature)) || usedNames.Contains(candidate.Name)) { continue; }
                    team = candidate;
                    break;
                }
                if (team == null) { throw new InvalidOperationException($"[TeamGenerator] {tier.Tier}부 {index}번 팀을 {MaxRetries}번 안에 유일하게 못 만들었다(규칙 표가 너무 좁다)"); }

                signatures.Add(team.Signature);
                usedNames.Add(team.Name);
                teams.Add(team);
            }
            return teams;
        }

        // 팀 하나. 난수는 (시즌 시드, 부, 팀 번호, 재시도)에서만 나온다. 다른 팀의 결과가 이 팀에 영향을 주지 않는다
        private static GeneratedTeam GenerateOne(int seasonSeed, TierRule tier, int index, int attempt, IReadOnlyList<PlayerStats> table,
            IReadOnlyList<FormationTemplate> formations, TeamNameTable names)
        {
            var rng = new Random(unchecked(seasonSeed * 1000003 + tier.Tier * 7919 + index * 104729 + attempt * 31));

            string formationId = tier.Formations[rng.Next(tier.Formations.Count)];
            FormationTemplate formation = FindFormation(formations, formationId);
            string presetId = tier.Presets[rng.Next(tier.Presets.Count)];
            string name = names.Compose(rng.Next(names.Cities.Count), rng.Next(names.Suffixes.Count));

            // 다이얼 변주 = 변형 선택(변형이 곧 다이얼 묶음). 0 기본만 / 1 슬롯 1~2개만 다른 변형 / 2 전 슬롯 무작위
            int varied = tier.DialVariance == 0 ? 0 : (tier.DialVariance == 1 ? 1 + rng.Next(2) : FormationTemplate.SlotCount);
            var variedSlots = new HashSet<int>();
            while (variedSlots.Count < varied) { variedSlots.Add(rng.Next(FormationTemplate.SlotCount)); }

            var rows = new List<StageEntry>(FormationTemplate.SlotCount);
            var players = new List<PlayerStats>(FormationTemplate.SlotCount);
            for (int s = 0; s < formation.Slots.Count; s++)
            {
                FormationSlot slot = formation.Slots[s];
                PlayerStats variant = variedSlots.Contains(s) ? RandomVariant(table, slot.RoleId, rng) : DefaultVariant(table, slot.RoleId);
                float defendX = tier.DualPositions ? slot.PosX2 : slot.PosX;
                float defendZ = tier.DualPositions ? slot.PosZ2 : slot.PosZ;
                // 템플릿은 내 진영(-X) 기준. 상대는 X를 뒤집는다(StageComposition의 enemy 행과 같은 규칙)
                rows.Add(new StageEntry
                {
                    Stage = index, Side = StageEntry.SideEnemy, Kind = StageEntry.KindPlayer, Id = variant.VariantId, Count = 1,
                    PosX = -slot.PosX, PosZ = slot.PosZ, PosX2 = -defendX, PosZ2 = defendZ,
                });
                players.Add(BuildScaler.Scale(variant, tier.TotalPoints));
            }
            return new GeneratedTeam(index, name, formationId, presetId, rows, players);
        }

        // 기본 변형 = PlayerTable에서 그 역할의 첫 행(gk_standard·cb_centreback·fb_fullback·dm_anchor·cm_box2box·am_advanced_pm·w_winger·st_poacher)
        public static PlayerStats DefaultVariant(IReadOnlyList<PlayerStats> table, string roleId)
        {
            for (int i = 0; i < table.Count; i++)
            {
                if (table[i].RoleId == roleId) { return table[i]; }
            }
            throw new InvalidOperationException($"[TeamGenerator] PlayerTable에 역할 {roleId}이 없다");
        }

        private static PlayerStats RandomVariant(IReadOnlyList<PlayerStats> table, string roleId, Random rng)
        {
            int count = 0;
            for (int i = 0; i < table.Count; i++) { if (table[i].RoleId == roleId) { count++; } }
            if (count == 0) { throw new InvalidOperationException($"[TeamGenerator] PlayerTable에 역할 {roleId}이 없다"); }
            int pick = rng.Next(count);
            for (int i = 0; i < table.Count; i++)
            {
                if (table[i].RoleId != roleId) { continue; }
                if (pick == 0) { return table[i]; }
                pick--;
            }
            throw new InvalidOperationException("unreachable");
        }

        private static FormationTemplate FindFormation(IReadOnlyList<FormationTemplate> formations, string id)
        {
            for (int i = 0; i < formations.Count; i++)
            {
                if (formations[i].Id == id) { return formations[i]; }
            }
            throw new InvalidOperationException($"[TeamGenerator] FormationTemplates에 없는 포메이션: {id}");
        }
    }

    // 빌드 9개 합계를 부 총점으로 축소한 복사본(스펙 §4-1 "리그 로드 때 비율로 축소"). 반올림 오차는 가장 큰 스탯에 몰아 합계를 정확히 맞춘다.
    // 다이얼·역할 문자열은 그대로 복사. 1부 "재분배"(rebuild)는 10-06 칸(3~1부 규칙)에서
    public static class BuildScaler
    {
        public static PlayerStats Scale(PlayerStats src, int totalPoints)
        {
            if (totalPoints == PlayerStats.TotalPoints) { return src; }

            float ratio = (float)totalPoints / PlayerStats.TotalPoints;
            PlayerStats p = Copy(src);
            p.Speed = Round(src.Speed * ratio); p.Stamina = Round(src.Stamina * ratio); p.Pass = Round(src.Pass * ratio);
            p.Shot = Round(src.Shot * ratio); p.Tackle = Round(src.Tackle * ratio); p.Positioning = Round(src.Positioning * ratio);
            p.Reflexes = Round(src.Reflexes * ratio); p.Handling = Round(src.Handling * ratio); p.Diving = Round(src.Diving * ratio);

            int diff = totalPoints - p.BuildTotal;
            if (diff != 0) { AddToLargest(p, diff); }
            return p;
        }

        // 전 필드 복사(PlayerTable 행은 공유 객체라 빌드를 바꾸기 전에 복사한다)
        public static PlayerStats Copy(PlayerStats src)
        {
            return new PlayerStats
            {
                RoleId = src.RoleId, VariantId = src.VariantId, Exposed = src.Exposed, Focus = src.Focus,
                Speed = src.Speed, Stamina = src.Stamina, Pass = src.Pass, Shot = src.Shot, Tackle = src.Tackle, Positioning = src.Positioning,
                Reflexes = src.Reflexes, Handling = src.Handling, Diving = src.Diving,
                PushUp = src.PushUp, PressRange = src.PressRange, ShotBias = src.ShotBias, PassLength = src.PassLength, Width = src.Width,
                LineHeight = src.LineHeight, RoamRadius = src.RoamRadius, PassRisk = src.PassRisk, Dribble = src.Dribble, HoldUp = src.HoldUp,
                GkRushRadius = src.GkRushRadius, DisplayName = src.DisplayName, Description = src.Description, Icon = src.Icon,
            };
        }

        private static int Round(float v)
        {
            return (int)Math.Round(v, MidpointRounding.AwayFromZero);
        }

        // 가장 큰 스탯에 오차를 더한다(음수도). 큰 스탯이라 몇 점 차이가 비율로 가장 작다
        private static void AddToLargest(PlayerStats p, int diff)
        {
            int best = p.Speed; int which = 0;
            int[] values = { p.Speed, p.Stamina, p.Pass, p.Shot, p.Tackle, p.Positioning, p.Reflexes, p.Handling, p.Diving };
            for (int i = 1; i < values.Length; i++) { if (values[i] > best) { best = values[i]; which = i; } }
            switch (which)
            {
                case 0: p.Speed += diff; break;
                case 1: p.Stamina += diff; break;
                case 2: p.Pass += diff; break;
                case 3: p.Shot += diff; break;
                case 4: p.Tackle += diff; break;
                case 5: p.Positioning += diff; break;
                case 6: p.Reflexes += diff; break;
                case 7: p.Handling += diff; break;
                default: p.Diving += diff; break;
            }
        }
    }
}
