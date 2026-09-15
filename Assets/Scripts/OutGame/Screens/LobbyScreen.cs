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
            SaveLoadSystem.Instance.Save();
            SceneManager.LoadScene("InGameScene");
        });
        optionsButton.onClick.AddListener(() => uiManager.ShowPopup<OutGameSettingsPanel>());
    }
}
