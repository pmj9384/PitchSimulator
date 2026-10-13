using UnityEngine;
using UnityEngine.UI;

// 로비 첫 화면. [다음 경기]는 인게임이 아니라 매치데이 허브(MatchdayScreen)를 연다(스펙 §2-1, 10-13).
// 경기 전에 만지는 것(전술·스카우트·상대 확인)이 전부 허브에 있고, 인게임은 허브의 [경기 시작]만 연다
public class LobbyScreen : UIScreen
{
    [SerializeField] private Button playButton;
    [SerializeField] private Button optionsButton;

    public override void Open()
    {
        base.Open();
        playButton.onClick.RemoveAllListeners();
        optionsButton.onClick.RemoveAllListeners();
        playButton.onClick.AddListener(OnPlay);
        optionsButton.onClick.AddListener(OnOptions);
    }

    private void OnPlay()
    {
        uiManager.OpenScreen<MatchdayScreen>();
    }

    private void OnOptions()
    {
        uiManager.ShowPopup<OutGameSettingsPanel>();
    }
}
