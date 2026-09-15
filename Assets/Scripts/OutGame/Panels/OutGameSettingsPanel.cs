using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class OutGameSettingsPanel : UIPopup
{
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Button closeButton;

    // 순서는 FrameRateSetting.Options와 짝을 이룬다. 라벨을 코드가 찍으므로 순서가 어긋나면
    // 토글에 다른 숫자가 뜬다 — 암묵 계약을 눈으로 잡을 수 있게 한 것.
    // ToggleGroup(allowSwitchOff=false)이 "정확히 하나 선택"을 강제하므로 선택 표시를 직접 그리지 않는다
    [SerializeField] private Toggle[] frameRateToggles;

    private void Awake()
    {
        closeButton.onClick.AddListener(Hide);
        bgmSlider.onValueChanged.AddListener(v => SoundManager.Instance.SetBgmVolume(v));
        sfxSlider.onValueChanged.AddListener(v => SoundManager.Instance.SetSfxVolume(v));

        var options = FrameRateSetting.Options;
        for (int i = 0; i < frameRateToggles.Length && i < options.Length; i++)
        {
            int fps = options[i];   // 클로저가 루프 변수를 잡지 않도록 복사
            var label = frameRateToggles[i].GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = fps.ToString();

            frameRateToggles[i].onValueChanged.AddListener(on =>
            {
                if (on) FrameRateSetting.Set(fps);   // 꺼지는 쪽은 무시 — 그룹이 곧바로 다른 하나를 켠다
            });
        }
    }

    public override void Show()
    {
        bgmSlider.SetValueWithoutNotify(SoundManager.Instance.bgmVolume);
        sfxSlider.SetValueWithoutNotify(SoundManager.Instance.sfxVolume);
        SyncFrameRateToggles();
        base.Show();
    }

    // 창을 열 때 현재 상한에 해당하는 토글만 켠다. Notify를 태우면 Set이 다시 불려 불필요한 저장이 생긴다
    private void SyncFrameRateToggles()
    {
        var options = FrameRateSetting.Options;
        for (int i = 0; i < frameRateToggles.Length && i < options.Length; i++)
            frameRateToggles[i].SetIsOnWithoutNotify(options[i] == FrameRateSetting.Current);
    }
}
