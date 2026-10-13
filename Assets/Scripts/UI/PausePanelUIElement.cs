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
        resumeButton.onClick.AddListener(OnResumeClicked);
        settingsButton.onClick.AddListener(OnSettingsClicked);
        homeButton.onClick.AddListener(OnHomeClicked);
    }

    private void OnResumeClicked()
    {
        gameManager.ResumeFromPause();
    }

    private void OnSettingsClicked()
    {
        uiManager.ShowUIElement(UIElementEnums.SettingsPanel);
    }

    private void OnHomeClicked()
    {
        gameManager.GoToTitle();
    }

    public override void Show() => gameObject.SetActive(true);
    public override void Hide() => gameObject.SetActive(false);
}
