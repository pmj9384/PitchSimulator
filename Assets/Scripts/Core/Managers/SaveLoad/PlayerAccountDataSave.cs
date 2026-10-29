using System;

[Serializable]
public class PlayerAccountDataSave
{
    public float bgmVolume;
    public float sfxVolume;
    public int bestScore;
    public int coins;

    // 프레임 상한. 인덱스가 아니라 fps 값을 그대로 저장한다 — 목록이 바뀌어도 이사 코드가 필요 없고,
    // 이 필드가 없던 옛 세이브는 0으로 읽혀 FrameRateSetting.Sanitize가 기본값으로 흡수한다
    public int frameRateFps;

    // 진동 꺼짐. "꺼짐"을 저장해 이 필드가 없던 옛 세이브(false)가 기본값 켜짐이 된다(10-29 템플릿 UI 키트)
    public bool vibrationOff;

    // TODO: 게임 특화 저장 필드 추가
}
