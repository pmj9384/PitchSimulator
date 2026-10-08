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
// 보기 전용이다. 끌어서 옮기는 배치는 배치 UI 몫(스펙 §8)
public class PitchView : MonoBehaviour
{
    private const int AttackPhase = 0;
    private const int DefendPhase = 1;
    private const int MyAttackSign = +1;   // 내 팀은 시뮬 팀 0(+X 공격). 필드 그림은 우리 골이 왼쪽

    [SerializeField] private PitchChip[] chips;   // 11개 고정(FormationTemplate.SlotCount)
    [SerializeField] private TabGroup phaseTabs;  // 0 = 공격 시, 1 = 수비 시

    private bool initialized;

    // 화면을 열 때와 전술이 바뀔 때 부른다
    public void Refresh()
    {
        EnsureInitialized();
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
    }

    private void OnPhaseSelected(int _)
    {
        PlaceChips();
    }

    private void PlaceChips()
    {
        SeasonState state = GameDataManager.Instance.Season.State;
        TeamTactics tactics = state.MyTactics(TeamTacticsRepository.All);
        IReadOnlyList<LineupEntry> lineup = state.Lineup;
        bool defending = phaseTabs.SelectedIndex == DefendPhase;

        for (int i = 0; i < chips.Length; i++)
        {
            bool hasPlayer = i < lineup.Count;
            chips[i].gameObject.SetActive(hasPlayer);
            if (!hasPlayer) { continue; }

            LineupEntry entry = lineup[i];
            PlayerStats stats = state.FindRosterPlayer(entry.PlayerId).Stats;
            (float x, float z) home = defending ? DefendHome(entry, stats) : AttackHome(entry, stats, tactics);
            chips[i].Show(stats.RoleId, stats.DisplayName, ToAnchor(home.x, home.z));
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
        float u = (x + FieldBounds.HalfLength) / (2f * FieldBounds.HalfLength);
        float v = (z + FieldBounds.HalfWidth) / (2f * FieldBounds.HalfWidth);
        return new Vector2(u, v);
    }
}
