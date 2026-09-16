using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
public class GameManager : MonoBehaviour
{
    public enum GameState
    {
        WaitLoading,
        GameReady,
        GamePlay,
        GameStop,
        GameOver,
        GameClear,
        Max,
    }

    // 진입(Enter) -> 시작(Start) -> 퇴장(Exit) 순으로 실행
    private Action[] gameStateEnterAction;
    private Action[] gameStateStartAction;
    private Action[] gameStateExitAction;

    private GameState previousState;
    private GameState currentState;
    public GameState CurrentState => currentState;
    public GameState PreviousState => previousState;

    private float previousStopTimeScale;

    #region 핵심 매니저 시스템
    private List<IManager> managers = new List<IManager>();

    public ObjectPoolManager ObjectPool { get; private set; }
    public GameUIManager UIManager { get; private set; }

    // 게임 매니저 3종(09-16 이식 1차). 매니저 간 참조는 이 프로퍼티 경유(CLAUDE.md 관례)
    public PlayerManager Players { get; private set; }
    public MatchManager Match { get; private set; }
    public StageManager Stage { get; private set; }
    // TODO: 게임별 매니저 추가

    #endregion

    private void Awake()
    {
        InitializeStateActions();
        InitializeCoreManagers();
        SetGameState(GameState.WaitLoading);
    }

    public static bool SkipTitle;

    private void Start()
    {
        SetGameState(GameState.GameReady);
        if (SkipTitle)
        {
            SkipTitle = false;
            SetGameState(GameState.GamePlay);
        }
    }

    private void InitializeStateActions()
    {
        int stateCount = (int)GameState.Max;
        gameStateEnterAction = new Action[stateCount];
        gameStateStartAction = new Action[stateCount];
        gameStateExitAction = new Action[stateCount];

        // 기본 일시정지/재생
        AddGameStateStartAction(GameState.GameStop, PauseTimeScale);
        AddGameStateExitAction(GameState.GameStop, ResumeTimeScale);

        // BGM 연결 - BgmClipId는 프로젝트마다 Defines/Enums.cs에 정의 필요
        // AddGameStateEnterAction(GameState.GameReady, () => SoundManager.Instance.PlayBgm(BgmClipId.IngameBGM));
        AddGameStateEnterAction(GameState.GameStop, () => SoundManager.Instance.PauseBgm());
        AddGameStateEnterAction(GameState.GameStop, () => SoundManager.Instance.PauseSfx());
        AddGameStateExitAction(GameState.GameStop, () => SoundManager.Instance.ResumeBgm());
        AddGameStateExitAction(GameState.GameStop, () => SoundManager.Instance.ResumeSfx());
        AddGameStateEnterAction(GameState.GameOver, () => SoundManager.Instance.StopBgm());

        // TODO: GameOver 시 점수 저장 등 게임 특화 로직 추가
        // AddGameStateEnterAction(GameState.GameOver, () => GameDataManager.Instance.PlayerAccountData.TryUpdateBestScore(score));
    }

    private void InitializeCoreManagers()
    {
        // 1. 순수 C# 매니저
        ObjectPool = new ObjectPoolManager();
        managers.Add(ObjectPool);

        // 2. 씬에서 "Manager" 태그로 MonoBehaviour 매니저 자동 등록
        List<GameObject> managerObjects = GameObject.FindGameObjectsWithTag("Manager").ToList();

        UIManager = RegisterManager<GameUIManager>(managerObjects);
        Players = RegisterManager<PlayerManager>(managerObjects);
        Match = RegisterManager<MatchManager>(managerObjects);
        Stage = RegisterManager<StageManager>(managerObjects);   // 등록 순서 = Initialize 순서 = GameReady 훅 순서(Match 시계 0 → Stage 스폰)

        foreach (var manager in managers)
        {
            manager.Initialize();
        }
        UIManager.InitializedUIElements();
    }

    private T RegisterManager<T>(List<GameObject> list) where T : InGameManager
    {
        T component = null;
        foreach (var obj in list)
        {
            if (obj.TryGetComponent<T>(out component)) { break; }
        }

        if (component != null)
        {
            component.SetGameManager(this);
            managers.Add(component);
        }
        else
        {
            Debug.LogWarning($"[GameManager] {typeof(T).Name}를 찾을 수 없습니다.");
        }
        return component;
    }

    #region 상태 제어
    public void SetGameState(GameState newState)
    {
        if (currentState == newState) { return; }

        previousState = currentState;
        currentState = newState;

        gameStateExitAction[(int)previousState]?.Invoke();
        gameStateEnterAction[(int)currentState]?.Invoke();
        gameStateStartAction[(int)currentState]?.Invoke();
    }

    // skipReady: 다시 로드한 뒤 GameReady(타이틀·준비 국면)를 건너뛰고 바로 GamePlay로 갈지.
    // 러너처럼 "탭해서 시작" 화면이면 true, 배치 국면처럼 매판 다시 거쳐야 하면 false (WarTableSimulator 09-14).
    public void RestartGame(bool skipReady)
    {
        SkipTitle = skipReady;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // 인게임 → 아웃게임 복귀. RestartGame과 몸통이 같아 현재 씬을 다시 로드하던 것을 고쳤다
    // (일시정지 창의 홈 버튼이 로비로 못 가고 판을 재시작시켰다)
    public void GoToTitle()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("LobbyScene");
    }

    // 일시정지 복귀 — 이전 상태로 돌아가되 그 상태의 진입 훅은 다시 쏘지 않는다 (초기 세팅이 두 번 돌면 안 된다).
    // GameStop의 퇴장 훅(timeScale·BGM 복구, 일시정지 창 닫기)만 실행한다. SetGameState(GamePlay)로 복귀하면
    // GamePlay 진입 훅이 다시 돌아 준비 국면(배치 등)에서 멈췄다 풀 때 게임이 시작돼 버린다 (WarTableSimulator 09-14).
    public void ResumeFromPause()
    {
        if (currentState != GameState.GameStop) { return; }

        GameState resumeTo = previousState;
        previousState = currentState;
        currentState = resumeTo;
        gameStateExitAction[(int)GameState.GameStop]?.Invoke();
    }

    public void AddGameStateEnterAction(GameState state, Action action) => gameStateEnterAction[(int)state] += action;
    public void RemoveGameStateEnterAction(GameState state, Action action) => gameStateEnterAction[(int)state] -= action;
    public void AddGameStateStartAction(GameState state, Action action) => gameStateStartAction[(int)state] += action;
    public void RemoveGameStateStartAction(GameState state, Action action) => gameStateStartAction[(int)state] -= action;
    public void AddGameStateExitAction(GameState state, Action action) => gameStateExitAction[(int)state] += action;
    public void RemoveGameStateExitAction(GameState state, Action action) => gameStateExitAction[(int)state] -= action;
    #endregion

    private void PauseTimeScale()
    {
        previousStopTimeScale = Time.timeScale;
        Time.timeScale = 0;
    }

    private void ResumeTimeScale()
    {
        Time.timeScale = previousStopTimeScale;
    }

    private void OnDestroy()
    {
        foreach (var manager in managers)
        {
            manager.Clear();
        }
    }
}
