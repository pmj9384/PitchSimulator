using System;

namespace Game.Core.Viewing
{
    // 보기 모드·배속의 기본값과 고를 수 있는 값(11-05 유저 확정: 핵심·확장·전체, 전체만 ×1·×2·×4, 기본 확장·×1).
    // 저장(PlayerPrefs)은 Unity 쪽 ViewingModeSetting이 하고, 값 검사는 EditMode로 잠그려고 여기 둔다
    public static class ViewingOptions
    {
        public const ViewingMode DefaultMode = ViewingMode.Extended;
        public const int DefaultSpeed = 1;

        public static readonly int[] Speeds = { 1, 2, 4 };

        // 범위 밖 값(손상·옛 값)은 기본값으로
        public static ViewingMode SanitizeMode(int value)
        {
            if (value < (int)ViewingMode.Key || value > (int)ViewingMode.Full) { return DefaultMode; }
            return (ViewingMode)value;
        }

        public static int SanitizeSpeed(int value)
        {
            if (Array.IndexOf(Speeds, value) < 0) { return DefaultSpeed; }
            return value;
        }

        // 실제로 적용할 배속. 배속은 전체 모드에만 있다(하이라이트 재생은 ×1, 설계 7절 결정 6)
        public static int EffectiveSpeed(ViewingMode mode, int speed)
        {
            if (mode != ViewingMode.Full) { return DefaultSpeed; }
            return SanitizeSpeed(speed);
        }
    }
}
