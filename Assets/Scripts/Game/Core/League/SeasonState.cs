using System;
using System.Collections.Generic;
using Game.Core.Data;

namespace Game.Core.League
{
    // 내 선수 개체(스펙 §11): 라인업은 빌드·다이얼을 인라인하지 않고 이 개체를 id로 가리킨다. 스카우트 교체가 로스터에 개체를 더하는 일이 되게
    public sealed class RosterPlayer
    {
        public int PlayerId { get; }
        public PlayerStats Stats { get; }

        public RosterPlayer(int playerId, PlayerStats stats)
        {
            PlayerId = playerId;
            Stats = stats;
        }
    }

    // 라인업 한 자리: 로스터 선수 id + 자리 2쌍(공격 시/수비 시)
    public readonly struct LineupEntry
    {
        public readonly int PlayerId;
        public readonly float AttackX;
        public readonly float AttackZ;
        public readonly float DefendX;
        public readonly float DefendZ;

        public LineupEntry(int playerId, float attackX, float attackZ, float defendX, float defendZ)
        {
            PlayerId = playerId;
            AttackX = attackX;
            AttackZ = attackZ;
            DefendX = defendX;
            DefendZ = defendZ;
        }
    }

    // 시즌 상태(스펙 §11 세이브 항목 그대로). 순수 C#: 엔진·파일을 모른다. 세이브 DTO(SeasonSave)와 왕복한다.
    // 상대 팀은 안 들어 있다: (SeasonSeed, Tier)로 TeamGenerator가 매번 같은 팀을 만든다. GeneratorVersion이 다르면 시즌을 새로 시작한다
    public sealed class SeasonState
    {
        public const int MyTeamId = 0;

        public int Tier { get; }
        public int SeasonSeed { get; }
        public int GeneratorVersion { get; }
        public int RoundsPlayed { get; private set; }
        public int ScoutAttempts { get; set; }
        public IReadOnlyList<MatchResult> Results => results;
        public IReadOnlyList<RosterPlayer> Roster => roster;
        public IReadOnlyList<LineupEntry> Lineup => lineup;

        private readonly List<MatchResult> results;
        private readonly List<RosterPlayer> roster;
        private readonly List<LineupEntry> lineup;

        public SeasonState(int tier, int seasonSeed, int generatorVersion, List<RosterPlayer> roster, List<LineupEntry> lineup,
            List<MatchResult> results, int roundsPlayed, int scoutAttempts)
        {
            Tier = tier;
            SeasonSeed = seasonSeed;
            GeneratorVersion = generatorVersion;
            this.roster = roster;
            this.lineup = lineup;
            this.results = results;
            RoundsPlayed = roundsPlayed;
            ScoutAttempts = scoutAttempts;
            ValidateLineup();
        }

        // 새 시즌: 내 로스터 11명은 편성 행(StageComposition의 player 쪽)에서 기본 변형으로 만든다. 선수 id는 0부터
        public static SeasonState NewSeason(int tier, int seasonSeed, IReadOnlyList<StageEntry> myRows, IReadOnlyList<PlayerStats> table)
        {
            var roster = new List<RosterPlayer>();
            var lineup = new List<LineupEntry>();
            int nextId = 0;
            for (int i = 0; i < myRows.Count; i++)
            {
                StageEntry r = myRows[i];
                if (r.Team != 0) { continue; }
                PlayerStats? stats = FindVariant(table, r.Id);
                if (stats == null) { throw new InvalidOperationException($"[SeasonState] PlayerTable에 없는 variantId: {r.Id}"); }
                for (int k = 0; k < r.Count; k++)
                {
                    roster.Add(new RosterPlayer(nextId, stats));
                    lineup.Add(new LineupEntry(nextId, r.PosX, r.PosZ, r.DefendX, r.DefendZ));
                    nextId++;
                }
            }
            return new SeasonState(tier, seasonSeed, TeamGenerator.Version, roster, lineup, new List<MatchResult>(), 0, 0);
        }

        public bool MatchesGenerator => GeneratorVersion == TeamGenerator.Version;

        public void AddResult(MatchResult result)
        {
            results.Add(result);
        }

        public void CompleteRound()
        {
            RoundsPlayed++;
        }

        public RosterPlayer FindRosterPlayer(int playerId)
        {
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].PlayerId == playerId) { return roster[i]; }
            }
            throw new InvalidOperationException($"[SeasonState] 로스터에 없는 선수 id {playerId}");
        }

        public IReadOnlyList<LineupSlot> MyLineup()
        {
            var slots = new List<LineupSlot>(lineup.Count);
            for (int i = 0; i < lineup.Count; i++)
            {
                LineupEntry e = lineup[i];
                slots.Add(new LineupSlot(FindRosterPlayer(e.PlayerId).Stats, e.AttackX, e.AttackZ, e.DefendX, e.DefendZ));
            }
            return slots;
        }

        public LeagueTable Table(int teamCount)
        {
            var ids = new List<int>(teamCount);
            for (int i = 0; i < teamCount; i++) { ids.Add(i); }
            return LeagueTable.Standings(ids, results);
        }

        // 라인업이 로스터 밖 id를 가리키면 로드 실패로 처리한다(조용히 빈 선수를 세우지 않는다)
        private void ValidateLineup()
        {
            for (int i = 0; i < lineup.Count; i++) { FindRosterPlayer(lineup[i].PlayerId); }
        }

        private static PlayerStats? FindVariant(IReadOnlyList<PlayerStats> table, string variantId)
        {
            for (int i = 0; i < table.Count; i++)
            {
                if (string.Equals(table[i].VariantId, variantId, StringComparison.OrdinalIgnoreCase)) { return table[i]; }
            }
            return null;
        }

        // ── 세이브 왕복. 로스터 선수는 variantId + 빌드 9개만 저장(다이얼은 PlayerTable에서 다시 읽는다. 스카우트가 빌드를 바꾸는 날을 위해 빌드는 저장)
        public SeasonSave ToSave()
        {
            var save = new SeasonSave { tier = Tier, seasonSeed = SeasonSeed, generatorVersion = GeneratorVersion, roundsPlayed = RoundsPlayed, scoutAttempts = ScoutAttempts };
            for (int i = 0; i < results.Count; i++)
            {
                MatchResult r = results[i];
                save.results.Add(new SeasonSave.Result { home = r.HomeTeamId, away = r.AwayTeamId, homeGoals = r.HomeGoals, awayGoals = r.AwayGoals });
            }
            for (int i = 0; i < roster.Count; i++)
            {
                PlayerStats s = roster[i].Stats;
                save.roster.Add(new SeasonSave.Player
                {
                    playerId = roster[i].PlayerId, variantId = s.VariantId,
                    build = new[] { s.Speed, s.Stamina, s.Pass, s.Shot, s.Tackle, s.Positioning, s.Reflexes, s.Handling, s.Diving },
                });
            }
            for (int i = 0; i < lineup.Count; i++)
            {
                LineupEntry e = lineup[i];
                save.lineup.Add(new SeasonSave.Slot { playerId = e.PlayerId, attackX = e.AttackX, attackZ = e.AttackZ, defendX = e.DefendX, defendZ = e.DefendZ });
            }
            return save;
        }

        public static SeasonState FromSave(SeasonSave save, IReadOnlyList<PlayerStats> table)
        {
            var roster = new List<RosterPlayer>(save.roster.Count);
            for (int i = 0; i < save.roster.Count; i++)
            {
                SeasonSave.Player p = save.roster[i];
                PlayerStats? variant = FindVariant(table, p.variantId);
                if (variant == null) { throw new InvalidOperationException($"[SeasonState] 세이브의 variantId가 PlayerTable에 없다: {p.variantId}"); }
                if (p.build == null || p.build.Length != 9) { throw new InvalidOperationException($"[SeasonState] 선수 {p.playerId} 빌드가 9개가 아니다"); }
                PlayerStats copy = BuildScaler.Copy(variant);   // 다이얼·문자열은 PlayerTable에서, 빌드는 세이브에서
                copy.Speed = p.build[0]; copy.Stamina = p.build[1]; copy.Pass = p.build[2]; copy.Shot = p.build[3]; copy.Tackle = p.build[4];
                copy.Positioning = p.build[5]; copy.Reflexes = p.build[6]; copy.Handling = p.build[7]; copy.Diving = p.build[8];
                roster.Add(new RosterPlayer(p.playerId, copy));
            }
            var lineup = new List<LineupEntry>(save.lineup.Count);
            for (int i = 0; i < save.lineup.Count; i++)
            {
                SeasonSave.Slot s = save.lineup[i];
                lineup.Add(new LineupEntry(s.playerId, s.attackX, s.attackZ, s.defendX, s.defendZ));
            }
            var results = new List<MatchResult>(save.results.Count);
            for (int i = 0; i < save.results.Count; i++)
            {
                SeasonSave.Result r = save.results[i];
                results.Add(new MatchResult(r.home, r.away, r.homeGoals, r.awayGoals));
            }
            return new SeasonState(save.tier, save.seasonSeed, save.generatorVersion, roster, lineup, results, save.roundsPlayed, save.scoutAttempts);
        }
    }

    // 세이브 DTO. 템플릿 SaveDataV1의 필드로 얹힌다(Json.NET, 필드 camelCase 관례). 옛 세이브에 이 필드가 없으면 null = 시즌 없음
    [Serializable]
    public sealed class SeasonSave
    {
        public int tier;
        public int seasonSeed;
        public int generatorVersion;
        public int roundsPlayed;
        public int scoutAttempts;
        public List<Result> results = new List<Result>();
        public List<Player> roster = new List<Player>();
        public List<Slot> lineup = new List<Slot>();

        [Serializable] public sealed class Result { public int home; public int away; public int homeGoals; public int awayGoals; }
        [Serializable] public sealed class Player { public int playerId; public string variantId = string.Empty; public int[] build = Array.Empty<int>(); }
        [Serializable] public sealed class Slot { public int playerId; public float attackX; public float attackZ; public float defendX; public float defendZ; }
    }
}
