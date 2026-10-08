using System;
using UnityEngine;
using UnityEngine.UI;

// 버튼 몇 개 중 하나를 고르면 그 버튼을 강조하고, 짝이 있는 내용 묶음만 보여 준다.
// 전술 화면에서 두 군데가 쓴다: 팀 전술 세부 3묶음(공격할 때·수비할 때·공수가 바뀔 때), 필드의 공격 시/수비 시.
// 내용 묶음이 없는 그룹(contents를 비움)은 선택 표시와 Selected 알림만 한다.
// 코드가 고를 때(Select)는 알림을 쏘지 않는다. 사용자가 눌렀을 때만 쏜다
public class TabGroup : MonoBehaviour
{
    private static readonly Color SelectedColor = new Color(1f, 0.835f, 0.31f);   // 전술 화면 공통 강조색(#FFD54F)
    private static readonly Color NormalColor = new Color(0.82f, 0.84f, 0.88f);   // 안 고른 탭. 글자가 짙은 색이라 밝은 회색

    [SerializeField] private Button[] tabButtons;
    [SerializeField] private Image[] tabImages;
    [SerializeField] private GameObject[] contents;   // 비워 둘 수 있다. 있으면 tabButtons와 같은 순서

    public int SelectedIndex { get; private set; }
    public event Action<int> Selected;

    private bool initialized;

    // 입력 연결은 한 번만. 여러 번 불러도 된다
    public void Initialize()
    {
        if (initialized) { return; }
        initialized = true;

        // 버튼·강조 이미지는 한 벌이고 내용 묶음은 없거나 같은 수다. 어긋나면 일부 탭만 조용히 안 먹는다(09-30 리뷰)
        bool contentsMatch = contents.Length == 0 || contents.Length == tabButtons.Length;
        if (tabImages.Length != tabButtons.Length || !contentsMatch)
        {
            Debug.LogError($"[TabGroup] {name}: 버튼 {tabButtons.Length}·이미지 {tabImages.Length}·내용 {contents.Length}개가 안 맞는다. 프리팹 배선을 확인한다");
        }

        for (int i = 0; i < tabButtons.Length; i++)
        {
            int index = i;   // 클로저가 루프 변수 대신 이 탭 번호를 잡게 한다
            tabButtons[i].onClick.AddListener(() => OnTabClicked(index));
        }
        Select(SelectedIndex);
    }

    public void Select(int index)
    {
        SelectedIndex = index;
        for (int i = 0; i < tabImages.Length; i++)
        {
            tabImages[i].color = i == index ? SelectedColor : NormalColor;
        }
        for (int i = 0; i < contents.Length; i++)
        {
            contents[i].SetActive(i == index);
        }
    }

    private void OnTabClicked(int index)
    {
        if (index == SelectedIndex) { return; }
        Select(index);
        Selected?.Invoke(index);
    }
}
