using System;
using System.Collections.Generic;
using Game.Core.AutoMatch;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Tactics;

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
        public const string DefaultPresetId = "balanced";   // 09-26 세이브엔 프리셋이 없다 → 균형

        public int Tier { get; }
        public int SeasonSeed { get; }
        public int GeneratorVersion { get; }
        public int RoundsPlayed { get; private set; }
        public int ScoutAttempts { get; set; }
        public string MyPresetId { get; private set; }   // 내 팀 기준 카드(TacticPresets presetId). 바꾸는 곳은 SetMyPreset 하나(팀 전술 설정창, 09-30)
        public TeamTactics? MyCustomTactics { get; private set; }   // 슬라이더·세부 표로 바꾼 값 전체. null = 기준 카드 값 그대로(09-30)
        public IReadOnlyList<MatchResult> Results => results;
        public IReadOnlyList<RosterPlayer> Roster => roster;
        public IReadOnlyList<LineupEntry> Lineup => lineup;

        private readonly List<MatchResult> results;
        private readonly List<RosterPlayer> roster;
        private readonly List<LineupEntry> lineup;

        public SeasonState(int tier, int seasonSeed, int generatorVersion, List<RosterPlayer> roster, List<LineupEntry> lineup,
            List<MatchResult> results, int roundsPlayed, int scoutAttempts, string? myPresetId = null, TeamTactics? myCustomTactics = null)
        {
            MyPresetId = string.IsNullOrEmpty(myPresetId) ? DefaultPresetId : myPresetId!;
            if (myCustomTactics != null) { TacticsEditing.Validate(myCustomTactics); }
            MyCustomTactics = myCustomTactics;
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

        // 새 시즌: 내 로스터 11명은 편성 행(StageComposition의 player 쪽)에서 기본 변형으로 만든다. 선수 id는 0부터.
        // 행 → 자리 펼치기는 MatchAssembler.ToLineup 하나만 쓴다(09-27 리뷰 X2: StageManager와 여기가 각자 펼치던 것)
        public static SeasonState NewSeason(int tier, int seasonSeed, IReadOnlyList<StageEntry> myRows, IReadOnlyList<PlayerStats> table, string? presetId = null)
        {
            IReadOnlyList<LineupSlot> slots = MatchAssembler.ToLineup(table, myRows, MyTeamId);
            var roster = new List<RosterPlayer>(slots.Count);
            var lineup = new List<LineupEntry>(slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                LineupSlot s = slots[i];
                roster.Add(new RosterPlayer(i, s.Stats));
                lineup.Add(new LineupEntry(i, s.AttackX, s.AttackZ, s.DefendX, s.DefendZ));
            }
            return new SeasonState(tier, seasonSeed, TeamGenerator.Version, roster, lineup, new List<MatchResult>(), 0, 0, presetId);
        }

        public bool MatchesGenerator => GeneratorVersion == TeamGenerator.Version;

        // 카드를 고를 때(팀 전술 설정창 1단, 09-30). 없는 id면 던지고 값을 바꾸지 않는다: 잘못된 id가 세이브에 들어가면 다음 경기를 차릴 때에야 터진다.
        // 카드를 고르면 아래 전부가 그 카드 값으로 돌아간다(목업 규칙). 그래서 바꾼 값을 지운다
        public void SetMyPreset(string presetId, IReadOnlyList<TeamTactics> presets)
        {
            SeasonRunner.FindPreset(presets, presetId);   // 없으면 InvalidOperationException
            MyPresetId = presetId;
            MyCustomTactics = null;
        }

        // 슬라이더로 바꾼 값(팀 전술 설정창 2단). 기준 카드(MyPresetId)는 그대로 둔다: 슬라이더가 그 카드 기준 오프셋이라서다. 범위 밖이면 던진다.
        // 불변식: MyCustomTactics가 있으면 값이 기준 카드와 다르다. 같아지면(슬라이더를 가운데로) 바꾼 값 없음으로 돌아간다
        public void SetMyTactics(TeamTactics tactics, IReadOnlyList<TeamTactics> presets)
        {
            TacticsEditing.Validate(tactics);
            if (TacticsEditing.SameValues(tactics, SeasonRunner.FindPreset(presets, MyPresetId)))
            {
                MyCustomTactics = null;
                return;
            }
            MyCustomTactics = TacticsEditing.Copy(tactics);
        }

        // 세부 표로 바꾼 값(3단). 어떤 카드와 값이 같아지면 그 카드를 고른 것으로 친다:
        // 화면이 강조하는 카드와 슬라이더가 기준으로 삼는 카드가 어긋나지 않게(09-30 리뷰)
        public void SetMyTacticsFromTable(TeamTactics tactics, IReadOnlyList<TeamTactics> presets)
        {
            TacticsEditing.Validate(tactics);
            string? matching = TacticsEditing.MatchingPreset(tactics, presets);
            if (matching != null)
            {
                SetMyPreset(matching, presets);
                return;
            }
            MyCustomTactics = TacticsEditing.Copy(tactics);
        }

        // 경기에 들어가는 내 전술: 바꾼 값이 있으면 그것, 없으면 기준 카드
        public TeamTactics MyTactics(IReadOnlyList<TeamTactics> presets)
        {
            if (MyCustomTactics != null) { return MyCustomTactics; }
            return SeasonRunner.FindPreset(presets, MyPresetId);
        }

        // 한 라운드의 전 경기를 한 번에 넣는다(09-27 리뷰 D3): 중간에 예외가 나면 아무것도 안 들어가 재호출해도 결과가 겹치지 않는다
        public void AddRound(IReadOnlyList<MatchResult> roundResults)
        {
            results.AddRange(roundResults);
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

        // ── 세이브 왕복. 로스터 선수는 variantId + 빌드 9개만 저장(다이얼은 PlayerTable에서 다시 읽는다. 스카우트가 빌드를 바꾸는 날을 위해 빌드는 저장)
        public SeasonSave ToSave()
        {
            var save = new SeasonSave { tier = Tier, seasonSeed = SeasonSeed, generatorVersion = GeneratorVersion, roundsPlayed = RoundsPlayed, scoutAttempts = ScoutAttempts, myPresetId = MyPresetId };
            if (MyCustomTactics != null) { save.myTactics = SeasonSave.TacticsValues.From(MyCustomTactics); }
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
            if (save.lineup.Count != FormationTemplate.SlotCount) { throw new InvalidOperationException($"[SeasonState] 라인업은 {FormationTemplate.SlotCount}명이어야 한다({save.lineup.Count})"); }
            var roster = new List<RosterPlayer>(save.roster.Count);
            var ids = new HashSet<int>();
            for (int i = 0; i < save.roster.Count; i++)
            {
                SeasonSave.Player p = save.roster[i];
                if (!ids.Add(p.playerId)) { throw new InvalidOperationException($"[SeasonState] 로스터 선수 id 중복 {p.playerId}"); }
                PlayerStats? variant = PlayerTableLookup.FindVariant(table, p.variantId);
                if (variant == null) { throw new InvalidOperationException($"[SeasonState] 세이브의 variantId가 PlayerTable에 없다: {p.variantId}"); }
                if (p.build == null || p.build.Length != 9) { throw new InvalidOperationException($"[SeasonState] 선수 {p.playerId} 빌드가 9개가 아니다"); }
                PlayerStats copy = BuildScaler.Copy(variant);   // 다이얼·문자열은 PlayerTable에서, 빌드는 세이브에서
                copy.Speed = p.build[0]; copy.Stamina = p.build[1]; copy.Pass = p.build[2]; copy.Shot = p.build[3]; copy.Tackle = p.build[4];
                copy.Positioning = p.build[5]; copy.Reflexes = p.build[6]; copy.Handling = p.build[7]; copy.Diving = p.build[8];
                if (copy.BuildTotal != PlayerStats.TotalPoints) { throw new InvalidOperationException($"[SeasonState] 선수 {p.playerId} 빌드 합계가 {PlayerStats.TotalPoints}가 아니다({copy.BuildTotal}). 내 선수는 총점 고정(스펙 축: 강화 없음)"); }
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
            TeamTactics? custom = save.myTactics == null ? null : save.myTactics.ToTactics(save.myPresetId ?? DefaultPresetId);   // 범위는 생성자가 검증한다
            return new SeasonState(save.tier, save.seasonSeed, save.generatorVersion, roster, lineup, results, save.roundsPlayed, save.scoutAttempts, save.myPresetId, custom);
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
        public string? myPresetId;   // 09-27 추가. 옛 세이브(09-26)엔 없어 null → DefaultPresetId
        public TacticsValues? myTactics;   // 09-30 추가(팀 전술 설정창). 카드 값 그대로면 null. 옛 세이브엔 없어 null → 기준 카드 값
        public List<Result> results = new List<Result>();
        public List<Player> roster = new List<Player>();
        public List<Slot> lineup = new List<Slot>();

        [Serializable] public sealed class Result { public int home; public int away; public int homeGoals; public int awayGoals; }
        [Serializable] public sealed class Player { public int playerId; public string variantId = string.Empty; public int[] build = Array.Empty<int>(); }
        [Serializable] public sealed class Slot { public int playerId; public float attackX; public float attackZ; public float defendX; public float defendZ; }

        // 바꾼 팀 전술 값. 이름·설명은 기준 카드에서 다시 읽으므로 판정에 쓰는 값만 저장한다
        [Serializable]
        public sealed class TacticsValues
        {
            public int[] passStyle = Array.Empty<int>();
            public int[] passRisk = Array.Empty<int>();
            public int[] tempo = Array.Empty<int>();
            public int[] width = Array.Empty<int>();
            public int[] pressStart = Array.Empty<int>();
            public int counter;
            public int counterPress;
            public int gkDistribution;
            public int mentality;

            public static TacticsValues From(TeamTactics t)
            {
                return new TacticsValues
                {
                    passStyle = (int[])t.PassStyle.Clone(),
                    passRisk = (int[])t.PassRisk.Clone(),
                    tempo = (int[])t.Tempo.Clone(),
                    width = (int[])t.Width.Clone(),
                    pressStart = (int[])t.PressStart.Clone(),
                    counter = t.Counter,
                    counterPress = t.CounterPress,
                    gkDistribution = t.GkDistribution,
                    mentality = t.Mentality,
                };
            }

            public TeamTactics ToTactics(string basePresetId)
            {
                return new TeamTactics
                {
                    PresetId = basePresetId,
                    PassStyle = (int[])passStyle.Clone(),
                    PassRisk = (int[])passRisk.Clone(),
                    Tempo = (int[])tempo.Clone(),
                    Width = (int[])width.Clone(),
                    PressStart = (int[])pressStart.Clone(),
                    Counter = counter,
                    CounterPress = counterPress,
                    GkDistribution = gkDistribution,
                    Mentality = mentality,
                };
            }
        }
    }
}
