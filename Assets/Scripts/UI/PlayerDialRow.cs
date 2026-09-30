using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 개인 지시 한 줄(09-30): 이름 · [<] 지금 단계 [>]. 개인 전술 패널(PlayerTacticsPanel)이 10줄을 들고 선수에게 해당하는 줄만 보여 준다.
// 어느 다이얼인지, 단계가 무슨 뜻인지는 모른다. 받은 글자를 보여 주고 누른 방향(−1·+1)만 알린다
public class PlayerDialRow : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text stepText;
    [SerializeField] private Button lessButton;
    [SerializeField] private Button moreButton;

    public event Action<PlayerDialRow, int> Stepped;

    public void Initialize()
    {
        lessButton.onClick.AddListener(OnLess);
        moreButton.onClick.AddListener(OnMore);
    }

    // 끝 단계에선 그 방향 버튼을 잠근다
    public void Show(string dialName, string step, bool canLess, bool canMore)
    {
        nameText.text = dialName;
        stepText.text = step;
        lessButton.interactable = canLess;
        moreButton.interactable = canMore;
    }

    private void OnLess()
    {
        Stepped?.Invoke(this, -1);
    }

    private void OnMore()
    {
        Stepped?.Invoke(this, +1);
    }
}
