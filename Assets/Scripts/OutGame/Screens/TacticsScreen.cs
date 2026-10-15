using UnityEngine;

// 로비의 전술 화면(09-30 유저 결정: 로비에서 세팅하고 경기 직전에 상대를 보고 고친다, FM·FC식).
// 편집은 TacticsBoardView가 한다. 값은 시즌 한 곳에만 저장돼서 여기서 바꾼 것이 경기에 그대로 들어간다. 매치데이 허브의 [전술판]도 이 화면을 연다(10-13, 스펙 §2-1)
// 하단 바 [전술] 탭(BottomMenuPanel)이 연다. 열 때마다 시즌의 지금 값으로 다시 채운다
public class TacticsScreen : UIScreen
{
    [SerializeField] private TacticsBoardView board;

    public override void Open()
    {
        base.Open();
        board.Refresh();
    }
}
