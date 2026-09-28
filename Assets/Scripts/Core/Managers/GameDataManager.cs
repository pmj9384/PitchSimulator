using UnityCommunity.UnitySingleton;
using UnityEngine;

public class GameDataManager : PersistentMonoSingleton<GameDataManager>
{
    public PlayerAccountData PlayerAccountData { get; private set; }
    public SeasonSystem Season { get; private set; }   // 4부제 시즌(스펙 §10·§11, 09-27). 로비·인게임이 둘 다 여기서 읽는다

    public override void InitializeSingleton()
    {
        base.InitializeSingleton();
        SaveLoadSystem.Instance.Load();

        PlayerAccountData = new();
        PlayerAccountData.Load(SaveLoadSystem.Instance.CurrentSaveData.playerAccountDataSave);

        Season = new();
        Season.Load(SaveLoadSystem.Instance.CurrentSaveData.seasonSave);
    }
}
