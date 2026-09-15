using UnityEngine;

public class UIElement : MonoBehaviour
{
    protected GameUIManager gameUIManager;
    protected GameManager gameManager;

    public void SetUIManager(GameManager gameManager, GameUIManager uIManager)
    {
        gameUIManager = uIManager;
        this.gameManager = gameManager;
    }

    public virtual void Initialize() { }

    public virtual void Show() { }

    public virtual void Hide() { }
}