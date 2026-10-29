public interface IUIManager
{
    void OpenScreen<T>() where T : UIScreen;
    void ShowPopup<T>() where T : UIPopup;
    void HidePopup<T>() where T : UIPopup;
    T GetPopup<T>() where T : UIPopup;   // 값을 넘겨야 하는 팝업(스카우트 판정 연출·ConfirmPopup.Open)이 쓴다. 없으면 null(10-21)

    void OpenScreen(UIScreen screen);

    // 열린 팝업 스택(UIPopup.Show/Hide가 부른다). 뒤로가기가 맨 위부터 닫는다(10-29 템플릿 UI 키트)
    void NotifyPopupShown(UIPopup popup);
    void NotifyPopupHidden(UIPopup popup);
}
