using UnityCommunity.UnitySingleton;
using UnityEngine;

public class GameDataManager : PersistentMonoSingleton<GameDataManager>
{
    public PlayerAccountData PlayerAccountData { get; private set; }

    // TODO: 게임 특화 시스템 추가
    // public MyGameSystem MySystem { get; private set; }

    public override void InitializeSingleton()
    {
        base.InitializeSingleton();
        SaveLoadSystem.Instance.Load();

        PlayerAccountData = new();
        PlayerAccountData.Load(SaveLoadSystem.Instance.CurrentSaveData.playerAccountDataSave);

        // TODO: 게임 특화 시스템 초기화
        // MySystem = new(); MySystem.Load(...);
    }
}
