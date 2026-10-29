using UnityEngine;

// 진동 켜기/끄기(2026-10-29 UI 키트 설정창 "기타" 탭). FrameRateSetting과 같은 꼴: 값의 보관은 세이브 체인(PlayerAccountData), 여기는 현재 값과 실제 진동.
// 세이브에는 "꺼짐"을 저장한다: 필드가 없던 옛 세이브가 false로 읽혀 기본값(켜짐)이 되게
public static class VibrationSetting
{
    public static bool On { get; private set; } = true;

    public static void Restore(bool off)
    {
        On = !off;
    }

    public static void Set(bool on)
    {
        On = on;
    }

    // 게임 코드가 진동을 낼 때 이것만 부른다. 꺼져 있으면 아무것도 안 한다
    public static void Vibrate()
    {
        if (!On) { return; }
#if UNITY_ANDROID || UNITY_IOS
        Handheld.Vibrate();
#endif
    }
}
