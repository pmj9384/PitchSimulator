using UnityEngine;

// 아웃게임 설정창(2026-10-29 UI 키트 표준안). 탭 3개 = 소리(배경음·효과음) / 화면(프레임 상한) / 기타(진동).
// 바꾸면 바로 적용되고 저장은 세이브 체인이 한다(적용 버튼 없음, Game Accessibility Guidelines "모든 설정 저장").
// 닫기는 PopupFrame(딤·X)과 UIManager(뒤로가기). 게임이 안 쓰는 탭을 빼는 절차는 TabBar 머리 주석. 그 탭의 부품을 지우면 여기 필드와 구독도 같이 지운다.
// 값의 SSOT는 SoundManager·FrameRateSetting·VibrationSetting, 여기는 화면과 입력만
public class OutGameSettingsPanel : UIPopup
{
    [Header("소리")]
    [SerializeField] private SliderRow bgmRow;
    [SerializeField] private SliderRow sfxRow;

    [Header("화면")]
    [SerializeField] private SegmentedControl frameRate;   // 칸 순서 = FrameRateSetting.Options

    [Header("기타")]
    [SerializeField] private ToggleSwitch vibration;

    private void Awake()
    {
        bgmRow.ValueChanged += OnBgmChanged;
        sfxRow.ValueChanged += OnSfxChanged;
        frameRate.SetLabels(FrameRateLabels());
        frameRate.Selected += OnFrameRateSelected;
        vibration.Changed += OnVibrationChanged;
    }

    public override void Show()
    {
        bgmRow.SetValueWithoutNotify(SoundManager.Instance.BgmVolume);
        sfxRow.SetValueWithoutNotify(SoundManager.Instance.SfxVolume);
        frameRate.SetSelectedWithoutNotify(CurrentFrameRateIndex());
        vibration.SetWithoutNotify(VibrationSetting.On);
        base.Show();
    }

    private void OnBgmChanged(float value)
    {
        SoundManager.Instance.SetBgmVolume(value);
    }

    private void OnSfxChanged(float value)
    {
        SoundManager.Instance.SetSfxVolume(value);
    }

    private void OnFrameRateSelected(int index)
    {
        FrameRateSetting.Set(FrameRateSetting.Options[index]);
    }

    private void OnVibrationChanged(bool on)
    {
        VibrationSetting.Set(on);
        VibrationSetting.Vibrate();   // 켰을 때 한 번 울려 확인시킨다
    }

    private static int CurrentFrameRateIndex()
    {
        return System.Array.IndexOf(FrameRateSetting.Options, FrameRateSetting.Current);
    }

    private static string[] FrameRateLabels()
    {
        int[] options = FrameRateSetting.Options;
        var names = new string[options.Length];
        for (int i = 0; i < options.Length; i++) { names[i] = options[i].ToString(); }
        return names;
    }
}
