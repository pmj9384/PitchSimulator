using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 부팅 로딩 화면의 표시부 — 진행바·텍스트 갱신만. 로딩 절차는 SceneLoader 소관.
// AnimalBreakOut LoadingSceneUI 승계 (검수 2026-08-03: 원본은 UpdateLoadingProgress 호출부가
// 없어 진행바가 0% 고정 장식이었음 — 이번엔 SceneLoader가 매 프레임 실배선한다)
public class LoadingSceneUI : MonoBehaviour
{
    [SerializeField] private Slider progressBar;
    [SerializeField] private TMP_Text progressText;

    private void Start()
    {
        UpdateProgress(0f);
    }

    public void UpdateProgress(float progress)
    {
        progressBar.value = progress;
        progressText.text = $"Loading... {progress * 100:F0}%";
    }
}
