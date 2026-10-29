using UnityEngine;

public abstract class UIPopup : MonoBehaviour
{
    protected IUIManager uiManager;

    public void SetUIManager(IUIManager uiManager)
    {
        this.uiManager = uiManager;
    }

    // 열고 닫을 때 UIManager의 열린 팝업 스택에 넣고 뺀다. 안드로이드 뒤로가기가 맨 위 팝업부터 닫는다(2026-10-29 UI 키트)
    public virtual void Show()
    {
        gameObject.SetActive(true);
        uiManager?.NotifyPopupShown(this);
    }

    public virtual void Hide()
    {
        gameObject.SetActive(false);
        uiManager?.NotifyPopupHidden(this);
    }

    // Hide를 거치지 않고 꺼져도(부모가 꺼지거나 SetActive(false)) 스택에서 뺀다. 남으면 뒤로가기 한 번이 안 보이는 팝업에 쓰인다(리뷰)
    protected virtual void OnDisable()
    {
        uiManager?.NotifyPopupHidden(this);
    }
}
