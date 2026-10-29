using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Tactics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 팀 전술 모달의 미니 피치(10-29 ③d, FM26 Visualiser. 10-27 유저 Play "팀 전술을 바꿀 때마다 선수가 어떻게 움직이는지 보여 주는 게 좋겠다").
// 공 칸 9개(서드 3 × 좌·중·우) 중 하나를 누르면 11명이 서는 자리를 점으로 보인다. 공격/수비는 모달의 국면 탭(TeamTacticsPhase)을 따라간다(③d-2: 자체 탭은 뺐다. 탭이 둘이면 유저가 "공수 UI가 합쳐졌다"로 읽었다).
// 자리는 경기 트리와 같은 함수(TacticsPreview → PositionRules.AttackStand·DefendStand)라 보이는 것 = 실제. 압박·패스·온사이드 같은 행동은 안 보인다(안내 글자).
// 전환 탭의 항목(역습·역압박·GK 배급)은 자리에 영향이 없어 공격 자리를 그대로 두고 안내 글자만 바꾼다.
// 여기는 눌린 칸·국면을 기억하고 점 그림(PitchDots)에 좌표를 넘기는 것뿐이다. 값은 시즌(SeasonState.MyLineup·MyTactics)에서 읽고, 팀 전술 패널이 바뀌면 보드(TacticsBoardView)가 Refresh를 부른다
public class TacticsMiniPitch : MonoBehaviour
{
    private const int CenterCell = 4;    // 처음 보이는 칸 = 중원 중앙(열 1, 행 1)
    private const string PositionNote = "공이 있는 칸을 누르면 11명이 서는 자리 · 압박·패스·오프사이드는 경기에서 정해진다";
    private const string TransitionNote = "전환 항목(역습·역압박·GK 배급)은 자리에 안 보인다 · 공격할 때 자리를 그대로 둔다";

    private static readonly Color SelectedCellColor = new Color(TacticsColors.Highlight.r, TacticsColors.Highlight.g, TacticsColors.Highlight.b, 0.35f);   // 고른 공 칸. 전술 화면 강조색을 투명하게
    private static readonly Color CellColor = new Color(1f, 1f, 1f, 0.06f);                 // 안 고른 칸. 격자가 희미하게 보일 만큼만

    [SerializeField] private Button[] cellButtons;   // 9개. index = 열 × 3 + 행(TacticsPreview.BallCell의 column·row). 열 = 우리 진영 → 상대 진영, 행 = 아래(−z) → 위(+z)
    [SerializeField] private Image[] cellImages;     // cellButtons와 같은 순서
    [SerializeField] private PitchDots dots;         // 점 11 + 그 탭 포메이션의 슬롯 자리 이름(주 필드 칩과 같은 출처)
    [SerializeField] private TMP_Text noteText;

    private int selectedCell = CenterCell;
    private TeamTacticsPhase phase = TeamTacticsPhase.Attack;
    private bool initialized;

    private static SeasonSystem Season => GameDataManager.Instance.Season;

    // 모달을 열 때와 국면 탭을 바꿀 때. 어느 자리(공격/수비)를 보일지 받는다
    public void Show(TeamTacticsPhase newPhase)
    {
        phase = newPhase;
        Refresh();
    }

    // 팀 전술 값이 바뀔 때. 국면·칸은 그대로 두고 자리만 다시 계산한다
    public void Refresh()
    {
        EnsureInitialized();
        ColorCells();
        PlaceDots();
    }

    private void EnsureInitialized()
    {
        if (initialized) { return; }
        initialized = true;

        if (cellButtons.Length != TacticsPreview.Cells * TacticsPreview.Cells || cellImages.Length != cellButtons.Length)
        {
            Debug.LogError($"[TacticsMiniPitch] 공 칸 버튼 {cellButtons.Length}·이미지 {cellImages.Length}개 ≠ {TacticsPreview.Cells * TacticsPreview.Cells}. 프리팹 배선을 확인한다");
        }
        for (int i = 0; i < cellButtons.Length; i++)
        {
            int index = i;   // 클로저가 루프 변수 대신 이 칸 번호를 잡게 한다
            cellButtons[i].onClick.AddListener(() => OnCellClicked(index));
        }
    }

    private void OnCellClicked(int index)
    {
        selectedCell = index;
        ColorCells();
        PlaceDots();
    }

    private void ColorCells()
    {
        for (int i = 0; i < cellImages.Length; i++)
        {
            cellImages[i].color = i == selectedCell ? SelectedCellColor : CellColor;
        }
    }

    // 고른 칸의 공 좌표로 11명 자리를 계산해 점 그림에 넘긴다. 점 이름은 그 국면 포메이션의 슬롯 자리(선수 값 Stats.RoleId는 공격 슬롯 변형의 자리라 수비 탭에선 어긋난다. 리뷰)
    private void PlaceDots()
    {
        bool defending = phase == TeamTacticsPhase.Defense;
        SeasonState state = Season.State;
        IReadOnlyList<LineupSlot> lineup = state.MyLineup(PlayerTableRepository.All, DefenseRoleRepository.All);
        IReadOnlyList<LineupEntry> entries = state.Lineup;
        TeamTactics tactics = state.MyTactics(TeamTacticsRepository.All);
        FormationTemplate template = FormationRules.Find(FormationTemplateRepository.All, defending ? state.DefenseFormationId : state.AttackFormationId);
        (float x, float z) ball = TacticsPreview.BallCell(selectedCell / TacticsPreview.Cells, selectedCell % TacticsPreview.Cells);
        (float x, float z)[] positions = TacticsPreview.Positions(lineup, tactics, ball.x, ball.z, defending);

        var names = new string[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            int slot = defending ? entries[i].DefenseSlot : entries[i].AttackSlot;
            names[i] = FormationRules.SlotOf(template, slot).RoleId;
        }
        dots.Show(positions, names);
        noteText.text = phase == TeamTacticsPhase.Transition ? TransitionNote : PositionNote;
    }
}
