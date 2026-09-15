using System;
using Newtonsoft.Json;

public abstract class SaveData
{
    // [JsonProperty]: setter가 protected면 Json.NET이 파일의 Version을 복원하지 않아 마이그레이션 판별 불가
    [JsonProperty] public int Version { get; protected set; }
    public abstract SaveData VersionUp();
}

public class SaveDataV1 : SaveData
{
    public PlayerAccountDataSave playerAccountDataSave;
    // 스태미나·스킨 모듈은 이 게임에서 제거(2026-09-15) — 진행도·선수 세팅 저장은 게임 전용 세이브로 추가한다

    // [AnimalBreakOut] 게임 전용 시스템
    //public GoldAnimalTokenKeySystemSave goldAnimalTokenKeySystemSave;
    //public PlayerLevelSystemSave playerLevelSystemSave;
    //public AnimalUserDataListSave animalUserDataTableSave;

    public DateTime saveTime = DateTime.Now;

    public SaveDataV1()
    {
        Version = 1;
    }

    public override SaveData VersionUp()
    {
        // 빈 객체를 조용히 반환하면 V2 도입 때 데이터 전멸+무한루프 — 이사 코드 구현을 강제한다
        throw new NotImplementedException(
            "SaveDataV2 도입 시 구현: V1 필드를 V2로 이사시켜 반환할 것 (새 빈 객체 반환 금지)");
    }
}
