using UnityEngine;

// 선수의 겉모습 담당. 컨트롤러는 경기만 알고, 팀 색은 여기서 입힌다.
public class PlayerView : MonoBehaviour
{
    [SerializeField] private Renderer bodyRenderer;

    public void ApplyTeam(Material teamMaterial)
    {
        if (bodyRenderer == null) { return; }
        bodyRenderer.sharedMaterial = teamMaterial;   // 인스턴스 머티리얼을 만들지 않는다. 22명이 2개를 공유
    }
}
