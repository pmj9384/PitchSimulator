using UnityEngine;
using UnityEngine.UI;

// 공통 팝업 틀(2026-10-29 표준안). 딤 + 패널 + 제목 + 우상단 X. 딤을 누르거나 X를 누르면 같은 GameObject의 UIPopup을 닫는다.
// 안드로이드 뒤로가기는 UIManager가 맨 위 팝업을 닫는다(UIPopup.Show/Hide가 스택에 넣고 뺀다).
// 근거: 모달 닫기 = 딤·X·뒤로가기 셋 다(실험/2026-10-29-설정창-기본UI-표준-조사.md 3절)
[RequireComponent(typeof(UIPopup))]
public class PopupFrame : MonoBehaviour
{
    [SerializeField] private Button dimButton;      // 비워 두면 딤을 눌러도 안 닫힌다(되돌릴 수 없는 확인 팝업 등)
    [SerializeField] private Button closeButton;

    private UIPopup popup;

    private void Awake()
    {
        popup = GetComponent<UIPopup>();
        if (dimButton != null) { dimButton.onClick.AddListener(OnCloseRequested); }
        if (closeButton != null) { closeButton.onClick.AddListener(OnCloseRequested); }
    }

    private void OnCloseRequested()
    {
        popup.Hide();
    }
}
