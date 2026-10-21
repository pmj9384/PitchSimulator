using System.Collections;
using Game.Core.Data;
using Game.Core.League;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 스카우트 판정 연출(10-22, 10-19 유저 "너무 텍스트 방식이라 맛이 없다, 가챠처럼 성공 화면"). 판정은 이미 끝난 값(ScoutVerdict)을 받아 보여 주기만 한다.
// 뒷면(?) 카드가 뒤집히며 앞면(초상·이름·★·성공/실패)이 나온다. 뒤집는 동안 탭하면 바로 끝 장면, 끝난 뒤 탭하면 닫힌다(가챠 스킵 문법).
// 확률형 아이템 표시 의무와는 무관: 결과는 시드로 결정적이고 연출만 빌린 것(스펙 §4-5 10-19)
public class ScoutRevealPopup : UIPopup
{
    private const float FlipSeconds = 0.6f;   // [가정] 출처 없음. 모바일 가챠 카드 뒤집기 0.5~0.8초대
    private static readonly Color SuccessColor = new Color(0.18f, 0.6f, 0.3f);
    private static readonly Color FailColor = new Color(0.45f, 0.45f, 0.5f);

    [SerializeField] private Button tapButton;      // 화면 전체. 뒤집는 중엔 건너뛰기, 끝나면 닫기
    [SerializeField] private RectTransform card;    // 가로 스케일로 뒤집는다
    [SerializeField] private GameObject back;       // 뒤집기 전반부에 보이는 면
    [SerializeField] private GameObject front;      // 후반부에 보이는 면
    [SerializeField] private Image portrait;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text starsText;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text detailText;

    private Coroutine flip;
    private bool revealed;

    private void Awake()
    {
        tapButton.onClick.AddListener(OnTapped);
    }

    // detail = 화면이 만든 판정 문장(주사위·확률·재시도). 숫자 모양은 ScoutScreen 한 곳이 정한다
    public void Show(PlayerStats target, Sprite portraitSprite, ScoutVerdict verdict, string detail)
    {
        portrait.sprite = portraitSprite;
        portrait.enabled = portraitSprite != null;
        nameText.text = $"{target.RoleId} {target.DisplayName}";
        starsText.text = new string('★', verdict.Stars);
        resultText.text = verdict.Success ? "영입 성공" : "실패";
        resultText.color = verdict.Success ? SuccessColor : FailColor;
        detailText.text = detail;

        base.Show();
        revealed = false;
        back.SetActive(true);
        front.SetActive(false);
        card.localScale = Vector3.one;
        StopFlip();
        flip = StartCoroutine(Flip());
    }

    public override void Hide()
    {
        StopFlip();
        base.Hide();
    }

    private void OnTapped()
    {
        if (!revealed)
        {
            StopFlip();
            Reveal();
            card.localScale = Vector3.one;
            return;
        }
        Hide();
    }

    // 가로 스케일 1 → 0(뒷면) → 1(앞면). 중간에서 면을 바꾼다. 일시정지와 무관하게 돌도록 unscaled
    private IEnumerator Flip()
    {
        float elapsed = 0f;
        while (elapsed < FlipSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            if (!revealed && elapsed >= FlipSeconds * 0.5f) { Reveal(); }
            float width = Mathf.Abs(Mathf.Cos(Mathf.PI * Mathf.Clamp01(elapsed / FlipSeconds)));
            card.localScale = new Vector3(width, 1f, 1f);
            yield return null;
        }
        card.localScale = Vector3.one;
        flip = null;
    }

    private void Reveal()
    {
        revealed = true;
        back.SetActive(false);
        front.SetActive(true);
    }

    private void StopFlip()
    {
        if (flip == null) { return; }
        StopCoroutine(flip);
        flip = null;
    }
}
