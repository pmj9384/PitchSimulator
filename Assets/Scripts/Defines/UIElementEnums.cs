public enum UIElementEnums
{
    ResultPanel,   // 승·무·패 결과 화면(09-28, 옛 GameOverPanel). 인덱스 0 그대로. 생성 버튼은 오브젝트 이름으로 만드니 씬 오브젝트 이름도 ResultPanel
    PausePanel,
    SettingsPanel,
    MatchHud,   // 스코어·시간·소유 팀(09-27). uiElements 배열 인덱스 3
    // 킥오프 전 전술 설정창과 스카우트 화면은 10-13에 로비(매치데이 허브, 스펙 §2-1)로 옮겨 인게임에서 뺐다. 인게임은 들어가는 순간 킥오프다
}
