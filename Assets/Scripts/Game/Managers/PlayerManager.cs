using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Match;
using UnityEngine;
using UnityEngine.Pool;

// 선수의 수명 관리자. 선수 번호 발급·풀 대여/반환·팀별 명부. 이 세 가지가 전부다.
// 경기 진행은 MatchManager, 편성은 StageManager 몫. 여기는 "누가 필드에 있는가"의 명부만 안다.
// 프리팹은 한 종류(Quaternius 모듈러 몸, 10-21). 겉모습 차이 = 머리 메시 하나 + 머티리얼(유니폼 KitRules, 피부·머리색 PortraitRules). 색은 PlayerPalette
public class PlayerManager : InGameManager
{
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Material lookTemplate;    // 단색 머티리얼 원본(URP Lit). 유니폼·피부·머리색·신발 전부 이걸 복사해 색만 바꾼다

    private ObjectPool<GameObject> pool;
    private readonly List<PlayerController>[] rosters = { new List<PlayerController>(), new List<PlayerController>() };
    private readonly Dictionary<int, PlayerController> byId = new Dictionary<int, PlayerController>();
    private int nextPlayerId;     // 스폰 순서로 발급하는 선수 번호. 타이브레이크 기준(GetInstanceID 금지)
    private readonly Dictionary<string, Material> looks = new Dictionary<string, Material>();   // "kit:3"·"skin:1"·"hair:4"·"boots" → 머티리얼. 색마다 1개를 22명이 공유

    // attackHome = 아군 소유 때 자리(킥오프 위치이기도 하다), defendHome = 상대 소유 때 자리(스펙 §4-3 자리 2쌍)
    // stats는 호출자가 준다: 시즌 로스터·부 총점으로 축소한 상대 빌드는 PlayerTable에 없는 사본이라 variantId 조회로는 못 세운다
    // portrait = 머리 번호(LineupSlot.Portrait), kit = 유니폼 번호(MatchSetup.Kit0/Kit1)
    public PlayerController Spawn(PlayerStats stats, int team, Vector3 attackHome, Vector3 defendHome, int portrait, int kit)
    {
        if (stats == null)
        {
            // 풀에서 빌리기 전에 막는다. 빌린 뒤 터지면 몸이 반환되지 않고 NRE로 데이터 오류가 가려진다
            throw new InvalidOperationException("[PlayerManager] stats가 null이다");
        }

        GameObject body = Pool().Get();

        // 경기 상태는 순수 PlayerState가 갖고 시뮬(MatchManager)에 등록한다. 컨트롤러는 그 값을 비추는 뷰
        var state = new PlayerState(nextPlayerId++, team, stats, attackHome.x, attackHome.z, defendHome.x, defendHome.z);
        GameManager.Match.Register(state);

        PlayerController player = body.GetComponent<PlayerController>();
        player.Setup(state);
        ApplyLook(body, portrait, kit);

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

    private void ApplyLook(GameObject body, int portrait, int kit)
    {
        PlayerView view = body.GetComponent<PlayerView>();
        if (view == null) { return; }
        if (!PortraitRules.IsValid(portrait))
        {
            Debug.LogError($"[PlayerManager] 겉모습 번호 범위 밖: {portrait}");
            portrait = PortraitRules.Of(0);
        }
        if (!PlayerPalette.Matches()) { Debug.LogError("[PlayerManager] PlayerPalette 표 길이가 KitRules·PortraitRules 상수와 다르다"); }
        view.ApplyLook(PortraitRules.HeadOf(portrait),
            LookMaterial("kit", kit, PlayerPalette.Kit(kit)),
            LookMaterial("skin", PortraitRules.SkinOf(portrait), PlayerPalette.Skin(PortraitRules.SkinOf(portrait))),
            LookMaterial("hair", PortraitRules.HairOf(portrait), PlayerPalette.Hair(PortraitRules.HairOf(portrait))),
            LookMaterial("boots", 0, PlayerPalette.Boots));
    }

    private Material LookMaterial(string kind, int index, Color color)
    {
        string key = $"{kind}:{index}";
        Material material;
        if (looks.TryGetValue(key, out material)) { return material; }

        material = new Material(lookTemplate);
        material.name = key;
        material.color = color;
        looks[key] = material;
        return material;
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
