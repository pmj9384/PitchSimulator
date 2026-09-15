using UnityEngine;

public abstract class UIWidget : MonoBehaviour
{
    private bool started;

    protected virtual void Start()
    {
        started = true;
        Subscribe();
    }

    protected virtual void OnEnable()
    {
        if (started) Subscribe();
    }

    protected virtual void OnDisable() => Unsubscribe();
    protected abstract void Subscribe();
    protected abstract void Unsubscribe();
    public abstract void Refresh();
}
