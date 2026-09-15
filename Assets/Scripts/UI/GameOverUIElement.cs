using UnityEngine;
using UnityEngine.UI;

public class GameOverUIElement : UIElement
{
    [SerializeField] private Button restartButton;

    public override void Initialize()
    {
        gameObject.SetActive(false);
        restartButton.onClick.AddListener(() => gameManager.RestartGame(skipReady: false));
    }

    public override void Show() => gameObject.SetActive(true);

    public override void Hide() => gameObject.SetActive(false);
}
