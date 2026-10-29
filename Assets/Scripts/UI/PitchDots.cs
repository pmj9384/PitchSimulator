using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Placement;
using TMPro;
using UnityEngine;

// 작은 필드 그림 위에 11명을 점으로 놓는다(10-29 ③d-2). 팀 전술 모달(미리보기 자리)과 포메이션 모달(템플릿 자리)이 같은 그림을 쓴다.
// 미터(팀 0 좌표, +X 공격) → 필드 그림 안 0~1 앵커. 가로 = 진영 축 X(왼쪽 = 우리 골), 세로 = 폭 Z. PitchView 격자와 같은 방향.
// 자리를 어디서 계산했는지는 모른다: 받은 좌표와 글자를 놓기만 한다
public class PitchDots : MonoBehaviour
{
    [SerializeField] private RectTransform[] dots;   // 11개. dots[i] = positions[i]
    [SerializeField] private TMP_Text[] labels;      // dots와 같은 순서

    private bool wiringChecked;

    public void Show(IReadOnlyList<(float x, float z)> positions, IReadOnlyList<string> names)
    {
        CheckWiring();
        for (int i = 0; i < dots.Length; i++)
        {
            bool used = i < positions.Count;
            dots[i].gameObject.SetActive(used);
            if (!used) { continue; }

            var anchor = new Vector2(
                (positions[i].x + FieldBounds.HalfLength) / (FieldBounds.HalfLength * 2f),
                (positions[i].z + FieldBounds.HalfWidth) / (FieldBounds.HalfWidth * 2f));
            dots[i].anchorMin = anchor;
            dots[i].anchorMax = anchor;
            dots[i].anchoredPosition = Vector2.zero;
            labels[i].text = names[i];
        }
    }

    private void CheckWiring()
    {
        if (wiringChecked) { return; }
        wiringChecked = true;
        if (dots.Length != FormationTemplate.SlotCount || labels.Length != dots.Length)
        {
            Debug.LogError($"[PitchDots] {name}: 점 {dots.Length}·이름 {labels.Length}개 ≠ 선발 {FormationTemplate.SlotCount}명. 프리팹 배선을 확인한다");
        }
    }
}
