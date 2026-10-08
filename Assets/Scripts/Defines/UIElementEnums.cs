public enum UIElementEnums
{
    ResultPanel,   // 승·무·패 결과 화면(09-28, 옛 GameOverPanel). 인덱스 0 그대로. 생성 버튼은 오브젝트 이름으로 만드니 씬 오브젝트 이름도 ResultPanel
    PausePanel,
    SettingsPanel,
    MatchHud,   // 스코어·시간·소유 팀(09-27). uiElements 배열 인덱스 3
    TacticsPanel,   // 킥오프 전 팀 전술 설정창(09-30, 스펙 §4-2). 인덱스 4. 씬 uiElements 목록 5번째에 직접 넣는다(자식 순서로 재생성하면 MatchHud가 맨 앞이라 어긋난다)
}
