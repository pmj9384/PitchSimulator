using UnityEngine;

// 프레임 상한 설정 — 고를 수 있는 값의 SSOT이자, 상한을 실제로 적용하는 지점.
// 값의 보관은 세이브 체인 몫이다(PlayerAccountData.Save가 Current를 끌어가고, Load가 Restore로 밀어넣는다).
// 노출은 OutGameSettingsPanel의 토글이 담당한다.
public static class FrameRateSetting
{
    public const int Default = 60;

    // 설정 패널이 토글 라벨을 여기서 찍는다 — 화면에 숫자를 하드코딩하지 않게
    public static readonly int[] Options = { 60, 120 };

    public static int Current { get; private set; } = Default;

    // 세이브가 손상됐거나 이 필드가 없던 옛 세이브(0으로 읽힘)를 기본값으로 흡수한다.
    // 인덱스가 아니라 fps 값을 저장하므로 목록이 바뀌어도 마이그레이션이 필요 없다
    public static int Sanitize(int fps)
        => System.Array.IndexOf(Options, fps) >= 0 ? fps : Default;

    public static void Restore(int fps) => Apply(Sanitize(fps));

    public static void Set(int fps) => Apply(Sanitize(fps));

    private static void Apply(int fps)
    {
        Current = fps;
        // vSyncCount가 0이 아니면 targetFrameRate는 통째로 무시된다(공식 문서).
        // 모바일은 vSyncCount 자체를 무시해 실기기엔 영향이 없지만,
        // 에디터·데스크톱 빌드에서 설정이 실제로 먹으려면 꺼줘야 한다
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Current;
    }
}
