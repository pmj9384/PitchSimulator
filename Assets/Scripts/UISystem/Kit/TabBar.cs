using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 탭 바(2026-10-29 표준안): 버튼 하나를 고르면 짝인 내용 묶음만 켠다. 설정 항목이 많을 때만 탭을 쓴다(조사 문서 1절).
// 탭이 하나뿐이면 바 자체를 숨긴다. 탭을 빼려면 tabs·tabImages·tabLabels·contents 네 배열에서 같은 칸을 지우고 그 탭 버튼·내용 오브젝트도 지운다(길이가 어긋나면 Awake가 알린다).
// 코드가 고를 때(Select)는 Selected를 쏘지 않는다
public class TabBar : MonoBehaviour
{
    [SerializeField] private Button[] tabs;
    [SerializeField] private Image[] tabImages;     // tabs와 같은 순서
    [SerializeField] private TMP_Text[] tabLabels;  // tabs와 같은 순서. 고른 탭은 강조색 위 짙은 글자, 나머지는 어두운 바탕 위 밝은 글자(10-29 Play: 안 고른 탭 글자가 바탕에 묻혔다)
    [SerializeField] private GameObject[] contents; // tabs와 같은 순서

    public int SelectedIndex { get; private set; }
    public event Action<int> Selected;

    private void Awake()
    {
        if (tabImages.Length != tabs.Length || tabLabels.Length != tabs.Length || contents.Length != tabs.Length)
        {
            Debug.LogError($"[TabBar] {name}: 탭 {tabs.Length}·이미지 {tabImages.Length}·글자 {tabLabels.Length}·내용 {contents.Length}개가 안 맞는다");
        }
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;   // 클로저가 루프 변수 대신 이 탭 번호를 잡게 한다
            tabs[i].onClick.AddListener(() => OnClicked(index));
        }
        gameObject.SetActive(tabs.Length > 1);
        Select(SelectedIndex);
    }

    public void Select(int index)
    {
        SelectedIndex = index;
        for (int i = 0; i < tabs.Length; i++)
        {
            tabImages[i].color = i == index ? UIKitColors.Accent : UIKitColors.Surface;
            tabLabels[i].color = i == index ? UIKitColors.OnAccent : UIKitColors.SubText;
            contents[i].SetActive(i == index);
        }
    }

    private void OnClicked(int index)
    {
        if (index == SelectedIndex) { return; }
        Select(index);
        Selected?.Invoke(index);
    }
}
