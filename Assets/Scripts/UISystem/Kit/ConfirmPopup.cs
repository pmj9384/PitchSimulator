using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 확인 팝업(2026-10-29 표준안): 제목 + 본문 + [취소] [주 행동]. 되돌릴 수 없는 행동이면 주 행동을 빨강으로(Apple HIG Alerts: 파괴적 확인엔 취소 필수, 취소를 기본으로 두지 않는다).
// 여는 쪽: uiManager.GetPopup<ConfirmPopup>().Open("선수 방출", "되돌릴 수 없다", "방출 +90", destructive: true, OnRelease). X·뒤로가기는 취소와 같고, 딤은 눌러도 안 닫힌다(실수로 취소되지 않게)
public class ConfirmPopup : UIPopup
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Button cancelButton;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Image confirmImage;
    [SerializeField] private TMP_Text confirmLabel;

    private Action onConfirm;

    private void Awake()
    {
        cancelButton.onClick.AddListener(Hide);
        confirmButton.onClick.AddListener(OnConfirmClicked);
    }

    public void Open(string title, string body, string confirmText, bool destructive, Action confirmed)
    {
        titleText.text = title;
        bodyText.text = body;
        confirmLabel.text = confirmText;
        confirmImage.color = destructive ? UIKitColors.Danger : UIKitColors.Accent;
        confirmLabel.color = destructive ? Color.white : UIKitColors.OnAccent;
        onConfirm = confirmed;
        Show();
    }

    public override void Hide()
    {
        onConfirm = null;   // 닫힌 뒤 늦게 눌린 확인이 이전 행동을 다시 부르지 않게
        base.Hide();
    }

    private void OnConfirmClicked()
    {
        Action confirmed = onConfirm;
        Hide();
        confirmed?.Invoke();
    }
}
