using UnityEngine;

public abstract class UIScreen : MonoBehaviour
{
    protected IUIManager uiManager;

    public void SetUIManager(IUIManager uiManager)
    {
        this.uiManager = uiManager;
    }

    public virtual void Open() => gameObject.SetActive(true);
    public virtual void Close() => gameObject.SetActive(false);
}
