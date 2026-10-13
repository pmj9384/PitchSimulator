using UnityEngine;
using UnityEngine.UI;

public class SettingsPanel : UIElement
{
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Button closeButton;

    public override void Initialize()
    {
        gameObject.SetActive(false);
        closeButton.onClick.AddListener(OnCloseClicked);
        bgmSlider.onValueChanged.AddListener(OnBgmChanged);
        sfxSlider.onValueChanged.AddListener(OnSfxChanged);
    }

    private void OnCloseClicked()
    {
        gameUIManager.HideUIElement(UIElementEnums.SettingsPanel);
    }

    private void OnBgmChanged(float volume)
    {
        SoundManager.Instance.SetBgmVolume(volume);
    }

    private void OnSfxChanged(float volume)
    {
        SoundManager.Instance.SetSfxVolume(volume);
    }

    public override void Show()
    {
        bgmSlider.SetValueWithoutNotify(SoundManager.Instance.BgmVolume);
        sfxSlider.SetValueWithoutNotify(SoundManager.Instance.SfxVolume);
        gameObject.SetActive(true);
    }

    public override void Hide() => gameObject.SetActive(false);
}
