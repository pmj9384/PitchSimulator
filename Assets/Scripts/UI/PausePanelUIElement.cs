using UnityEngine;
using UnityEngine.UI;

public class PausePanelUIElement : UIElement
{
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button homeButton;

    private GameUIManager uiManager;

    public override void Initialize()
    {
        gameObject.SetActive(false);
        uiManager = gameUIManager;
        resumeButton.onClick.AddListener(() => gameManager.ResumeFromPause());
        settingsButton.onClick.AddListener(() => uiManager.ShowUIElement(UIElementEnums.SettingsPanel));
        homeButton.onClick.AddListener(() => gameManager.GoToTitle());
    }

    public override void Show() => gameObject.SetActive(true);
    public override void Hide() => gameObject.SetActive(false);
}
