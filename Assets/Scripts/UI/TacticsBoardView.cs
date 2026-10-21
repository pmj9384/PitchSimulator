using UnityEngine;

// 전술 화면 전체: 위 탭 [팀 전술][개인 전술][스쿼드] + 왼쪽 패널 + 오른쪽 필드(선발 11명). FM·EA FC·FC 온라인 전술 화면 구조를 따랐다(09-30 유저 지시).
// [팀 전술](10-21, 목업 4번 보드)은 오른쪽이 필드가 아니라 서드 표라 그 탭에서만 필드를 숨긴다. 844×390 가로 폰에 입구·표·필드 세 열이 안 들어간다.
// [스쿼드](10-19)는 벤치 ↔ 선발 교체와 방출. 필드에서 고른 선발이 교체 대상이다
// 로비 전술 화면(TacticsScreen)이 쓴다. 10-13까지는 경기 직전 세팅 화면도 같은 프리팹을 썼는데, 세팅을 로비 매치데이 허브로 모으며(스펙 §2-1) 그 화면을 뺐다.
// 여기의 몫은 조각을 잇는 것뿐이다: 세 패널과 필드는 서로를 모른다. 전술이나 개인 지시가 바뀌면 필드를 다시 그리라고 전하고,
// 필드에서 선수를 고르면 개인 전술 패널을 그 선수로 바꾸고 그 탭을 연다
public class TacticsBoardView : MonoBehaviour
{
    private const int TeamTab = 0;   // tabs의 순서: 0 = 팀 전술, 1 = 개인 전술, 2 = 스쿼드
    private const int PlayerTab = 1;
    private const int SquadTab = 2;

    [SerializeField] private TabGroup tabs;
    [SerializeField] private TeamTacticsPanel teamPanel;
    [SerializeField] private PlayerTacticsPanel playerPanel;
    [SerializeField] private SquadPanel squadPanel;
    [SerializeField] private PitchView pitch;

    private bool initialized;

    // 화면을 열 때마다 부른다
    public void Refresh()
    {
        if (!initialized)
        {
            initialized = true;
            tabs.Initialize();
            tabs.Selected += ShowPitch;
            teamPanel.Changed += OnTacticsChanged;
            playerPanel.Changed += OnTacticsChanged;
            squadPanel.Changed += OnTacticsChanged;
            squadPanel.StarterSwapped += OnStarterSwapped;
            pitch.PlayerSelected += OnPlayerSelected;
        }
        teamPanel.Refresh();
        ShowPitch(tabs.SelectedIndex);
        pitch.Refresh();
        if (pitch.SelectedPlayerId != PitchView.NoPlayer) { playerPanel.Show(pitch.SelectedPlayerId); }
        squadPanel.Show(pitch.SelectedPlayerId);
    }

    // 전진 정도·폭(팀 전술)이나 전진 폭·측면 쏠림·라인 높이·역할(개인 전술)이 바뀌면 필드의 자리와 이름이 달라진다
    private void OnTacticsChanged()
    {
        pitch.Refresh();
    }

    // 코드로 탭을 고를 때(Select)는 TabGroup이 알리지 않는다. 지금은 필드를 눌러 개인 탭으로 가는 길뿐이라 필드가 이미 보이는 상태다
    private void ShowPitch(int tabIndex)
    {
        pitch.gameObject.SetActive(tabIndex != TeamTab);
    }

    private void OnPlayerSelected(int playerId)
    {
        playerPanel.Show(playerId);
        squadPanel.Show(playerId);
        if (tabs.SelectedIndex != SquadTab) { tabs.Select(PlayerTab); }   // 스쿼드 탭에서 선발을 고르는 중이면 탭을 안 바꾼다
    }

    // 교체로 들어온 선수를 필드와 개인 전술 패널이 같이 고른다
    private void OnStarterSwapped(int playerId)
    {
        pitch.Select(playerId);
        playerPanel.Show(playerId);
    }
}
