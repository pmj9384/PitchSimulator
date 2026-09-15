using Newtonsoft.Json;
using System;
using System.IO;
using UnityCommunity.UnitySingleton;
using UnityEngine;
using SaveDataVC = SaveDataV1;

public class SaveLoadSystem : PersistentMonoSingleton<SaveLoadSystem>
{
    public static int SaveDataVersion { get; private set; } = 1;

    public SaveDataVC CurrentSaveData { get; set; }

    public static string CurrentSaveFileName => "SaveFile.json";

    public static string SavePathDirectory => $"{Application.persistentDataPath}/Save";

    public Action onApplicationQuitSave;

    public override void InitializeSingleton()
    {
        base.InitializeSingleton();
        CurrentSaveData = new();
    }

    // TypeNameHandling.All 제거 (TongTongDefence 검수 v6 역이식): $type으로 클래스명이 파일에 박제되면
    // 리네임=옛 세이브 로드 실패. 구체 타입 역직렬화로 대체 — 버전 판별은 Version 필드 몫
    private static JsonSerializerSettings settings = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
    };

    public void Save()
    {
        OnApplicationQuitSave();

        if (!Directory.Exists(SavePathDirectory))
            Directory.CreateDirectory(SavePathDirectory);

        var path = Path.Combine(SavePathDirectory, CurrentSaveFileName);
        var json = JsonConvert.SerializeObject(CurrentSaveData, settings);
        // 원자적 저장: temp에 다 쓰고 rename — 저장 도중 프로세스 킬에도 반파손 파일이 생기지 않는다
        var tmpPath = path + ".tmp";
        File.WriteAllText(tmpPath, json);
        if (File.Exists(path)) File.Replace(tmpPath, path, null);
        else File.Move(tmpPath, path);
    }

    public void Load()
    {
        var path = Path.Combine(SavePathDirectory, CurrentSaveFileName);

        if (!File.Exists(path))
        {
            Debug.Log($"Save file not found, using defaults.");
            return;
        }

        // [AnimalBreakOut] 튜토리얼 완료 여부로 세이브 파일 교체하는 로직
        //else if (PlayerPrefs.GetInt("ECET_CLEAR_ALL") != 1)
        //{
        //    path = "Tables/defaultSave";
        //    var textAsset = Resources.Load<TextAsset>(path);
        //    json = textAsset.text;
        //}

        // 파손 파일 방어: 로드 실패가 부팅 크래시(영구 진입 불가)로 번지지 않게 기본값 폴백.
        // 파일은 지우지 않고 다음 Save 때 정상본으로 덮인다
        try
        {
            var json = File.ReadAllText(path);
            var saveData = JsonConvert.DeserializeObject<SaveDataVC>(json, settings);
            if (saveData == null) throw new JsonException("역직렬화 결과 null");

            while (saveData.Version < SaveDataVersion)
            {
                int before = saveData.Version;
                saveData = (SaveDataVC)saveData.VersionUp();
                if (saveData.Version <= before)
                    throw new InvalidOperationException($"VersionUp이 버전을 올리지 않음 (v{before})");
            }

            CurrentSaveData = saveData;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveLoad] 세이브 로드 실패 — 기본값으로 시작: {e.Message}");
            CurrentSaveData = new SaveDataVC();
        }
    }

    private void OnApplicationQuitSave()
    {
        onApplicationQuitSave?.Invoke();
    }

    public void RegisterOnSaveAction(ISaveLoad target)
    {
        onApplicationQuitSave += target.Save;
    }

    private void OnApplicationQuit()
    {
        Save();
    }

#if !UNITY_EDITOR
    private void OnApplicationPause(bool pause)
    {
        if (pause)
            Save();
    }
#endif
}
