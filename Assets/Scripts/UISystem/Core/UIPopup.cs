using UnityEngine;

public abstract class UIPopup : MonoBehaviour
{
    protected IUIManager uiManager;

    public void SetUIManager(IUIManager uiManager)
    {
        this.uiManager = uiManager;
    }

    public virtual void Show() => gameObject.SetActive(true);
    public virtual void Hide() => gameObject.SetActive(false);
}
