using UnityEngine;

// 선수의 겉모습 담당. 컨트롤러는 경기만 알고, 유니폼·얼굴은 여기서 입힌다.
// 모델 = Kenney Blocky Characters(10-22): 머리 렌더러는 얼굴 텍스처 머티리얼, 몸통·팔·다리는 유니폼 머티리얼. 메시는 전원 같고 머티리얼만 다르다
public class PlayerView : MonoBehaviour
{
    [SerializeField] private Renderer headRenderer;
    [SerializeField] private Renderer[] bodyRenderers;

    // 인스턴스 머티리얼을 만들지 않는다. 유니폼은 팀당 1개, 얼굴은 번호당 1개를 22명이 공유
    public void ApplyLook(Material kitMaterial, Material faceMaterial)
    {
        if (headRenderer != null) { headRenderer.sharedMaterial = faceMaterial; }
        if (bodyRenderers == null) { return; }
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            bodyRenderers[i].sharedMaterial = kitMaterial;
        }
    }
}
