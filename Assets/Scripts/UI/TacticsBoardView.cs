using UnityEngine;
using UnityEngine.UI;

// 전술 화면 전체(10-27 ③b, 스펙 §8 화면 확정. 근거 실험/2026-10-27-전술화면-UI-레퍼런스.md: 필드 중심 + 옆 패널, 자리 단위와 팀 단위 설정 분리).
// 오른쪽 = 필드(공격/수비 위상 탭 + 슬롯 칩 11). 필드 위 띠의 버튼 3개([포메이션][저장 전술][팀 전술])는 각각 모달을 연다.
// 왼쪽 패널은 두 상태: 아무 칩도 안 골랐으면 명단(RosterPanel), 칩을 골랐으면 그 자리(SlotPanel). 칩 재탭·빈 곳 탭 → 명단.
// 로비 전술 화면(TacticsScreen)이 쓴다. 여기의 몫은 조각을 잇는 것뿐이다: 패널·필드·모달은 서로를 모른다. 바뀌면 다시 그리라고 전하고, 선택을 패널 상태로 옮긴다
public class TacticsBoardView : MonoBehaviour
{
    [Header("필드와 왼쪽 패널 2상태")]
    [SerializeField] private PitchView pitch;
    [SerializeField] private RosterPanel rosterPanel;
    [SerializeField] private SlotPanel slotPanel;

    [Header("모달 3개 (필드 위 띠 버튼 → 열기, 각 모달의 닫기)")]
    [SerializeField] private FormationPanel formationPanel;   // 포메이션 모달과 저장 전술 모달의 내용을 같이 든다
    [SerializeField] private TeamTacticsPanel teamPanel;
    [SerializeField] private GameObject formationModal;
    [SerializeField] private GameObject libraryModal;
    [SerializeField] private GameObject teamModal;
    [SerializeField] private Button openFormationButton;
    [SerializeField] private Button openLibraryButton;
    [SerializeField] private Button openTeamButton;
    [SerializeField] private Button closeFormationButton;
    [SerializeField] private Button closeLibraryButton;
    [SerializeField] private Button closeTeamButton;

    private bool initialized;

    // 화면을 열 때마다 부른다
    public void Refresh()
    {
        if (!initialized)
        {
            initialized = true;
            pitch.PlayerSelected += OnPlayerSelected;
            pitch.Deselected += ShowRoster;
            pitch.PhaseChanged += OnPhaseChanged;
            rosterPanel.StarterSelected += OnStarterSelected;
            rosterPanel.Changed += OnRosterChanged;
            slotPanel.Changed += OnSlotChanged;
            slotPanel.LineupChanged += OnLineupChanged;
            formationPanel.Changed += OnFormationChanged;
            teamPanel.Changed += OnTeamChanged;
            openFormationButton.onClick.AddListener(OpenFormation);
            openLibraryButton.onClick.AddListener(OpenLibrary);
            openTeamButton.onClick.AddListener(OpenTeam);
            closeFormationButton.onClick.AddListener(CloseModals);
            closeLibraryButton.onClick.AddListener(CloseModals);
            closeTeamButton.onClick.AddListener(CloseModals);
        }
        CloseModals();
        pitch.Refresh();
        ShowSidePanel();
    }

    // 고른 칩이 있으면 자리 패널, 없으면 명단
    private void ShowSidePanel()
    {
        bool hasSelection = pitch.SelectedPlayerId != PitchView.NoPlayer;
        rosterPanel.gameObject.SetActive(!hasSelection);
        slotPanel.gameObject.SetActive(hasSelection);
        if (hasSelection) { slotPanel.Show(pitch.SelectedPlayerId, pitch.Defending); }
        else { rosterPanel.Show(PitchView.NoPlayer); }
    }

    private void ShowRoster()
    {
        ShowSidePanel();
    }

    private void OnPlayerSelected(int playerId)
    {
        ShowSidePanel();
    }

    private void OnStarterSelected(int playerId)
    {
        pitch.Select(playerId);
        ShowSidePanel();
    }

    private void OnPhaseChanged()
    {
        ShowSidePanel();
    }

    // 변형·지시·수비 역할이 바뀌면 칩 글자와 어긋남 표시가 달라진다
    private void OnSlotChanged()
    {
        pitch.Refresh();
    }

    // 교체 뒤: 필드가 새 선수를 고르고 자리 패널이 그 슬롯을 보인다
    private void OnLineupChanged(int playerId)
    {
        pitch.Select(playerId);
        ShowSidePanel();
    }

    // 방출 뒤: 명단과 필드(어긋남 표시는 안 바뀌지만 같은 경로)
    private void OnRosterChanged()
    {
        pitch.Refresh();
        ShowSidePanel();
    }

    // 포메이션·저장 전술을 입히면 격자(필드)·팀 전술·자리 패널이 다 달라질 수 있다
    private void OnFormationChanged()
    {
        pitch.Refresh();
        teamPanel.Refresh();
        ShowSidePanel();
    }

    // 전진 정도·수비 라인·폭이 바뀌어도 격자는 안 움직이지만, 저장 전술 요약(카드 이름)이 달라진다
    private void OnTeamChanged()
    {
        formationPanel.Refresh();
    }

    private void OpenFormation()
    {
        CloseModals();
        formationPanel.Refresh();
        formationModal.SetActive(true);
    }

    private void OpenLibrary()
    {
        CloseModals();
        formationPanel.Refresh();
        libraryModal.SetActive(true);
    }

    private void OpenTeam()
    {
        CloseModals();
        teamPanel.Refresh();
        teamModal.SetActive(true);
    }

    private void CloseModals()
    {
        formationModal.SetActive(false);
        libraryModal.SetActive(false);
        teamModal.SetActive(false);
    }
}
