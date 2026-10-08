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
        public PlayerStats Stats { get; }   // 능력치(빌드) + 역할 값(다이얼). 개인 지시는 여기 안 들어 있다
        public IReadOnlyList<int> Instructions => instructions;   // 개인 지시(09-30): 역할 값 기준 오프셋, PlayerDial 순서. 전부 0이면 역할 값 그대로

        private readonly int[] instructions;

        public RosterPlayer(int playerId, PlayerStats stats, int[]? instructions = null)
        {
            PlayerId = playerId;
            Stats = stats;
            this.instructions = instructions == null ? new int[PlayerInstructions.DialCount] : (int[])instructions.Clone();
            PlayerInstructions.Validate(this.instructions, stats);
        }

        public bool HasInstructions
        {
            get
            {
                for (int i = 0; i < instructions.Length; i++)
                {
                    if (instructions[i] != 0) { return true; }
                }
                return false;
            }
        }

        // 경기에 들어가는 값: 역할 값에 개인 지시를 얹은 것
        public PlayerStats EffectiveStats()
        {
            return PlayerInstructions.Apply(Stats, instructions);
        }

        public int[] CopyInstructions()
        {
            return (int[])instructions.Clone();
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

        // ── 개인 전술(09-30, 전술 화면에서 필드의 선수 칩을 눌러 정한다)
        // 역할 변경: 같은 자리의 노출된 변형만. 지시 값(다이얼)은 새 역할 것으로, 능력치(빌드)는 그대로 둔다(스펙 축: 강화 없음, 목업 "스탯은 전술 창에 없다").
        // 개인 지시는 역할 기준 오프셋이라 역할을 바꾸면 지운다(팀 전술에서 카드를 고르면 바꾼 값이 지워지는 것과 같은 규칙)
        public void SetPlayerRole(int playerId, string variantId, IReadOnlyList<PlayerStats> table)
        {
            int index = RosterIndex(playerId);
            PlayerStats current = roster[index].Stats;
            PlayerStats? variant = PlayerTableLookup.FindVariant(table, variantId);
            if (variant == null) { throw new InvalidOperationException($"[SeasonState] PlayerTable에 없는 역할: {variantId}"); }
            if (!PlayerTableLookup.IsAssignable(variant, current.RoleId))
            {
                throw new InvalidOperationException($"[SeasonState] 선수 {playerId}({current.RoleId})에게 줄 수 없는 역할: {variantId}(자리 {variant.RoleId}, 노출 {variant.Exposed})");
            }
            ApplyRole(index, variant);
        }

        // 역할을 바꾸고 개인 지시를 지운다. 받아도 되는지는 부르는 쪽이 정한다(새로 고를 땐 노출까지, 이월은 같은 자리만)
        private void ApplyRole(int index, PlayerStats variant)
        {
            PlayerStats next = variant.Clone();
            CopyBuild(roster[index].Stats, next);
            roster[index] = new RosterPlayer(roster[index].PlayerId, next);
        }

        // 개인 지시 한 칸. 범위 밖·해당 없는 다이얼이면 던지고 아무것도 안 바뀐다(RosterPlayer 생성자가 검증)
        public void SetPlayerInstruction(int playerId, PlayerDial dial, int offset)
        {
            int index = RosterIndex(playerId);
            int[] next = roster[index].CopyInstructions();
            next[(int)dial] = offset;
            roster[index] = new RosterPlayer(playerId, roster[index].Stats, next);
        }

        public void ClearPlayerInstructions(int playerId)
        {
            int index = RosterIndex(playerId);
            roster[index] = new RosterPlayer(playerId, roster[index].Stats);
        }

        // 새 시즌의 로스터는 기본 편성에서 다시 만들어진다(SeasonFactory). 이전 시즌의 역할과 개인 지시를 같은 선수 id로 옮겨 온다.
        // 이전 역할이 표에서 사라졌거나 자리가 다르면 그 선수는 기본 역할로 둔다. 잠긴 역할은 옮긴다(10-08: 업데이트로 잠겨도 쓰던 것은 남는다, PlayerTableLookup.IsSamePosition 주석)
        public void AdoptPlayerTactics(IReadOnlyList<RosterPlayer> previousRoster, IReadOnlyList<PlayerStats> table)
        {
            for (int i = 0; i < previousRoster.Count; i++)
            {
                RosterPlayer previous = previousRoster[i];
                int index = IndexOfRosterPlayer(previous.PlayerId);
                if (index < 0) { continue; }
                PlayerStats? variant = PlayerTableLookup.FindVariant(table, previous.Stats.VariantId);
                if (variant == null) { continue; }
                if (!PlayerTableLookup.IsSamePosition(variant, roster[index].Stats.RoleId)) { continue; }

                ApplyRole(index, variant);
                roster[index] = new RosterPlayer(previous.PlayerId, roster[index].Stats, previous.CopyInstructions());
            }
        }

        private static void CopyBuild(PlayerStats from, PlayerStats to)
        {
            to.Speed = from.Speed;
            to.Stamina = from.Stamina;
            to.Pass = from.Pass;
            to.Shot = from.Shot;
            to.Tackle = from.Tackle;
            to.Positioning = from.Positioning;
            to.Reflexes = from.Reflexes;
            to.Handling = from.Handling;
            to.Diving = from.Diving;
        }

        private int RosterIndex(int playerId)
        {
            int index = IndexOfRosterPlayer(playerId);
            if (index < 0) { throw new InvalidOperationException($"[SeasonState] 로스터에 없는 선수 id {playerId}"); }
            return index;
        }

        private int IndexOfRosterPlayer(int playerId)
        {
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].PlayerId == playerId) { return i; }
            }
            return -1;
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
                slots.Add(new LineupSlot(FindRosterPlayer(e.PlayerId).EffectiveStats(), e.AttackX, e.AttackZ, e.DefendX, e.DefendZ));   // 역할 값 + 개인 지시(09-30)
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
                    instructions = roster[i].HasInstructions ? roster[i].CopyInstructions() : null,
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
                roster.Add(new RosterPlayer(p.playerId, copy, p.instructions));   // 지시 칸 수·범위는 생성자가 검증한다
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

        [Serializable] public sealed class Result
        {
            public int home;
            public int away;
            public int homeGoals;
            public int awayGoals;
        }
        [Serializable]
        public sealed class Player
        {
            public int playerId;
            public string variantId = string.Empty;   // 역할. 전술 화면에서 같은 자리 안에서 바꾼다(09-30)
            public int[] build = Array.Empty<int>();
            public int[]? instructions;               // 09-30 추가. 개인 지시 오프셋(PlayerDial 순서). 지시가 없으면 null, 옛 세이브에도 없어 null → 역할 값 그대로
        }
        [Serializable] public sealed class Slot
        {
            public int playerId;
            public float attackX;
            public float attackZ;
            public float defendX;
            public float defendZ;
        }

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
