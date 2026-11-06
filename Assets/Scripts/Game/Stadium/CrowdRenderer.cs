using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Placement;
using UnityEngine;
using UnityEngine.Rendering;

// 관중(11-06 경기장 4종 4단계, 설계 §3). 경기장 프리팹 루트에 붙고 매니저를 모른다: Init(스펙, 채움 시드, 홈 유니폼 색)만 받는다.
// 채운 좌석마다 쿼드 하나(경기장 가운데를 보는 고정 방향)를 면별 Matrix4x4 배열로 Init에서 한 번 만들어 두고,
// 프레임마다 Graphics.RenderMeshInstanced로 1,023개씩 그린다. 프레임 안에서 새로 만드는 것이 없다(배열·RenderParams 전부 Init에서).
// 면마다 worldBounds가 따로라 화면 밖 면(가까운 쪽)은 호출 자체가 컬링된다. 그림자 드리움·받음 없음.
// 경기장은 안 움직이는 것으로 본다: 행렬은 Init 때의 루트 변환으로 굳힌다
// ExecuteAlways인 이유: EditMode 테스트의 DestroyImmediate에서도 OnDestroy → Release가 돌아 Mesh·Material이 새지 않게 하려고
[ExecuteAlways]
public class CrowdRenderer : MonoBehaviour
{
    private const float QuadWidth = 0.55f;    // [가정] 앉은 사람 한 명의 폭(m). 좌석 간격 0.5보다 조금 넓어 옆 사람과 어깨가 겹친다
    private const float QuadHeight = 1.1f;    // [가정] 앉은 사람의 상반신 높이(m). 바로 뒷단(rowHeight만큼 높음)에 가려지는 아랫부분은 어차피 안 보인다
    private const float BoundsPadding = 1f;   // 면 경계에 두르는 여유(m)
    private const string HomeColorProperty = "_HomeColor";

    private struct SideBatch
    {
        public RenderParams Params;
        public Matrix4x4[] Matrices;
    }

    [SerializeField] private Material sourceMaterial;

    private Mesh mesh;
    private Material material;
    private SideBatch[] batches;

    public int TotalInstances { get; private set; }
    public int DrawCalls { get; private set; }

    public int InstancesOf(int sideIndex)
    {
        if (batches == null) { return 0; }

        return batches[sideIndex].Matrices.Length;
    }

    public int SideCount => batches == null ? 0 : batches.Length;

    public void Init(StadiumSpec spec, uint fillSeed, Color homeKit)
    {
        Release();
        if (sourceMaterial == null)
        {
            Debug.LogError("[Crowd] 관중 머티리얼이 비어 있다. Tools/Pitch/경기장 프리팹 만들기를 실행한다");
            return;
        }

        mesh = CreateQuad();
        material = new Material(sourceMaterial);
        material.enableInstancing = true;
        material.SetColor(HomeColorProperty, homeKit);

        var filled = CrowdLayout.FilledSeats(spec, fillSeed);
        batches = new SideBatch[spec.Stands.Count];
        TotalInstances = 0;
        DrawCalls = 0;
        for (int s = 0; s < spec.Stands.Count; s++)
        {
            batches[s] = BuildSide(spec.Stands[s], filled);
            TotalInstances += batches[s].Matrices.Length;
            DrawCalls += CrowdLayout.CallCount(batches[s].Matrices.Length);
        }
    }

    private SideBatch BuildSide(StandSide side, List<SeatSlot> filled)
    {
        int count = 0;
        for (int i = 0; i < filled.Count; i++)
        {
            if (filled[i].Side == side) { count++; }
        }

        var matrices = new Matrix4x4[count];
        Quaternion rotation = Quaternion.Euler(0f, CrowdLayout.FacingYaw(side), 0f);
        Matrix4x4 root = transform.localToWorldMatrix;
        int n = 0;
        for (int i = 0; i < filled.Count; i++)
        {
            SeatSlot seat = filled[i];
            if (seat.Side != side) { continue; }

            Matrix4x4 local = Matrix4x4.TRS(new Vector3(seat.X, seat.Y, seat.Z), rotation, Vector3.one);
            matrices[n] = root * local;
            n++;
        }
        Bounds bounds = WorldBounds(CrowdLayout.SideBox(filled, side, QuadHeight, BoundsPadding), root);

        var renderParams = new RenderParams(material)
        {
            worldBounds = bounds,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            layer = gameObject.layer,
        };
        return new SideBatch { Params = renderParams, Matrices = matrices };
    }

    // 로컬 상자의 8모서리를 루트 변환해 월드 AABB로 감싼다(루트가 회전해도 좌석을 다 덮는다)
    private static Bounds WorldBounds(CrowdLayout.SeatBox box, Matrix4x4 root)
    {
        Bounds bounds = default;
        for (int i = 0; i < 8; i++)
        {
            float x = (i & 1) == 0 ? box.MinX : box.MaxX;
            float y = (i & 2) == 0 ? box.MinY : box.MaxY;
            float z = (i & 4) == 0 ? box.MinZ : box.MaxZ;
            Vector3 corner = root.MultiplyPoint3x4(new Vector3(x, y, z));
            if (i == 0) { bounds = new Bounds(corner, Vector3.zero); }
            else { bounds.Encapsulate(corner); }
        }
        return bounds;
    }

    // 아래 가운데가 원점인 세로 쿼드. 보이는 면은 -Z. 크기를 메시에 구워 두면 행렬이 균등 스케일(1)이라 assumeuniformscaling을 쓸 수 있다
    private static Mesh CreateQuad()
    {
        var quad = new Mesh { name = "CrowdQuad" };
        float half = QuadWidth * 0.5f;
        quad.vertices = new[]
        {
            new Vector3(-half, 0f, 0f),
            new Vector3(half, 0f, 0f),
            new Vector3(half, QuadHeight, 0f),
            new Vector3(-half, QuadHeight, 0f),
        };
        quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        quad.UploadMeshData(true);
        return quad;
    }

    private void LateUpdate()
    {
        if (batches == null) { return; }

        for (int s = 0; s < batches.Length; s++)
        {
            DrawSide(batches[s]);
        }
    }

    private void DrawSide(SideBatch batch)
    {
        int total = batch.Matrices.Length;
        int calls = CrowdLayout.CallCount(total);
        for (int c = 0; c < calls; c++)
        {
            Graphics.RenderMeshInstanced(batch.Params, mesh, 0, batch.Matrices, CrowdLayout.CallSize(total, c), c * CrowdLayout.MaxInstancesPerCall);
        }
    }

    // 정리. 메시·머티리얼은 코드로 만든 것이라 직접 지운다
    private void Release()
    {
        batches = null;
        TotalInstances = 0;
        DrawCalls = 0;
        DestroyCreated(mesh);
        DestroyCreated(material);
        mesh = null;
        material = null;
    }

    private static void DestroyCreated(Object target)
    {
        if (target == null) { return; }

        if (Application.isPlaying) { Destroy(target); }
        else { DestroyImmediate(target); }
    }

    private void OnDestroy()
    {
        Release();
    }
}
