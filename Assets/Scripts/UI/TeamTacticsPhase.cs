// 팀 전술 모달의 국면 탭 순서(10-29 ③d-2, FM26 팀 지시 3판 In Possession / In Transition / Out of Possession).
// 패널(TeamTacticsPanel)이 탭을 쥐고 미니 피치(TacticsMiniPitch)가 따라간다. 주 필드의 공격/수비 두 탭(PitchPhase)과는 전환이 있어 순서가 다르다
public enum TeamTacticsPhase
{
    Attack = 0,
    Transition = 1,
    Defense = 2,
}
