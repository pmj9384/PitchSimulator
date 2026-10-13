using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 스카우트 화면의 목록 한 줄: 팀·상대 선수·내 선수가 같은 부품을 쓴다. 글자를 보여 주고 눌렸다고만 알린다.
// 무엇을 뜻하는지(어느 팀·몇 번째 선수)는 패널이 배열 번호로 안다(PlayerDialRow·PitchChip과 같은 "값만 받는 부품" 꼴)
public class ScoutEntryButton : MonoBehaviour
{
    private static readonly Color SelectedColor = TacticsColors.Highlight;
    private static readonly Color NormalColor = Color.white;
    private static readonly Color NormalTextColor = new Color(0.13f, 0.13f, 0.13f);

    [SerializeField] private Button button;
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text label;

    public event Action<ScoutEntryButton> Clicked;

    public void Initialize()
    {
        button.onClick.AddListener(OnClicked);
    }

    public void Show(string text, bool selected, bool interactable)
    {
        gameObject.SetActive(true);
        label.text = text;
        label.color = NormalTextColor;
        background.color = selected ? SelectedColor : NormalColor;
        button.interactable = interactable;
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void OnClicked()
    {
        Clicked?.Invoke(this);
    }
}
