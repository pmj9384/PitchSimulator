using UnityEngine;

// 아웃게임 설정창(2026-10-29 UI 키트 표준안). 프리팹이 둘이고 스크립트는 하나다(Assets/Prefabs/UI/Settings).
// - 가로형(RPG·스포츠): 왼쪽 세로 탭 3개(소리 / 화면 / 기타), 소리는 슬라이더
// - 세로형(캐주얼): 탭 없는 카드 한 장, 소리는 켜기/끄기 스위치
// 소리 컨트롤은 슬라이더(bgmRow)와 스위치(bgmSwitch) 중 프리팹에 배선된 쪽을 쓴다. 화면만 다르고 하는 일은 같아서 클래스를 나누지 않았다.
// 바꾸면 바로 적용되고 저장은 세이브 시스템이 한다(적용 버튼 없음, Game Accessibility Guidelines "모든 설정 저장").
// 닫기는 PopupFrame(딤·X)과 UIManager(뒤로가기). 값의 SSOT는 SoundManager·FrameRateSetting·VibrationSetting, 여기는 화면과 입력만
public class OutGameSettingsPanel : UIPopup
{
    private const float SwitchOnVolume = 1f;      // 스위치로 켤 때 볼륨. 세로형은 세기 조절이 없다
    private const float AudibleMin = 0.01f;       // 이보다 작으면 꺼진 것으로 본다(SoundManager가 0 대신 0.0001을 저장한다)

    [Header("소리: 슬라이더(가로형) 또는 스위치(세로형), 배선된 쪽만")]
    [SerializeField] private SliderRow bgmRow;
    [SerializeField] private SliderRow sfxRow;
    [SerializeField] private ToggleSwitch bgmSwitch;
    [SerializeField] private ToggleSwitch sfxSwitch;

    [Header("화면")]
    [SerializeField] private SegmentedControl frameRate;   // 칸 순서 = FrameRateSetting.Options

    [Header("기타")]
    [SerializeField] private ToggleSwitch vibration;

    private void Awake()
    {
        if (bgmRow != null) { bgmRow.ValueChanged += OnBgmChanged; }
        if (sfxRow != null) { sfxRow.ValueChanged += OnSfxChanged; }
        if (bgmSwitch != null) { bgmSwitch.Changed += OnBgmSwitched; }
        if (sfxSwitch != null) { sfxSwitch.Changed += OnSfxSwitched; }
        frameRate.SetLabels(FrameRateLabels());
        frameRate.Selected += OnFrameRateSelected;
        vibration.Changed += OnVibrationChanged;
    }

    public override void Show()
    {
        float bgm = SoundManager.Instance.BgmVolume;
        float sfx = SoundManager.Instance.SfxVolume;
        if (bgmRow != null) { bgmRow.SetValueWithoutNotify(bgm); }
        if (sfxRow != null) { sfxRow.SetValueWithoutNotify(sfx); }
        if (bgmSwitch != null) { bgmSwitch.SetWithoutNotify(bgm >= AudibleMin); }
        if (sfxSwitch != null) { sfxSwitch.SetWithoutNotify(sfx >= AudibleMin); }
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

    private void OnBgmSwitched(bool on)
    {
        SoundManager.Instance.SetBgmVolume(on ? SwitchOnVolume : 0f);
    }

    private void OnSfxSwitched(bool on)
    {
        SoundManager.Instance.SetSfxVolume(on ? SwitchOnVolume : 0f);
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
