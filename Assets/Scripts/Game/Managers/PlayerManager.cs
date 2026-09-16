using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Match;
using UnityEngine;
using UnityEngine.Pool;

// 선수의 수명 관리자. 선수 번호 발급·풀 대여/반환·팀별 명부. 이 세 가지가 전부다.
// 경기 진행은 MatchManager, 편성은 StageManager 몫. 여기는 "누가 필드에 있는가"의 명부만 안다.
// 1차 이식(09-16): 프리팹은 한 종류(캡슐). 역할별 겉모습이 생기면 WTS처럼 roleId별 프리팹 캐시로 넓힌다.
public class PlayerManager : InGameManager
{
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Material homeMaterial;   // 팀 0 = 플레이어
    [SerializeField] private Material awayMaterial;   // 팀 1 = 상대

    private ObjectPool<GameObject> pool;
    private readonly List<PlayerController>[] rosters = { new List<PlayerController>(), new List<PlayerController>() };
    private readonly Dictionary<int, PlayerController> byId = new Dictionary<int, PlayerController>();
    private int nextPlayerId;     // 스폰 순서로 발급하는 선수 번호. 타이브레이크의 근원(GetInstanceID 금지)

    public PlayerController Spawn(string roleId, int team, Vector3 position)
    {
        PlayerStats stats = PlayerTableRepository.Get(roleId);
        if (stats == null)
        {
            // 풀에서 빌리기 전에 막는다. 빌린 뒤 터지면 몸이 반환되지 않고 NRE로 데이터 오류가 가려진다
            throw new InvalidOperationException($"[PlayerManager] PlayerTable에 없는 roleId: {roleId}");
        }

        GameObject body = Pool().Get();

        // 경기 상태는 순수 PlayerState가 갖고 시뮬(MatchManager)에 등록한다. 컨트롤러는 그 값을 비추는 뷰
        var state = new PlayerState(nextPlayerId++, team, stats, position.x, position.z);
        GameManager.Match.Register(state);

        PlayerController player = body.GetComponent<PlayerController>();
        player.Setup(state);
        ApplyTeamColor(body, team);

        rosters[team].Add(player);
        byId[player.PlayerId] = player;
        return player;
    }

    public void Despawn(PlayerController player)
    {
        rosters[player.Team].Remove(player);
        byId.Remove(player.PlayerId);
        Pool().Release(player.gameObject);
    }

    public IReadOnlyList<PlayerController> Roster(int team)
    {
        return rosters[team];
    }

    // 선수 번호로 실체를 찾는다. 없으면(이미 반환됐으면) null
    public PlayerController Find(int playerId)
    {
        PlayerController player;
        if (byId.TryGetValue(playerId, out player)) { return player; }
        return null;
    }

    private ObjectPool<GameObject> Pool()
    {
        if (pool != null) { return pool; }

        pool = GameManager.ObjectPool.CreateObjectPool(playerPrefab, CreateBody, OnGetFromPool, OnReleaseToPool);
        return pool;
    }

    private GameObject CreateBody()
    {
        return Instantiate(playerPrefab);
    }

    private void ApplyTeamColor(GameObject body, int team)
    {
        PlayerView view = body.GetComponent<PlayerView>();
        if (view == null) { return; }
        view.ApplyTeam(team == 0 ? homeMaterial : awayMaterial);
    }

    private void OnGetFromPool(GameObject body)
    {
        body.SetActive(true);
    }

    private void OnReleaseToPool(GameObject body)
    {
        body.SetActive(false);
    }

    // 씬이 내려갈 때만 불린다. 빌린 몸은 씬과 함께 사라지므로 풀에 돌려보내지 않는다
    public override void Clear()
    {
        rosters[0].Clear();
        rosters[1].Clear();
        byId.Clear();
        nextPlayerId = 0;
    }
}
