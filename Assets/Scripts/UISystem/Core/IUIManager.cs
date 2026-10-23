public interface IUIManager
{
    void OpenScreen<T>() where T : UIScreen;
    void ShowPopup<T>() where T : UIPopup;
    void HidePopup<T>() where T : UIPopup;
    T GetPopup<T>() where T : UIPopup;   // 값을 넘겨야 하는 팝업(스카우트 판정 연출)이 쓴다. 없으면 null(10-21)

    void OpenScreen(UIScreen screen);
}
