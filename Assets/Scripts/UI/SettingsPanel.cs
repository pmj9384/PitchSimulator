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
        closeButton.onClick.AddListener(() => gameUIManager.HideUIElement(UIElementEnums.SettingsPanel));
        bgmSlider.onValueChanged.AddListener(v => SoundManager.Instance.SetBgmVolume(v));
        sfxSlider.onValueChanged.AddListener(v => SoundManager.Instance.SetSfxVolume(v));
    }

    public override void Show()
    {
        bgmSlider.SetValueWithoutNotify(SoundManager.Instance.bgmVolume);
        sfxSlider.SetValueWithoutNotify(SoundManager.Instance.sfxVolume);
        gameObject.SetActive(true);
    }

    public override void Hide() => gameObject.SetActive(false);
}
