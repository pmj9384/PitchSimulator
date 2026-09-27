using UnityEngine;
using UnityEngine.UI;

public class GameOverUIElement : UIElement
{
    [SerializeField] private Button restartButton;

    public override void Initialize()
    {
        gameObject.SetActive(false);
        // 재시작 = 다음 라운드(결과 보고가 먼저 라운드를 올린다). 시즌이 끝났으면 다음 시즌 입구는 로비(PrepareNextMatch) 하나라 로비로 보낸다(09-27 리뷰 R1)
        restartButton.onClick.AddListener(OnRestart);
    }

    private void OnRestart()
    {
        if (GameDataManager.Instance.Season.IsOver)
        {
            gameManager.GoToTitle();
            return;
        }
        gameManager.RestartGame(skipReady: false);
    }

    public override void Show() => gameObject.SetActive(true);

    public override void Hide() => gameObject.SetActive(false);
}
