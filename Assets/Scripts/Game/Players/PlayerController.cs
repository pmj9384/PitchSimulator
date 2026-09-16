using Game.Core.AI;
using Game.Core.Data;
using UnityEngine;

// 선수 1명의 실체. 스폰 순번·팀·역할 프리셋을 들고 필드에 선다.
// 1차 이식(09-16)은 "서 있음"까지. 트리가 묻고 시키는 것(IPlayerContext 멤버)은 공·소유 설계 뒤 여기서 구현한다.
public class PlayerController : MonoBehaviour, IPlayerContext
{
    public int SpawnIndex { get; private set; }   // 자체 발급 순번. 타이브레이크·명부 조회의 열쇠
    public int Team { get; private set; }         // 0 = 플레이어, 1 = 상대
    public PlayerStats Stats { get; private set; }

    public void Setup(int spawnIndex, int team, PlayerStats stats, Vector3 position)
    {
        SpawnIndex = spawnIndex;
        Team = team;
        Stats = stats;
        transform.position = position;
        name = $"Player_{team}_{stats.RoleId}_{spawnIndex}";   // 하이어라키에서 바로 읽히게
    }
}
