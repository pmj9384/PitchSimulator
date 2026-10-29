// 전술 화면의 공격할 때/수비할 때 탭 순서(TabGroup 인덱스). 주 필드(PitchView)와 포메이션 모달(FormationPanel)이 같은 값을 본다.
// 둘이 각자 상수를 들면 탭 순서가 바뀔 때 한쪽만 조용히 어긋난다(10-29 리뷰). 팀 전술 모달은 전환 탭이 있어 TeamTacticsPhase를 따로 쓴다
public static class PitchPhase
{
    public const int Attack = 0;
    public const int Defend = 1;
}
