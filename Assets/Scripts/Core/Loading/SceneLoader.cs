using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

// 부팅 로딩 씬 진행자 — Addressables 라벨 프리로드 후 다음 씬으로 넘어간다 (빌드 0번 씬 전용).
// AnimalBreakOut LoadingScene 검수 후 재설계 (2026-08-03): 가짜 진행바 → PercentComplete 실배선 /
// 씬 이름 문자열 계약 → SerializeField / 인게임 위 additive 커버 → 부팅 진입 씬 /
// 실패 시 영구 감금(while 대기) → 로그 남기고 다음 씬 진입 (세이브 폴백과 같은 사상: 부팅은 보장한다)
public class SceneLoader : MonoBehaviour
{
    [SerializeField] private string nextSceneName = "LobbyScene";
    // Addressables 라벨 — 비우면 프리로드 생략. 템플릿 기본값은 빈 문자열이다:
    // 새 프로젝트엔 Addressables 설정 자체가 없어서, 라벨이 차 있으면 부팅하자마자
    // 런타임 데이터 로드 실패로 콘솔에 에러가 쌓인다(아래 Preload의 라벨 부재 방어보다 한 층 아래 실패라 막히지 않는다).
    // 프리로드할 에셋 그룹이 생기면 그 라벨을 적어 되살린다 — 등록은 AddressablesFolderRegistrar가 담당한다.
    [SerializeField] private string preloadLabel = "";
    [SerializeField] private float minShowSeconds = 1f;       // 로컬 로드는 순식간 — 스플래시가 깜빡 사라지는 것 방지
    [SerializeField] private LoadingSceneUI ui;

    private IEnumerator Start()
    {
        float shownAt = Time.realtimeSinceStartup;   // timeScale 무관 기준

        if (!string.IsNullOrEmpty(preloadLabel))
            yield return Preload();

        // 최소 노출 시간 채우기 — 남은 시간 동안 바를 100%까지 마저 채우는 연출
        float remain;
        while ((remain = minShowSeconds - (Time.realtimeSinceStartup - shownAt)) > 0f)
        {
            if (ui != null) ui.UpdateProgress(1f - remain / minShowSeconds);
            yield return null;
        }
        if (ui != null) ui.UpdateProgress(1f);

        yield return SceneManager.LoadSceneAsync(nextSceneName);   // 공식 표준 관용구 — 로드 중에도 로딩 화면이 살아 있게
    }

    private IEnumerator Preload()
    {
        // 라벨 실존을 조용히 먼저 조회 — 없는 키에 바로 Download를 걸면 Addressables가 콘솔에
        // 빨간 InvalidKeyException을 뿌린다 (새 프로젝트에 라벨이 아직 없을 때의 템플릿 방어이기도)
        var probe = Addressables.LoadResourceLocationsAsync(preloadLabel);
        yield return probe;
        int found = probe.Status == AsyncOperationStatus.Succeeded ? probe.Result.Count : 0;
        Addressables.Release(probe);
        if (found == 0)
        {
            Debug.LogWarning($"[SceneLoader] 프리로드 라벨 '{preloadLabel}' 대상 없음 — 건너뛰고 진입");
            yield break;   // 프리로드는 최적화일 뿐 — 실패가 부팅을 막으면 안 된다
        }

        // DownloadDependenciesAsync: 원격이면 다운로드, 로컬이면 번들 캐시 예열 —
        // 스킨이 원격 배포로 전환돼도 이 한 곳만 그대로 동작한다 (SkinSprites 캐시와 상호보완)
        AsyncOperationHandle handle = Addressables.DownloadDependenciesAsync(preloadLabel);

        while (!handle.IsDone)
        {
            if (ui != null) ui.UpdateProgress(handle.PercentComplete * 0.9f);   // 마지막 10%는 최소 노출 연출 몫
            yield return null;
        }

        if (handle.Status == AsyncOperationStatus.Failed)
            Debug.LogWarning($"[SceneLoader] 프리로드 실패({preloadLabel}) — 개별 로드는 사용 시점 SkinSprites가 담당");

        Addressables.Release(handle);   // 다운로드/예열 목적 달성 — 실사용 refcount는 SkinSprites 몫
    }
}
