using UnityEngine;

// 전술 화면 전체(09-30): 왼쪽 패널(팀 전술) + 오른쪽 필드(내 11명). FM·EA FC·FC 온라인 전술 화면 구조를 따랐다(09-30 유저 지시).
// 프리팹 하나를 로비 전술 화면(TacticsScreen)과 경기 직전 세팅 화면(TacticsPanelUIElement)이 같이 쓴다.
// 여기의 몫은 조각을 잇는 것뿐이다: 패널과 필드는 서로를 모르고, 전술이 바뀌면 필드를 다시 그리라고 전한다.
// 개인 전술 탭(선수 칩을 눌러 역할·개인 지시)은 다음 단계에서 이 자리에 붙는다
public class TacticsBoardView : MonoBehaviour
{
    [SerializeField] private TeamTacticsPanel teamPanel;
    [SerializeField] private PitchView pitch;

    private bool initialized;

    // 화면을 열 때마다 부른다
    public void Refresh()
    {
        if (!initialized)
        {
            initialized = true;
            teamPanel.Changed += OnTacticsChanged;
        }
        teamPanel.Refresh();
        pitch.Refresh();
    }

    // 전진 정도·폭이 바뀌면 필드의 공격 시 자리가 달라진다
    private void OnTacticsChanged()
    {
        pitch.Refresh();
    }
}
