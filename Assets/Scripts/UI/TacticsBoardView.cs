using UnityEngine;

// 전술 화면 전체: 위 탭 [팀 전술][개인 전술] + 왼쪽 패널 + 오른쪽 필드(내 11명). FM·EA FC·FC 온라인 전술 화면 구조를 따랐다(09-30 유저 지시).
// 로비 전술 화면(TacticsScreen)이 쓴다. 10-13까지는 경기 직전 세팅 화면도 같은 프리팹을 썼는데, 세팅을 로비 매치데이 허브로 모으며(스펙 §2-1) 그 화면을 뺐다.
// 여기의 몫은 조각을 잇는 것뿐이다: 두 패널과 필드는 서로를 모른다. 전술이나 개인 지시가 바뀌면 필드를 다시 그리라고 전하고,
// 필드에서 선수를 고르면 개인 전술 패널을 그 선수로 바꾸고 그 탭을 연다
public class TacticsBoardView : MonoBehaviour
{
    private const int PlayerTab = 1;   // tabs의 순서: 0 = 팀 전술, 1 = 개인 전술

    [SerializeField] private TabGroup tabs;
    [SerializeField] private TeamTacticsPanel teamPanel;
    [SerializeField] private PlayerTacticsPanel playerPanel;
    [SerializeField] private PitchView pitch;

    private bool initialized;

    // 화면을 열 때마다 부른다
    public void Refresh()
    {
        if (!initialized)
        {
            initialized = true;
            tabs.Initialize();
            teamPanel.Changed += OnTacticsChanged;
            playerPanel.Changed += OnTacticsChanged;
            pitch.PlayerSelected += OnPlayerSelected;
        }
        teamPanel.Refresh();
        pitch.Refresh();
        if (pitch.SelectedPlayerId != PitchView.NoPlayer) { playerPanel.Show(pitch.SelectedPlayerId); }
    }

    // 전진 정도·폭(팀 전술)이나 전진 폭·측면 쏠림·라인 높이·역할(개인 전술)이 바뀌면 필드의 자리와 이름이 달라진다
    private void OnTacticsChanged()
    {
        pitch.Refresh();
    }

    private void OnPlayerSelected(int playerId)
    {
        playerPanel.Show(playerId);
        tabs.Select(PlayerTab);
    }
}
