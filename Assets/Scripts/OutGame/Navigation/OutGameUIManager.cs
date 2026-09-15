public class OutGameUIManager : UIManager, IManager
{
    private OutGameManager outGameManager;

    public void SetOutGameManager(OutGameManager outGameManager)
    {
        this.outGameManager = outGameManager;
    }

    public void Initialize() => Setup();

    public void Clear() { }
}
