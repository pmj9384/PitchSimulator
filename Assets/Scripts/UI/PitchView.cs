using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Placement;
using Game.Core.Tactics;
using UnityEngine;

// 전술 화면의 필드(09-30, FM·EA FC 전술 화면이 화면 절반을 필드로 쓰는 것을 따름). 내 11명을 칩으로 세운다.
// 공격 시 / 수비 시 자리를 토글로 본다(스펙 §4-3 자리 2쌍). 자리는 경기가 쓰는 식 그대로 계산한다(PositionRules.AttackHome·DefendHome):
// 편성 자리 + 팀 전진 정도 + 개인 전진 폭·측면 쏠림·라인 높이. 그래서 "얼마나 앞으로" 슬라이더나 폭을 바꾸면 칩이 같이 움직인다(읽히는 전술).
// 공 위치에 따라 달라지는 값은 공이 필드 한가운데 있을 때로 고정해 보여 준다: 서드별 폭은 중원 값을 쓰고, 공 지향 슬라이드(SlideTowardBall)는 공이 (0, 0)이면 0이라 부르지 않는다.
// 칩을 누르면 그 선수를 고른다(09-30 개인 전술: 고른 선수의 역할·개인 지시를 옆 패널이 보여 준다). 자리는 개인 지시까지 얹은 값으로 그린다.
// 배치(10-08, 스펙 §8 10-08 구현 설계): 칩을 끌어 놓으면 그 자리에서 전진·폭·개인 다이얼 오프셋을 빼 편성 자리로 저장한다. 공격 시/수비 시 탭이 "같은 선수를 두 번 놓는" 토글이다.
// 내 진영 절반 밖이거나 다른 칩과 1.2m 안이면 칩을 원래 자리로 되돌리고 잠깐 빨갛게 한다
public class PitchView : MonoBehaviour
{
    private const int AttackPhase = 0;
    private const int DefendPhase = 1;
    private const int MyAttackSign = +1;   // 내 팀은 시뮬 팀 0(+X 공격). 필드 그림은 우리 골이 왼쪽
    public const int NoPlayer = -1;

    [SerializeField] private PitchChip[] chips;   // 11개 고정(FormationTemplate.SlotCount)
    [SerializeField] private TabGroup phaseTabs;  // 0 = 공격 시, 1 = 수비 시

    // 칩을 눌러 선수를 고를 때(playerId). 전술 화면(TacticsBoardView)이 개인 전술 패널을 그 선수로 바꾼다
    public event Action<int> PlayerSelected;

    public int SelectedPlayerId { get; private set; } = NoPlayer;

    private bool initialized;

    private static SeasonState State => GameDataManager.Instance.Season.State;

    // 화면을 열 때와 전술이 바뀔 때 부른다
    public void Refresh()
    {
        EnsureInitialized();
        KeepSelectionInLineup();
        PlaceChips();
    }

    private void EnsureInitialized()
    {
        if (initialized) { return; }
        initialized = true;

        if (chips.Length != FormationTemplate.SlotCount)
        {
            Debug.LogError($"[PitchView] 칩 {chips.Length}개 ≠ 라인업 {FormationTemplate.SlotCount}명. 프리팹 배선을 확인한다");
        }
        phaseTabs.Initialize();
        phaseTabs.Selected += OnPhaseSelected;
        for (int i = 0; i < chips.Length; i++)
        {
            chips[i].Initialize();
            chips[i].Clicked += OnChipClicked;
            chips[i].Dropped += OnChipDropped;
        }
    }

    // 칩을 놓은 자리(필드 로컬 좌표) → 필드 좌표 → 편성 자리 → 판정 → 저장. 안 되면 되돌린다.
    // 편성 자리는 "놓은 자리 − (보이는 자리 − 편성 자리)"인데 측면 쏠림이 편성 z의 부호를 따라서(PositionRules.AttackHome), z = 0 선을 건너 놓으면 한 번에 안 풀린다.
    // 그래서 몇 번 되풀이해 다시 그린 자리가 놓은 자리와 같아질 때까지 맞추고, 끝내 안 맞으면(필드 밖으로 잘린 경우) 거부한다(10-08 리뷰)
    private void OnChipDropped(PitchChip chip, Vector2 local)
    {
        try
        {
            TryMoveChip(chip, local);
        }
        finally
        {
            PlaceChips();   // 성공이든 거부든 예외든 칩은 저장된 자리에 선다
        }
    }

    private void TryMoveChip(PitchChip chip, Vector2 local)
    {
        int index = Array.IndexOf(chips, chip);
        SeasonState state = State;
        LineupEntry entry = state.Lineup[index];
        bool defending = phaseTabs.SelectedIndex == DefendPhase;

        var field = chip.transform.parent as RectTransform;
        if (field == null) { return; }
        (float x, float z) dropped = FromLocal(field, local);
        if (!TryResolveBase(state, entry, defending, dropped, out (float x, float z) basePos))
        {
            chip.FlashRejected();
            return;
        }

        PlacementVerdict verdict = state.EvaluateLineupSlot(entry.PlayerId, defending, basePos.x, basePos.z);
        if (verdict != PlacementVerdict.Ok)
        {
            chip.FlashRejected();
            return;
        }
        GameDataManager.Instance.Season.MoveLineupSlot(entry.PlayerId, defending, basePos.x, basePos.z);
    }

    private const int ResolveIterations = 3;
    private const float ResolveTolerance = 0.05f;   // m. 이 안이면 "놓은 자리에 선다"로 본다

    private static bool TryResolveBase(SeasonState state, LineupEntry entry, bool defending, (float x, float z) dropped, out (float x, float z) basePos)
    {
        PlayerStats stats = state.FindRosterPlayer(entry.PlayerId).EffectiveStats();
        TeamTactics tactics = state.MyTactics(TeamTacticsRepository.All);
        float bx = defending ? entry.DefendX : entry.AttackX;
        float bz = defending ? entry.DefendZ : entry.AttackZ;
        for (int i = 0; i < ResolveIterations; i++)
        {
            (float x, float z) shown = Home(bx, bz, stats, tactics, defending);
            (bx, bz) = FieldBounds.BaseFromShown(dropped.x, dropped.z, shown.x, shown.z, bx, bz);
        }
        basePos = (bx, bz);
        (float x, float z) check = Home(bx, bz, stats, tactics, defending);
        float dx = check.x - dropped.x;
        float dz = check.z - dropped.z;
        return dx * dx + dz * dz <= ResolveTolerance * ResolveTolerance;
    }

    private static (float x, float z) Home(float baseX, float baseZ, PlayerStats stats, TeamTactics tactics, bool defending)
    {
        if (defending) { return PositionRules.DefendHome(baseX, baseZ, MyAttackSign, stats.LineHeight); }
        return PositionRules.AttackHome(baseX, baseZ, MyAttackSign, tactics.Mentality, stats.PushUp, tactics.Width[(int)Third.Middle], stats.Width);
    }

    // 필드 그림 로컬 좌표 → 필드 좌표(ToAnchor의 역. 변환식은 FieldBounds가 들고 테스트가 왕복을 잠근다)
    private static (float x, float z) FromLocal(RectTransform field, Vector2 local)
    {
        Rect r = field.rect;
        float u = (local.x - r.xMin) / r.width;
        float v = (local.y - r.yMin) / r.height;
        return FieldBounds.FromUnit(u, v);
    }

    // 고른 선수가 라인업에 없으면(화면을 처음 열 때, 시즌이 바뀌어 선수 id가 달라졌을 때) 첫 선수를 고른다
    private void KeepSelectionInLineup()
    {
        IReadOnlyList<LineupEntry> lineup = State.Lineup;
        for (int i = 0; i < lineup.Count; i++)
        {
            if (lineup[i].PlayerId == SelectedPlayerId) { return; }
        }
        SelectedPlayerId = lineup.Count > 0 ? lineup[0].PlayerId : NoPlayer;
    }

    private void OnChipClicked(PitchChip chip)
    {
        int index = Array.IndexOf(chips, chip);
        SelectedPlayerId = State.Lineup[index].PlayerId;
        PlaceChips();
        PlayerSelected?.Invoke(SelectedPlayerId);
    }

    private void OnPhaseSelected(int _)
    {
        PlaceChips();
    }

    private void PlaceChips()
    {
        SeasonState state = State;
        TeamTactics tactics = state.MyTactics(TeamTacticsRepository.All);
        IReadOnlyList<LineupEntry> lineup = state.Lineup;
        bool defending = phaseTabs.SelectedIndex == DefendPhase;

        for (int i = 0; i < chips.Length; i++)
        {
            bool hasPlayer = i < lineup.Count;
            chips[i].gameObject.SetActive(hasPlayer);
            if (!hasPlayer) { continue; }

            LineupEntry entry = lineup[i];
            PlayerStats stats = state.FindRosterPlayer(entry.PlayerId).EffectiveStats();   // 역할 값 + 개인 지시. 경기에 들어가는 값과 같다
            (float x, float z) home = defending ? DefendHome(entry, stats) : AttackHome(entry, stats, tactics);
            chips[i].Show(stats.RoleId, stats.DisplayName, ToAnchor(home.x, home.z), entry.PlayerId == SelectedPlayerId);
        }
    }

    private static (float x, float z) AttackHome(LineupEntry entry, PlayerStats stats, TeamTactics tactics)
    {
        int widthLevel = tactics.Width[(int)Third.Middle];
        return PositionRules.AttackHome(entry.AttackX, entry.AttackZ, MyAttackSign, tactics.Mentality, stats.PushUp, widthLevel, stats.Width);
    }

    private static (float x, float z) DefendHome(LineupEntry entry, PlayerStats stats)
    {
        return PositionRules.DefendHome(entry.DefendX, entry.DefendZ, MyAttackSign, stats.LineHeight);
    }

    // 필드 좌표(중앙 원점, +X가 상대 골, 105 × 68m) → 필드 그림 안 0~1
    private static Vector2 ToAnchor(float x, float z)
    {
        (float u, float v) unit = FieldBounds.ToUnit(x, z);
        return new Vector2(unit.u, unit.v);
    }
}
