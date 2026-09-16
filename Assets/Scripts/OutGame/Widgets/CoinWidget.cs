using TMPro;
using UnityEngine;

[RequireComponent(typeof(TextMeshProUGUI))]
public class CoinWidget : UIWidget
{
    private TMP_Text coinText;

    private void Awake() => coinText = GetComponent<TextMeshProUGUI>();

    protected override void Subscribe()
    {
        GameDataManager.Instance.PlayerAccountData.OnCoinsChanged += UpdateUI;
        Refresh();
    }

    protected override void Unsubscribe()
    {
        if (!GameDataManager.HasInstance) { return; }   // 씬 teardown — Instance 접근이 죽은 싱글톤을 재생성하므로 금지
        GameDataManager.Instance.PlayerAccountData.OnCoinsChanged -= UpdateUI;
    }

    public override void Refresh()
        => UpdateUI(GameDataManager.Instance.PlayerAccountData.Coins);

    private void UpdateUI(int coins) => coinText.text = coins.ToString("N0");
}
