using System;
using System.Collections.Generic;
using Game.Core.AutoMatch;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Placement;
using Game.Core.Tactics;

namespace Game.Core.League
{
    // 내 선수 개체(스펙 §11): 라인업은 빌드·다이얼을 인라인하지 않고 이 개체를 id로 가리킨다. 스카우트 영입이 로스터에 개체를 더하는 일이 되게
    public sealed class RosterPlayer
    {
        public int PlayerId { get; }
        public PlayerStats Stats { get; }   // 능력치(빌드) + 역할 값(다이얼). 개인 지시는 여기 안 들어 있다
        public IReadOnlyList<int> Instructions => instructions;   // 개인 지시: 역할 값 기준 오프셋, PlayerDial 순서. 전부 0이면 역할 값 그대로
        public int Stars { get; }   // 조합 특이성(ScoutRules.Stars). 기본 로스터는 1, 영입 선수는 판정 때 값. 방출 코인의 재료(스펙 §4-5 10-19)
        public int Portrait { get; } // 초상 번호(PortraitRules, 1~VariantCount). 영입 선수는 후보 때 본 얼굴, 기본 로스터·옛 세이브는 playerId로(10-21)

        private readonly int[] instructions;

        // portrait를 안 주면(None) playerId로 정한다. 영입·복사 경로는 반드시 넘긴다 — 빠뜨리면 그 선수 얼굴이 바뀐다(PortraitTests가 잡는다)
        public RosterPlayer(int playerId, PlayerStats stats, int[]? instructions = null, int stars = SquadRules.DefaultStars, int portrait = PortraitRules.None)
        {
            if (stars < ScoutRules.MinStars || stars > ScoutRules.MaxStars) { throw new ArgumentOutOfRangeException(nameof(stars), $"[RosterPlayer] ★ {stars}"); }
            if (portrait != PortraitRules.None && !PortraitRules.IsValid(portrait)) { throw new ArgumentOutOfRangeException(nameof(portrait), $"[RosterPlayer] 초상 {portrait}"); }
            PlayerId = playerId;
            Stats = stats;
            Stars = stars;
            Portrait = portrait == PortraitRules.None ? PortraitRules.Of(playerId) : portrait;
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

        // 불변 객체라 "하나만 바꾼 복사본"으로 고친다. 필드가 늘어도 호출부가 빠뜨릴 자리가 없게(10-19 리뷰: 생성자를 손으로 다시 부르다 ★을 빠뜨렸다)
        public RosterPlayer WithStats(PlayerStats stats)
        {
            return new RosterPlayer(PlayerId, stats, instructions, Stars, Portrait);
        }

        public RosterPlayer WithInstructions(int[]? next)
        {
            return new RosterPlayer(PlayerId, Stats, next, Stars, Portrait);
        }
    }

    // 스카우트 시도 기록: 어느 팀의 몇 번째 선수를 노렸고 됐나. 재시도 보정(같은 선수 횟수)과 후보 교체(성공한 자리만, ScoutPool)의 재료
    public readonly struct ScoutTarget
    {
        public readonly int TeamId;
        public readonly int PlayerIndex;
        public readonly bool Success;

        public ScoutTarget(int teamId, int playerIndex, bool success)
        {
            TeamId = teamId;
            PlayerIndex = playerIndex;
            Success = success;
        }
    }

    // 라인업 한 자리: 로스터 선수 id + 공격 포메이션 슬롯·수비 포메이션 슬롯·수비 역할(10-25 슬롯 격자) + 자리 2쌍(공격 시/수비 시).
    // 자리 2쌍은 포메이션·슬롯에서 파생한 캐시(SetFormations·SwapLineupSlots가 다시 채운다). 10-08 자유 배치(끌어 놓은 좌표 저장)는 10-27 ④에서 지웠다
    public readonly struct LineupEntry
    {
        public readonly int PlayerId;
        public readonly int AttackSlot;        // 공격 포메이션의 슬롯 1~11
        public readonly int DefenseSlot;       // 수비 포메이션의 슬롯 1~11
        public readonly string DefenseRoleId;  // DefenseRoles.csv id
        public readonly float AttackX;
        public readonly float AttackZ;
        public readonly float DefendX;
        public readonly float DefendZ;

        public LineupEntry(int playerId, int attackSlot, int defenseSlot, string defenseRoleId, float attackX, float attackZ, float defendX, float defendZ)
        {
            PlayerId = playerId;
            AttackSlot = attackSlot;
            DefenseSlot = defenseSlot;
            DefenseRoleId = string.IsNullOrEmpty(defenseRoleId) ? DefenseRole.DefaultId : defenseRoleId;
            AttackX = attackX;
            AttackZ = attackZ;
            DefendX = defendX;
            DefendZ = defendZ;
        }

        public LineupEntry WithPlayer(int playerId) { return new LineupEntry(playerId, AttackSlot, DefenseSlot, DefenseRoleId, AttackX, AttackZ, DefendX, DefendZ); }
        public LineupEntry WithAttack(int slot, float x, float z) { return new LineupEntry(PlayerId, slot, DefenseSlot, DefenseRoleId, x, z, DefendX, DefendZ); }
        public LineupEntry WithDefense(int slot, float x, float z) { return new LineupEntry(PlayerId, AttackSlot, slot, DefenseRoleId, AttackX, AttackZ, x, z); }
        public LineupEntry WithDefenseRole(string roleId) { return new LineupEntry(PlayerId, AttackSlot, DefenseSlot, roleId, AttackX, AttackZ, DefendX, DefendZ); }
    }

    // 시즌 상태(스펙 §11 세이브 항목 그대로). 순수 C#: 엔진·파일을 모른다. 세이브 DTO(SeasonSave)와 왕복한다.
    // 상대 팀은 안 들어 있다: (SeasonSeed, Tier)로 TeamGenerator가 매번 같은 팀을 만든다. GeneratorVersion이 다르면 시즌을 새로 시작한다
    public sealed class SeasonState
    {
        public const int MyTeamId = 0;
        public const string DefaultPresetId = "balanced";   // 09-26 세이브엔 프리셋이 없다 → 균형
        public const string DefaultFormationId = "4-4-2";   // 10-25 전 세이브·새 시즌의 공수 포메이션. StageComposition 기본 11명의 순서가 이 템플릿 슬롯 순서와 같다

        public string AttackFormationId { get; private set; } = DefaultFormationId;    // 공격 시 모양(FM26 In Possession)
        public string DefenseFormationId { get; private set; } = DefaultFormationId;   // 수비 시 모양(FM26 Out of Possession). 따로 고른다

        public int Tier { get; }
        public int SeasonSeed { get; }
        public int GeneratorVersion { get; }
        public int RoundsPlayed { get; private set; }
        public int ScoutAttempts { get; private set; }       // 이번 시즌에 쓴 시도(실패 포함)
        public int ScoutExtraAttempts { get; private set; }  // 코인으로 산 추가 시도
        public int RemainingScoutAttempts => ScoutRules.AttemptsPerSeason + ScoutExtraAttempts - ScoutAttempts;
        public IReadOnlyList<ScoutTarget> ScoutTargets => scoutTargets;   // 이번 시즌 시도 기록(실패 포함). 후보 선정이 읽는다
        public string MyPresetId { get; private set; }   // 내 팀 기준 카드(TacticPresets presetId). 바꾸는 곳은 SetMyPreset 하나(팀 전술 설정창, 09-30)
        public TeamTactics? MyCustomTactics { get; private set; }   // 슬라이더·세부 표로 바꾼 값 전체. null = 기준 카드 값 그대로
        public TacticsReadoutSummary? LastReadout { get; private set; }   // 지난 내 경기의 실측 요약(로비 홈 한 줄, 스펙 §2-1). 아직 안 치렀거나 옛 세이브면 null
        public IReadOnlyList<MatchResult> Results => results;
        public IReadOnlyList<PlayoffResult> PlayoffResults => playoff;   // 승강전 결과. 정규 시즌 뒤에만 쌓인다. 옛 세이브는 비어 있음
        public IReadOnlyList<RosterPlayer> Roster => roster;
        public IReadOnlyList<LineupEntry> Lineup => lineup;

        private readonly List<MatchResult> results;
        private readonly List<PlayoffResult> playoff = new List<PlayoffResult>();
        private readonly List<ScoutTarget> scoutTargets = new List<ScoutTarget>();
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
                lineup.Add(new LineupEntry(i, i + 1, i + 1, DefenseRole.DefaultId, s.AttackX, s.AttackZ, s.DefendX, s.DefendZ));   // 편성 행 순서 = 4-4-2 슬롯 순서
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
        // 화면이 강조하는 카드와 슬라이더가 기준으로 삼는 카드가 어긋나지 않게
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

        // 화면에 적는 내 전술 이름. 바꾼 값이 있어도 기준 카드 이름이다.
        // 바꾼 값(MyCustomTactics)은 세이브 왕복 뒤 DisplayName이 비어 있다(10-15 Play 홈의 "내 전술:" 빈 칸)
        public string MyCardName(IReadOnlyList<TeamTactics> presets)
        {
            return SeasonRunner.FindPreset(presets, MyPresetId).DisplayName;
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
            roster[index] = roster[index].WithStats(next).WithInstructions(null);   // 지시는 역할 기준 오프셋이라 역할이 바뀌면 지운다
        }

        // 개인 지시 한 칸. 범위 밖·해당 없는 다이얼이면 던지고 아무것도 안 바뀐다(RosterPlayer 생성자가 검증)
        public void SetPlayerInstruction(int playerId, PlayerDial dial, int offset)
        {
            int index = RosterIndex(playerId);
            int[] next = roster[index].CopyInstructions();
            next[(int)dial] = offset;
            roster[index] = roster[index].WithInstructions(next);
        }

        public void ClearPlayerInstructions(int playerId)
        {
            int index = RosterIndex(playerId);
            roster[index] = roster[index].WithInstructions(null);
        }

        // 새 시즌의 로스터는 기본 편성에서 다시 만들어진다(SeasonFactory). 이전 시즌 선발을 **슬롯 번호**로 옮겨 온다: 새 선발 i ← 이전 라인업 슬롯 i에 서 있던 선수의 역할·개인 지시·★·자리 2쌍.
        // 선수 id가 아니라 슬롯인 이유(10-19 스쿼드): 선발에 영입 선수(id ≥ 11)가 설 수 있어 id = 슬롯이 더는 성립하지 않는다.
        // 공수 포메이션 id는 여기서 안 옮긴다: 호출처(SeasonSystem.StartNextSeason)가 이전 id로 SetFormations를 먼저 부른 뒤 이 메서드를 부른다(10-25 리뷰 🔴: 좌표만 이월하면 id와 어긋난다).
        // 이전 역할이 표에서 사라졌거나 자리가 다르면 그 슬롯은 기본 역할로 둔다. 잠긴 역할은 옮긴다(업데이트로 잠겨도 쓰던 것은 남는다, PlayerTableLookup.IsSamePosition 주석).
        // 배치(자리 2쌍)는 필드 규칙이 부와 무관해 그대로 유효하다. 벤치는 AdoptBench
        public void AdoptStarters(IReadOnlyList<RosterPlayer> previousRoster, IReadOnlyList<LineupEntry> previousLineup, IReadOnlyList<PlayerStats> table)
        {
            int slots = Math.Min(lineup.Count, previousLineup.Count);
            for (int slot = 0; slot < slots; slot++)
            {
                LineupEntry previousEntry = previousLineup[slot];
                RosterPlayer? previous = FindOrNull(previousRoster, previousEntry.PlayerId);
                if (previous == null) { continue; }
                int index = RosterIndex(lineup[slot].PlayerId);
                lineup[slot] = previousEntry.WithPlayer(lineup[slot].PlayerId);   // 슬롯·수비 역할·자리 2쌍 전부 이월

                PlayerStats? variant = PlayerTableLookup.FindVariant(table, previous.Stats.VariantId);
                if (variant == null) { continue; }
                if (!PlayerTableLookup.IsSamePosition(variant, roster[index].Stats.RoleId)) { continue; }
                ApplyRole(index, variant);
                roster[index] = new RosterPlayer(roster[index].PlayerId, roster[index].Stats, previous.CopyInstructions(), previous.Stars, previous.Portrait);
            }
        }

        private static RosterPlayer? FindOrNull(IReadOnlyList<RosterPlayer> players, int playerId)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].PlayerId == playerId) { return players[i]; }
            }
            return null;
        }

        private int IndexOfLineup(int playerId)
        {
            for (int i = 0; i < lineup.Count; i++)
            {
                if (lineup[i].PlayerId == playerId) { return i; }
            }
            return -1;
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

        // 내 경기가 끝날 때 결과와 같이 적는다. 라운드마다 덮어쓴다: 홈이 보여 주는 건 "지난 경기" 하나다. 요약을 못 만든 경기는 null(홈 줄이 빈다)
        public void SetLastReadout(TacticsReadoutSummary? summary)
        {
            LastReadout = summary;
        }

        // 승강전 한 경기. 단계 순서는 PlayoffRules.NextStage가 정하고 여기선 쌓기만 한다
        public void AddPlayoffResult(PlayoffResult result)
        {
            playoff.Add(result);
        }

        // ── 스카우트(스펙 §4-5, 10-12). 판정은 ScoutRules, 여기는 시도 소모·기록·영입(벤치 추가)만
        public int ScoutRetriesOn(int teamId, int playerIndex)
        {
            int n = 0;
            for (int i = 0; i < scoutTargets.Count; i++)
            {
                if (scoutTargets[i].TeamId == teamId && scoutTargets[i].PlayerIndex == playerIndex) { n++; }
            }
            return n;
        }

        // 시도 1회 소모. 판정 결과(success)를 함께 적는다: 성공한 후보의 자리만 다음 후보로 넘어가서다(ScoutPool). 남은 시도가 없으면 던진다(화면이 먼저 막는다)
        public void SpendScoutAttempt(int teamId, int playerIndex, bool success)
        {
            if (RemainingScoutAttempts <= 0) { throw new InvalidOperationException("[SeasonState] 남은 스카우트 시도가 없다"); }
            ScoutAttempts++;
            scoutTargets.Add(new ScoutTarget(teamId, playerIndex, success));
        }

        public void AddExtraScoutAttempt()
        {
            ScoutExtraAttempts++;
        }

        // ── 스쿼드(스펙 §4-5 10-19): 선발 11 + 벤치 ≤ SquadRules.MaxBench. 벤치 = 라인업에 없는 로스터 선수(별도 목록 없음)
        public bool IsStarter(int playerId)
        {
            return IndexOfLineup(playerId) >= 0;
        }

        public List<RosterPlayer> Bench()
        {
            var bench = new List<RosterPlayer>();
            for (int i = 0; i < roster.Count; i++)
            {
                if (IsStarter(roster[i].PlayerId)) { continue; }
                bench.Add(roster[i]);
            }
            return bench;
        }

        public int BenchCount => roster.Count - lineup.Count;

        // 내 라인업의 어느 자리에든 설 수 있는 선수인가(선발 교체가 같은 자리라서. 후보 선정·영입 가능 판정이 같은 기준을 쓴다)
        public bool FitsLineupRole(PlayerStats scouted)
        {
            for (int i = 0; i < lineup.Count; i++)
            {
                if (PlayerTableLookup.IsSamePosition(scouted, FindRosterPlayer(lineup[i].PlayerId).Stats.RoleId)) { return true; }
            }
            return false;
        }

        // 영입 = 벤치에 추가. 대상의 역할(변형)·다이얼을 가져오고 빌드는 내 총점(300)으로 재분배한다(강화 아님). 벤치가 차 있으면 던진다(화면이 먼저 막는다)
        // portrait = 후보 때 보여 준 얼굴(PortraitRules.Of(팀, 번호)). 새 playerId로 다시 정하면 영입 순간 얼굴이 바뀐다
        public RosterPlayer AddToBench(PlayerStats scouted, int stars, int portrait)
        {
            if (!SquadRules.BenchHasRoom(BenchCount)) { throw new InvalidOperationException($"[SeasonState] 벤치가 찼다({BenchCount}/{SquadRules.MaxBench}). 먼저 방출한다"); }
            if (!FitsLineupRole(scouted)) { throw new InvalidOperationException($"[SeasonState] {scouted.RoleId} 자리가 내 라인업에 없다"); }
            PlayerStats incoming = BuildScaler.Copy(scouted);
            incoming = BuildScaler.Scale(incoming, PlayerStats.TotalPoints);
            var player = new RosterPlayer(NextPlayerId(), incoming, null, stars, portrait);
            roster.Add(player);
            return player;
        }

        // 방출 = 벤치 선수만 로스터에서 뺀다. 선발은 먼저 벤치로 내린다(SwapStarter). 코인은 호출자가 SquadRules.ReleaseCoins(Stars)로 준다
        public RosterPlayer Release(int playerId)
        {
            int index = RosterIndex(playerId);
            if (IsStarter(playerId)) { throw new InvalidOperationException($"[SeasonState] 선발 선수 {playerId}는 방출할 수 없다. 먼저 벤치로 내린다"); }
            RosterPlayer released = roster[index];
            roster.RemoveAt(index);
            return released;
        }

        // 선발 교체: 같은 자리(roleId)의 벤치 선수와 자리를 바꾼다. 자리 2쌍·편성 좌표는 슬롯에 남고 선수만 바뀐다. 개인 지시는 선수를 따라간다
        public void SwapStarter(int starterId, int benchId)
        {
            int slot = IndexOfLineup(starterId);
            if (slot < 0) { throw new InvalidOperationException($"[SeasonState] {starterId}는 선발이 아니다"); }
            if (IsStarter(benchId)) { throw new InvalidOperationException($"[SeasonState] {benchId}는 이미 선발이다"); }
            PlayerStats starter = FindRosterPlayer(starterId).Stats;
            PlayerStats bench = FindRosterPlayer(benchId).Stats;
            if (!PlayerTableLookup.IsSamePosition(bench, starter.RoleId)) { throw new InvalidOperationException($"[SeasonState] {starter.RoleId} 자리에 {bench.RoleId} 선수를 세울 수 없다"); }
            lineup[slot] = lineup[slot].WithPlayer(benchId);
        }

        // 다음 시즌: 벤치 선수를 역할·★만 들고 넘어온다. 빌드는 그 역할의 기본값(선발 이월 규칙과 같다, 스펙 §4-5 [가정] ④). 역할이 표에서 사라졌으면 그 선수는 두고 온다
        public void AdoptBench(IReadOnlyList<RosterPlayer> previousBench, IReadOnlyList<PlayerStats> table)
        {
            for (int i = 0; i < previousBench.Count; i++)
            {
                PlayerStats? variant = PlayerTableLookup.FindVariant(table, previousBench[i].Stats.VariantId);
                if (variant == null) { continue; }
                if (!SquadRules.BenchHasRoom(BenchCount)) { return; }
                if (!FitsLineupRole(variant)) { continue; }
                roster.Add(new RosterPlayer(NextPlayerId(), BuildScaler.Copy(variant), null, previousBench[i].Stars, previousBench[i].Portrait));
            }
        }

        private int NextPlayerId()
        {
            int next = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].PlayerId >= next) { next = roster[i].PlayerId + 1; }
            }
            return next;
        }

        // ── 슬롯 격자(10-25, FM26 공수 모양 2장). 좌표는 템플릿에서 파생해 LineupEntry에 캐시한다

        // 공격·수비 포메이션을 고른다. 선수를 새 템플릿의 역할군이 맞는 슬롯에 다시 앉히고(FormationRules.ReseatSlots) 자리 2쌍을 그 슬롯 값으로 채운다. 없는 id면 던지고 아무것도 안 바뀐다
        public void SetFormations(string attackFormationId, string defenseFormationId, IReadOnlyList<FormationTemplate> formations)
        {
            FormationTemplate attack = FormationRules.Find(formations, attackFormationId);
            FormationTemplate defense = FormationRules.Find(formations, defenseFormationId);
            var roleIds = new List<string>(lineup.Count);
            var attackSlots = new List<int>(lineup.Count);
            var defenseSlots = new List<int>(lineup.Count);
            for (int i = 0; i < lineup.Count; i++)
            {
                roleIds.Add(FindRosterPlayer(lineup[i].PlayerId).Stats.RoleId);
                attackSlots.Add(lineup[i].AttackSlot);
                defenseSlots.Add(lineup[i].DefenseSlot);
            }
            int[] newAttackSlots = FormationRules.ReseatSlots(roleIds, attackSlots, attack);
            int[] newDefenseSlots = FormationRules.ReseatSlots(roleIds, defenseSlots, defense);

            AttackFormationId = attack.Id;
            DefenseFormationId = defense.Id;
            for (int i = 0; i < lineup.Count; i++)
            {
                (float ax, float az) = FormationRules.Position(attack, newAttackSlots[i], defending: false);
                (float dx, float dz) = FormationRules.Position(defense, newDefenseSlots[i], defending: true);
                lineup[i] = lineup[i].WithAttack(newAttackSlots[i], ax, az).WithDefense(newDefenseSlots[i], dx, dz);
            }
        }

        // 두 선발의 슬롯을 맞바꾼다(공격 탭이면 공격 슬롯, 수비 탭이면 수비 슬롯). 공격 슬롯은 역할군이 맞아야 한다(FormationRules.CanOccupy).
        // 수비 슬롯은 수비 역할이 따로 있어 필드 선수끼리는 군을 안 본다. 단 GK 슬롯은 GK만: 수비 자리가 KeeperLineX(GK 라인)와 슛 대응의 기준이라 필드 선수가 거기 서면 안 된다(10-25 리뷰 🔴)
        public void SwapLineupSlots(int playerA, int playerB, bool defending, IReadOnlyList<FormationTemplate> formations)
        {
            int a = LineupIndexOf(playerA);
            int b = LineupIndexOf(playerB);
            if (a == b) { return; }
            string? blocker = SwapBlocker(playerA, playerB, defending, formations);
            if (blocker != null) { throw new InvalidOperationException(blocker); }
            FormationTemplate template = SwapTemplate(defending, formations);
            LineupEntry ea = lineup[a];
            LineupEntry eb = lineup[b];
            int slotA = defending ? ea.DefenseSlot : ea.AttackSlot;
            int slotB = defending ? eb.DefenseSlot : eb.AttackSlot;
            (float xa, float za) = FormationRules.Position(template, slotB, defending);
            (float xb, float zb) = FormationRules.Position(template, slotA, defending);
            lineup[a] = defending ? ea.WithDefense(slotB, xa, za) : ea.WithAttack(slotB, xa, za);
            lineup[b] = defending ? eb.WithDefense(slotA, xb, zb) : eb.WithAttack(slotA, xb, zb);
        }

        // 전술판이 끌어 놓기 전에 묻는다(안 되면 칩을 빨갛게). Swap과 같은 판정 한 곳
        public bool CanSwapLineupSlots(int playerA, int playerB, bool defending, IReadOnlyList<FormationTemplate> formations)
        {
            return SwapBlocker(playerA, playerB, defending, formations) == null;
        }

        // 못 바꾸는 이유. null이면 바꿀 수 있다
        private string? SwapBlocker(int playerA, int playerB, bool defending, IReadOnlyList<FormationTemplate> formations)
        {
            LineupEntry ea = lineup[LineupIndexOf(playerA)];
            LineupEntry eb = lineup[LineupIndexOf(playerB)];
            FormationTemplate template = SwapTemplate(defending, formations);
            FormationSlot slotA = FormationRules.SlotOf(template, defending ? ea.DefenseSlot : ea.AttackSlot);
            FormationSlot slotB = FormationRules.SlotOf(template, defending ? eb.DefenseSlot : eb.AttackSlot);
            if (defending) { return KeeperSlotsBlocker(slotA, slotB); }
            string roleA = FindRosterPlayer(playerA).Stats.RoleId;
            string roleB = FindRosterPlayer(playerB).Stats.RoleId;
            if (!FormationRules.CanOccupy(roleA, slotB)) { return $"[SeasonState] {roleA} 선수를 슬롯 {slotB.Slot}({slotB.RoleId})에 둘 수 없다: 역할군이 다르다"; }
            if (!FormationRules.CanOccupy(roleB, slotA)) { return $"[SeasonState] {roleB} 선수를 슬롯 {slotA.Slot}({slotA.RoleId})에 둘 수 없다: 역할군이 다르다"; }
            return null;
        }

        private FormationTemplate SwapTemplate(bool defending, IReadOnlyList<FormationTemplate> formations)
        {
            return FormationRules.Find(formations, defending ? DefenseFormationId : AttackFormationId);
        }

        private static string? KeeperSlotsBlocker(FormationSlot slotA, FormationSlot slotB)
        {
            bool keeperA = RoleGroups.Of(slotA.RoleId) == RoleGroup.Keeper;
            bool keeperB = RoleGroups.Of(slotB.RoleId) == RoleGroup.Keeper;
            if (keeperA == keeperB) { return null; }
            return $"[SeasonState] GK 슬롯은 GK 슬롯과만 바꿀 수 있다 ({slotA.Slot} ↔ {slotB.Slot})";
        }

        // 선발 한 명의 수비 역할. 없는 id면 던진다(잘못된 id가 세이브에 들어가면 경기 조립 때에야 터진다)
        public void SetDefenseRole(int playerId, string defenseRoleId, IReadOnlyList<DefenseRole> roles)
        {
            if (DefenseRoleParser.Find(roles, defenseRoleId) == null) { throw new InvalidOperationException($"[SeasonState] 없는 수비 역할 id {defenseRoleId}"); }
            int index = LineupIndexOf(playerId);
            lineup[index] = lineup[index].WithDefenseRole(defenseRoleId);
        }

        // ── 저장 전술(10-25). 한 장 = 공수 포메이션 + 수비 슬롯별 역할 + 팀 전술. 선수 배정은 안 담는다(SavedTactic 주석)

        // 지금 세팅을 한 장으로 뜬다. 수비 역할은 선수가 아니라 그 선수가 선 수비 슬롯에 붙인다
        public SavedTactic SnapshotTactic(string name)
        {
            var roles = new string[FormationTemplate.SlotCount];
            for (int i = 0; i < roles.Length; i++) { roles[i] = DefenseRole.DefaultId; }
            for (int i = 0; i < lineup.Count; i++)
            {
                roles[lineup[i].DefenseSlot - 1] = lineup[i].DefenseRoleId;
            }
            return new SavedTactic(name, AttackFormationId, DefenseFormationId, roles, MyPresetId, MyCustomTactics);
        }

        // 저장 전술을 입힌다: 포메이션 2장(선수는 역할군 맞는 슬롯에 다시 앉음) → 각 선발이 선 수비 슬롯의 역할 → 팀 전술(카드, 바꾼 값). 선수 배정은 그대로.
        // id 셋을 먼저 전부 확인한 뒤 바꾼다(10-26 리뷰 🟡): 중간에 던지면 포메이션만 바뀐 반쪽 상태가 남는다. 옛 세이브의 전술이 표에서 사라진 id를 들고 있을 때 닿는 경로
        public void ApplyTactic(SavedTactic tactic, IReadOnlyList<FormationTemplate> formations, IReadOnlyList<DefenseRole> roles, IReadOnlyList<TeamTactics> presets)
        {
            FormationRules.Find(formations, tactic.AttackFormationId);
            FormationRules.Find(formations, tactic.DefenseFormationId);
            SeasonRunner.FindPreset(presets, tactic.PresetId);
            for (int slot = 1; slot <= FormationTemplate.SlotCount; slot++)
            {
                if (DefenseRoleParser.Find(roles, tactic.DefenseRoleOf(slot)) == null) { throw new InvalidOperationException($"[SeasonState] 저장 전술 {tactic.Name}의 수비 역할 id {tactic.DefenseRoleOf(slot)}가 표에 없다"); }
            }
            SetFormations(tactic.AttackFormationId, tactic.DefenseFormationId, formations);
            for (int i = 0; i < lineup.Count; i++)
            {
                SetDefenseRole(lineup[i].PlayerId, tactic.DefenseRoleOf(lineup[i].DefenseSlot), roles);
            }
            SetMyPreset(tactic.PresetId, presets);
            if (tactic.CustomTactics != null) { SetMyTactics(tactic.CustomTactics, presets); }
        }

        // 선발의 공격 자리·수비 자리 거리. 전술판 경고(FormationRules.IsFarTransition)의 재료
        public float TransitionDistanceOf(int playerId)
        {
            LineupEntry e = lineup[LineupIndexOf(playerId)];
            float dx = e.AttackX - e.DefendX;
            float dz = e.AttackZ - e.DefendZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private int LineupIndexOf(int playerId)
        {
            for (int i = 0; i < lineup.Count; i++)
            {
                if (lineup[i].PlayerId == playerId) { return i; }
            }
            throw new InvalidOperationException($"[SeasonState] 라인업에 없는 선수 id {playerId}");
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
            return MyLineup(null);
        }

        // 경기 조립 재료. 수비 역할 표를 주면 압박 거리·라인 높이·이탈 반경을 그 역할로 덮는다(없으면 역할 값 + 개인 지시 그대로 = 기본 역할과 같다)
        public IReadOnlyList<LineupSlot> MyLineup(IReadOnlyList<DefenseRole>? defenseRoles)
        {
            var slots = new List<LineupSlot>(lineup.Count);
            for (int i = 0; i < lineup.Count; i++)
            {
                LineupEntry e = lineup[i];
                RosterPlayer player = FindRosterPlayer(e.PlayerId);
                PlayerStats stats = player.EffectiveStats();   // 역할 값 + 개인 지시
                DefenseRole? role = defenseRoles == null ? null : DefenseRoleParser.Find(defenseRoles, e.DefenseRoleId);
                if (role != null) { stats = role.Apply(stats); }
                slots.Add(new LineupSlot(stats, e.AttackX, e.AttackZ, e.DefendX, e.DefendZ, player.Portrait));
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
            var save = new SeasonSave { tier = Tier, seasonSeed = SeasonSeed, generatorVersion = GeneratorVersion, roundsPlayed = RoundsPlayed, scoutAttempts = ScoutAttempts, myPresetId = MyPresetId, playoff = new List<SeasonSave.PlayoffEntry>() };
            save.attackFormation = AttackFormationId;
            save.defenseFormation = DefenseFormationId;
            if (MyCustomTactics != null) { save.myTactics = SeasonSave.TacticsValues.From(MyCustomTactics); }
            save.scoutExtra = ScoutExtraAttempts;
            if (LastReadout != null) { save.lastReadout = SeasonSave.ReadoutEntry.From(LastReadout); }
            var targets = new List<SeasonSave.ScoutEntry>(scoutTargets.Count);
            for (int i = 0; i < scoutTargets.Count; i++) { targets.Add(new SeasonSave.ScoutEntry { team = scoutTargets[i].TeamId, player = scoutTargets[i].PlayerIndex, success = scoutTargets[i].Success }); }
            save.scoutTargets = targets;
            for (int i = 0; i < playoff.Count; i++)
            {
                PlayoffResult r = playoff[i];
                save.playoff.Add(new SeasonSave.PlayoffEntry { stage = (int)r.Stage, home = r.HomeTeamId, away = r.AwayTeamId, homeGoals = r.HomeGoals, awayGoals = r.AwayGoals, homePens = r.HomePenalties, awayPens = r.AwayPenalties });
            }
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
                    stars = roster[i].Stars,
                    portrait = roster[i].Portrait,
                });
            }
            for (int i = 0; i < lineup.Count; i++)
            {
                LineupEntry e = lineup[i];
                save.lineup.Add(new SeasonSave.Slot { playerId = e.PlayerId, attackSlot = e.AttackSlot, defenseSlot = e.DefenseSlot, defenseRole = e.DefenseRoleId, attackX = e.AttackX, attackZ = e.AttackZ, defendX = e.DefendX, defendZ = e.DefendZ });
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
                copy.Speed = p.build[0];
                copy.Stamina = p.build[1];
                copy.Pass = p.build[2];
                copy.Shot = p.build[3];
                copy.Tackle = p.build[4];
                copy.Positioning = p.build[5];
                copy.Reflexes = p.build[6];
                copy.Handling = p.build[7];
                copy.Diving = p.build[8];
                if (copy.BuildTotal != PlayerStats.TotalPoints) { throw new InvalidOperationException($"[SeasonState] 선수 {p.playerId} 빌드 합계가 {PlayerStats.TotalPoints}가 아니다({copy.BuildTotal}). 내 선수는 총점 고정(스펙 축: 강화 없음)"); }
                int stars = p.stars == 0 ? SquadRules.DefaultStars : p.stars;   // 옛 세이브(10-19 전)엔 없어 0 = 기본 로스터
                roster.Add(new RosterPlayer(p.playerId, copy, p.instructions, stars, p.portrait));   // 지시 칸 수·범위·★·초상 범위는 생성자가 검증한다. 초상 0(10-21 전)은 playerId로
            }
            var lineup = new List<LineupEntry>(save.lineup.Count);
            for (int i = 0; i < save.lineup.Count; i++)
            {
                SeasonSave.Slot s = save.lineup[i];
                // 10-25 전 세이브엔 슬롯이 없다(0). 그때 라인업 순서 = 4-4-2 슬롯 순서였으니 i+1로 흡수한다. 좌표는 일단 저장값을 두고 SeasonSystem.Load가 템플릿 값으로 1회 바꾼다(10-27 ④: 자유 배치로 옮긴 자리는 버린다)
                int attackSlot = s.attackSlot == 0 ? i + 1 : s.attackSlot;
                int defenseSlot = s.defenseSlot == 0 ? i + 1 : s.defenseSlot;
                if (attackSlot < 1 || attackSlot > FormationTemplate.SlotCount || defenseSlot < 1 || defenseSlot > FormationTemplate.SlotCount) { throw new InvalidOperationException($"[SeasonState] 선수 {s.playerId} 슬롯이 범위 밖이다({attackSlot}, {defenseSlot})"); }
                lineup.Add(new LineupEntry(s.playerId, attackSlot, defenseSlot, s.defenseRole ?? DefenseRole.DefaultId, s.attackX, s.attackZ, s.defendX, s.defendZ));
            }
            var results = new List<MatchResult>(save.results.Count);
            for (int i = 0; i < save.results.Count; i++)
            {
                SeasonSave.Result r = save.results[i];
                results.Add(new MatchResult(r.home, r.away, r.homeGoals, r.awayGoals));
            }
            TeamTactics? custom = save.myTactics == null ? null : save.myTactics.ToTactics(save.myPresetId ?? DefaultPresetId);   // 범위는 생성자가 검증한다
            var state = new SeasonState(save.tier, save.seasonSeed, save.generatorVersion, roster, lineup, results, save.roundsPlayed, save.scoutAttempts, save.myPresetId, custom);
            state.ScoutExtraAttempts = save.scoutExtra;
            state.AttackFormationId = string.IsNullOrEmpty(save.attackFormation) ? DefaultFormationId : save.attackFormation!;     // 옛 세이브(10-25 전)엔 없어 4-4-2
            state.DefenseFormationId = string.IsNullOrEmpty(save.defenseFormation) ? DefaultFormationId : save.defenseFormation!;
            if (save.lastReadout != null) { state.LastReadout = save.lastReadout.ToSummaryOrNull(); }   // 옛 세이브(10-15 전)엔 없어 null = 아직 치른 경기 없음
            if (save.scoutTargets != null)   // 옛 세이브(10-12 전)엔 없어 null
            {
                for (int i = 0; i < save.scoutTargets.Count; i++) { state.scoutTargets.Add(new ScoutTarget(save.scoutTargets[i].team, save.scoutTargets[i].player, save.scoutTargets[i].success)); }
            }
            if (save.playoff != null)   // 옛 세이브(10-08 전)엔 없어 null = 승강전 전
            {
                for (int i = 0; i < save.playoff.Count; i++)
                {
                    SeasonSave.PlayoffEntry e = save.playoff[i];
                    if (e.stage < (int)PlayoffStage.Semifinal || e.stage > (int)PlayoffStage.LegTwo) { throw new InvalidOperationException($"[SeasonState] 승강전 단계가 범위 밖이다({e.stage})"); }
                    state.playoff.Add(new PlayoffResult((PlayoffStage)e.stage, e.home, e.away, e.homeGoals, e.awayGoals, e.homePens, e.awayPens));
                }
            }
            return state;
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
        public string? myPresetId;   // 옛 세이브엔 없어 null → DefaultPresetId
        public TacticsValues? myTactics;   // 09-30 추가(팀 전술 설정창). 카드 값 그대로면 null. 옛 세이브엔 없어 null → 기준 카드 값
        public List<Result> results = new List<Result>();
        public List<Player> roster = new List<Player>();
        public List<Slot> lineup = new List<Slot>();
        public List<PlayoffEntry>? playoff;   // 승강전 결과. 옛 세이브엔 없어 null → 승강전 전
        public int scoutExtra;                // 코인으로 산 스카우트 시도. 옛 세이브엔 없어 0
        public List<ScoutEntry>? scoutTargets;   // 이번 시즌 시도한 (팀, 선수 번호). 옛 세이브엔 없어 null(playoff와 같은 꼴. 초기화자를 두면 Json.NET이 빈 목록을 남겨 주석과 어긋난다)
        public ReadoutEntry? lastReadout;   // 지난 내 경기 실측 요약(10-15). 옛 세이브엔 없어 null
        public string? attackFormation;      // 10-25 슬롯 격자. 옛 세이브엔 없어 null → 4-4-2
        public string? defenseFormation;

        // 실측 요약. 서드 배열 3칸·설정 3칸·횟수 3개. 길이·범위는 TacticsReadoutSummary 생성자가 본다
        [Serializable] public sealed class ReadoutEntry
        {
            public int[] passCounts = Array.Empty<int>();
            public float[] passLengths = Array.Empty<float>();
            public int[] regains = Array.Empty<int>();
            public int counters;
            public int counterShots;
            public int shots;
            public int passStyle;
            public int pressStart;
            public int counter;

            public static ReadoutEntry From(TacticsReadoutSummary summary)
            {
                return new ReadoutEntry
                {
                    passCounts = summary.CopyPassCounts(), passLengths = summary.CopyMeanPassLengths(), regains = summary.CopyRegainCounts(),
                    counters = summary.Counters, counterShots = summary.CounterShots, shots = summary.Shots,
                    passStyle = summary.SettingPassStyle, pressStart = summary.SettingPressStart, counter = summary.SettingCounter,
                };
            }

            // 깨진 값이면 null. 보조 표시값 하나 때문에 시즌 로드 전체가 실패해 새 시즌으로 초기화되면 안 된다(10-15 리뷰). 배열이 null이어도 같은 길로 간다
            public TacticsReadoutSummary? ToSummaryOrNull()
            {
                try
                {
                    return new TacticsReadoutSummary(passCounts, passLengths, regains, counters, counterShots, shots, passStyle, pressStart, counter);
                }
                catch (ArgumentException)
                {
                    return null;
                }
                catch (NullReferenceException)
                {
                    return null;
                }
            }
        }

        [Serializable] public sealed class ScoutEntry
        {
            public int team;
            public int player;
            public bool success;   // 10-19 추가. 옛 세이브(10-19 전)엔 없어 false = 그 기록으로는 후보가 안 바뀐다
        }

        [Serializable] public sealed class PlayoffEntry
        {
            public int stage;
            public int home;
            public int away;
            public int homeGoals;
            public int awayGoals;
            public int homePens;
            public int awayPens;
        }

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
            public string variantId = string.Empty;   // 역할. 전술 화면에서 같은 자리 안에서 바꾼다
            public int[] build = Array.Empty<int>();
            public int[]? instructions;               // 개인 지시 오프셋(PlayerDial 순서). 지시가 없으면 null, 옛 세이브에도 없어 null → 역할 값 그대로
            public int stars;                         // 10-19 추가. 옛 세이브엔 없어 0 → 기본 1
            public int portrait;                      // 10-21 추가. 초상 번호(PortraitRules). 옛 세이브엔 없어 0 → playerId로
        }
        [Serializable] public sealed class Slot
        {
            public int playerId;
            public float attackX;
            public float attackZ;
            public float defendX;
            public float defendZ;
            public int attackSlot;          // 10-25 슬롯 격자. 옛 세이브엔 없어 0 → 라인업 순서(i+1)
            public int defenseSlot;
            public string? defenseRole;     // 옛 세이브엔 없어 null → 기본 역할
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
