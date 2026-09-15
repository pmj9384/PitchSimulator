using UnityEngine;
using UnityEngine.UI;

public class BottomMenuPanel : UIPanel
{
    [SerializeField] private Button[] buttons;
    [SerializeField] private UIScreen[] targetScreen;

    public override void SetUIManager(IUIManager uiManager)
    {
        base.SetUIManager(uiManager);
        for (int i = 0; i < buttons.Length; i++)
        {
            var screen = targetScreen[i];
            buttons[i].onClick.AddListener(() => uiManager.OpenScreen(screen));
        }

    }
}
