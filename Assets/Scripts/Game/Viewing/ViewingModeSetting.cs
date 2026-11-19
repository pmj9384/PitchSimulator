using Game.Core.Viewing;
using UnityEngine;

// 보기 모드·배속의 저장 자리(S4). 기기 설정이라 세이브가 아니라 PlayerPrefs에 둔다(설계 문서 7절 결정 9. 고르는 자리를 경기 화면으로 옮긴 것은 9절 11-19 변경).
// 쓰고 읽는 곳은 MatchManager 한 곳(경기 중 우측 상단 패널이 MatchManager를 거쳐 바꾼다, 11-19). 다음 경기도 이 값으로 시작한다. 기본값·검사는 ViewingOptions(Game.Core)
public static class ViewingModeSetting
{
    private const string ModeKey = "ViewingMode";
    private const string SpeedKey = "ViewingSpeed";

    public static ViewingMode Mode => ViewingOptions.SanitizeMode(PlayerPrefs.GetInt(ModeKey, (int)ViewingOptions.DefaultMode));

    public static int Speed => ViewingOptions.SanitizeSpeed(PlayerPrefs.GetInt(SpeedKey, ViewingOptions.DefaultSpeed));

    public static void SetMode(ViewingMode mode)
    {
        PlayerPrefs.SetInt(ModeKey, (int)ViewingOptions.SanitizeMode((int)mode));
        PlayerPrefs.Save();
    }

    public static void SetSpeed(int speed)
    {
        PlayerPrefs.SetInt(SpeedKey, ViewingOptions.SanitizeSpeed(speed));
        PlayerPrefs.Save();
    }
}
