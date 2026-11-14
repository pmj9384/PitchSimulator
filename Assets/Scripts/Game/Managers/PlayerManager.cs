using System;
using System.Collections.Generic;
using Game.Core.Match;
using UnityEngine;
using UnityEngine.Pool;

// 선수 뷰의 수명 관리자. 풀 대여/반환·팀별 명부. 이 두 가지가 전부다.
// 경기 상태(PlayerState·선수 번호)는 시뮬 조립(MyMatchAssembler)이 만든다. 여기는 이미 있는 상태에 몸을 붙일 뿐 상태를 만들거나 등록하지 않는다(11-06 S4-1).
// 경기 진행은 MatchManager, 편성은 StageManager 몫. 여기는 "누가 필드에 있는가"의 명부만 안다.
// 프리팹은 한 종류(Quaternius UBC 몸, 11-14 교체). 겉모습 차이 = 머리 칸 하나 + 체형 메시 + 머티리얼(유니폼 KitRules, 피부·머리색·체형 PortraitRules). 색은 PlayerPalette
public class PlayerManager : InGameManager
{
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Material lookTemplate;    // 단색 머티리얼 원본(URP Lit). 유니폼·머리색·신발은 이걸 복사해 색만 바꾼다
    [SerializeField] private Material skinTemplate;    // 피부 원본(URP Lit + 피부 밝기 무늬 텍스처). 색을 곱해 피부 4종을 만든다. 얼굴 그림을 남기려고 단색과 따로 둔다(11-14 결정 B10)

    private ObjectPool<GameObject> pool;
    private readonly List<PlayerController>[] rosters = { new List<PlayerController>(), new List<PlayerController>() };
    private readonly Dictionary<int, PlayerController> byId = new Dictionary<int, PlayerController>();
    private readonly Dictionary<string, Material> looks = new Dictionary<string, Material>();   // "kit:3"·"skin:1"·"hair:4"·"boots" → 머티리얼. 색마다 1개를 22명이 공유

    // state = 시뮬에 이미 들어 있는 선수(MatchManager.Simulation.Players). 컨트롤러는 그 값을 비추는 뷰
    // portrait = 머리 번호(LineupSlot.Portrait), kit = 유니폼 번호(MatchSetup.Kit0/Kit1)
    public PlayerController Spawn(PlayerState state, int portrait, int kit)
    {
        if (state == null)
        {
            // 풀에서 빌리기 전에 막는다. 빌린 뒤 터지면 몸이 반환되지 않고 NRE로 데이터 오류가 가려진다
            throw new InvalidOperationException("[PlayerManager] state가 null이다");
        }

        GameObject body = Pool().Get();

        PlayerController player = body.GetComponent<PlayerController>();
        player.Setup(state);
        ApplyLook(body, portrait, kit);

        rosters[state.Team].Add(player);
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

        if (!PlayerPalette.Matches()) { Debug.LogError("[PlayerManager] PlayerPalette 표 길이가 KitRules·PortraitRules 상수와 다르다"); }
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
        if (view == null)
        {
            Debug.LogError("[PlayerManager] 선수 프리팹에 PlayerView가 없다. 기본 머티리얼로 나온다");
            return;
        }
        if (!PortraitRules.IsValid(portrait))
        {
            Debug.LogError($"[PlayerManager] 겉모습 번호 범위 밖: {portrait}");
            portrait = PortraitRules.Of(0);
        }
        int skin = PortraitRules.SkinOf(portrait);
        int hair = PortraitRules.HairOf(portrait);
        view.ApplyLook(PortraitRules.HeadOf(portrait), PortraitRules.BodyOf(portrait),
            LookMaterial("kit", kit, PlayerPalette.Kit(kit), lookTemplate),
            LookMaterial("skin", skin, PlayerPalette.Skin(skin), skinTemplate),
            LookMaterial("hair", hair, PlayerPalette.Hair(hair), lookTemplate),
            LookMaterial("boots", 0, PlayerPalette.Boots, lookTemplate));
    }

    private Material LookMaterial(string kind, int index, Color color, Material template)
    {
        string key = $"{kind}:{index}";
        Material material;
        if (looks.TryGetValue(key, out material)) { return material; }

        material = new Material(template);
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
        // new Material은 씬이 내려가도 안 사라진다(에셋 취급). 만든 쪽이 지운다
        foreach (Material material in looks.Values) { Destroy(material); }
        looks.Clear();
    }
}
