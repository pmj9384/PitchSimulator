using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class LobbyScreen : UIScreen
{
    [SerializeField] private Button playButton;
    [SerializeField] private Button optionsButton;

    public override void Open()
    {
        base.Open();
        playButton.onClick.RemoveAllListeners();
        optionsButton.onClick.RemoveAllListeners();

        playButton.onClick.AddListener(() =>
        {
            // 스태미나 없음(제거). SkipTitle도 안 켠다 — 이 게임은 GameReady가 선수 세팅 국면이라 건너뛰면 안 된다
            // 끝난 시즌의 다음 시즌은 경기가 차려질 때(MatchManager.ResetMatch) 연다. 입구마다 부르면 입구가 늘 때 빠진다(09-29 발견 A)
            SaveLoadSystem.Instance.Save();
            SceneManager.LoadScene("InGameScene");
        });
        optionsButton.onClick.AddListener(() => uiManager.ShowPopup<OutGameSettingsPanel>());
    }
}
