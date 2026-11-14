using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.Placement;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 저장된 경기장 프리팹 4종이 Stadiums.csv·StadiumLayout과 같은지(11-06 2단계), 잔디 머티리얼·텍스처 임포트가 CSV·모바일 설정과 같은지(11-06 3단계). 어긋나면 Tools/Pitch/경기장 프리팹 만들기로 다시 만든다.
// UnityEditor를 쓰므로 Unity EditMode에서만 돈다(dotnet 러너 제외)
public class StadiumPrefabTests
{
    private const float Eps = 1e-3f;
    private const string CsvPath = "Assets/Resources/Tables/Stadiums.csv";
    private const string CommonPitchPath = "Assets/Prefabs/Pitch/CommonPitch.prefab";
    private const string StandsName = "Stands";
    private const string MaterialFolder = "Assets/Materials/Stadium/";
    private const float UnityPlaneSize = 10f;

    private static List<StadiumSpec> Specs()
    {
        return StadiumSpecParser.Parse(File.ReadAllText(CsvPath));
    }

    private static GameObject Load(StadiumSpec spec)
    {
        string path = $"Assets/Resources/Stadiums/{spec.Id}.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, $"{path} 없음");
        return prefab;
    }

    [Test]
    public void 단_큐브_수와_자리와_크기가_StadiumLayout과_같다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            Transform stands = Load(spec).transform.Find(StandsName);
            Assert.IsNotNull(stands, $"{spec.Id} Stands 없음");
            List<StandRow> rows = StadiumLayout.Rows(spec);
            int cubes = 0;
            foreach (Transform t in stands.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Stand_")) { cubes++; }
            }
            Assert.AreEqual(rows.Count, cubes, $"{spec.Id} 단 수");
            for (int i = 0; i < rows.Count; i++)
            {
                StandRow row = rows[i];
                Transform t = stands.Find($"{row.Side}/Stand_{row.Side}_{row.Index:00}_{row.Piece}");   // StadiumPrefabBuilder.RowName과 같은 형식
                Assert.IsNotNull(t, $"{spec.Id} {row.Side} {row.Index}단 조각 {row.Piece} 없음");
                Assert.AreEqual((row.FromX + row.ToX) * 0.5f, t.localPosition.x, Eps, $"{spec.Id} {row.Side}{row.Index} x");
                Assert.AreEqual((row.FromZ + row.ToZ) * 0.5f, t.localPosition.z, Eps, $"{spec.Id} {row.Side}{row.Index} z");
                Assert.AreEqual(row.Height, t.localScale.y, Eps, $"{spec.Id} {row.Side}{row.Index} 높이");
                Assert.AreEqual(row.Height * 0.5f, t.localPosition.y, Eps, $"{spec.Id} {row.Side}{row.Index} 바닥이 지면");
                float length = Mathf.Abs(row.ToX - row.FromX) + Mathf.Abs(row.ToZ - row.FromZ);
                float along = StadiumLayout.IsAlongX(row.Side) ? t.localScale.x : t.localScale.z;
                float depth = StadiumLayout.IsAlongX(row.Side) ? t.localScale.z : t.localScale.x;
                Assert.AreEqual(length, along, Eps, $"{spec.Id} {row.Side}{row.Index} 길이");
                Assert.AreEqual(spec.RowDepth, depth, Eps, $"{spec.Id} {row.Side}{row.Index} 깊이");
            }
        }
    }

    [Test]
    public void CommonPitch가_중첩_프리팹으로_들어_있다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            Transform pitch = Load(spec).transform.Find("CommonPitch");
            Assert.IsNotNull(pitch, $"{spec.Id} CommonPitch 없음");
            Assert.IsTrue(PrefabUtility.IsAnyPrefabInstanceRoot(pitch.gameObject), $"{spec.Id} 중첩 프리팹이 아니다");
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(pitch.gameObject);
            Assert.AreEqual(CommonPitchPath, AssetDatabase.GetAssetPath(source), $"{spec.Id} 원본 경로");
        }
    }

    [Test]
    public void 지붕_수와_조명탑_전광판_광고판_수가_스펙과_같다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            Transform root = Load(spec).transform;
            Assert.AreEqual(spec.Roof.Count, CountPrefix(root, "Roofs", "Roof_"), $"{spec.Id} 지붕");
            Assert.AreEqual(spec.LightTowers, CountPrefix(root, "Towers", "Tower"), $"{spec.Id} 조명탑");
            Assert.AreEqual(spec.Scoreboards, CountPrefix(root, "Scoreboards", "Scoreboard"), $"{spec.Id} 전광판");
            Assert.AreEqual(AdBoardCount(spec), CountPrefix(root, "AdBoards", "AdBoard_"), $"{spec.Id} 광고판");
            Assert.AreEqual(2, CountPrefix(root, "Benches", "Bench"), $"{spec.Id} 벤치");
        }
    }

    [Test]
    public void 콜라이더가_없고_그림자를_드리우지_않는다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            GameObject prefab = Load(spec);
            Assert.AreEqual(0, prefab.GetComponentsInChildren<Collider>(true).Length, $"{spec.Id} 콜라이더");
            Transform stands = prefab.transform.Find(StandsName);
            foreach (MeshRenderer r in stands.GetComponentsInChildren<MeshRenderer>(true))
            {
                Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode, $"{spec.Id} {r.name} 그림자 드리움");
                Assert.IsFalse(r.receiveShadows, $"{spec.Id} {r.name} 그림자 받음");
                Assert.AreEqual((StaticEditorFlags)0, GameObjectUtility.GetStaticEditorFlags(r.gameObject), $"{spec.Id} {r.name} 정적 배칭 안 씀");
            }
            Transform roofs = prefab.transform.Find("Roofs");
            if (roofs == null) { continue; }
            foreach (MeshRenderer r in roofs.GetComponentsInChildren<MeshRenderer>(true))
            {
                Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode, $"{spec.Id} {r.name} 그림자 드리움");
            }
        }
    }

    [Test]
    public void 잔디_평면이_선_밖_런오프까지_덮는다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            Transform grass = Load(spec).transform.Find("Grass");
            Assert.IsNotNull(grass, $"{spec.Id} Grass 없음");
            float width = FieldBounds.HalfLength * 2f + spec.Runoff * 2f;
            float depth = FieldBounds.HalfWidth * 2f + spec.Runoff * 2f;
            Assert.AreEqual(width / UnityPlaneSize, grass.localScale.x, Eps, $"{spec.Id} 잔디 길이");
            Assert.AreEqual(depth / UnityPlaneSize, grass.localScale.z, Eps, $"{spec.Id} 잔디 폭");
        }
    }

    // 잔디·CommonPitch 밖의 렌더러 재질: 구조물 4개(StadiumLight·Dark·Ground·Tunnel) + 광고판 LedBoard 1개 + 테크니컬 에어리어 선(CommonPitch와 같은 FieldLine) 1개. 경기장 재질은 인스턴싱을 끈다(SRP Batcher와 겹치지 않게)
    [Test]
    public void 경기장_구조물은_재질_4개와_LED와_선_재질만_쓰고_인스턴싱을_끈다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            Transform root = Load(spec).transform;
            var structure = new HashSet<Material>();
            var all = new HashSet<Material>();
            foreach (Transform child in root)
            {
                if (child.name == "Grass") { continue; }
                if (child.name == "CommonPitch") { continue; }

                foreach (MeshRenderer r in child.GetComponentsInChildren<MeshRenderer>(true))
                {
                    all.Add(r.sharedMaterial);
                    if (IsLineOrLed(r)) { continue; }

                    structure.Add(r.sharedMaterial);
                }
            }
            // 구조물 = 밝은색·어두운색 + 바깥 바닥·통로 홈 벽(11-10 유저 승인). 넷 다 URP Lit 하나에 색만 달라 SRP Batcher 호환은 그대로
            Assert.AreEqual(4, structure.Count, $"{spec.Id} 구조물 재질 수");
            Assert.AreEqual(6, all.Count, $"{spec.Id} 전체 재질 수(구조물 4 + LED 1 + 선 1)");
            foreach (Material m in all)
            {
                if (m.name == "FieldLine") { continue; }

                StringAssert.StartsWith(MaterialFolder, AssetDatabase.GetAssetPath(m), $"{spec.Id} {m.name} 경로");
                Assert.IsFalse(m.enableInstancing, $"{spec.Id} {m.name} 인스턴싱");
            }
        }
    }

    // 11-10: 관중석 없는 면에 테라스를 세우면서 지면 전광판이 테라스 단·지붕을 뚫었다(리뷰 🔴). 전광판은 테라스·구석 블록과 부피가 겹치지 않는다
    [Test]
    public void 전광판은_테라스와_구석_블록을_뚫지_않는다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            GameObject root = Load(spec);
            Transform boards = root.transform.Find("Scoreboards");
            Transform outer = root.transform.Find("OuterBounds");
            if (boards == null) { continue; }

            Assert.IsNotNull(outer, $"{spec.Id} OuterBounds 없음");
            foreach (MeshRenderer board in boards.GetComponentsInChildren<MeshRenderer>(true))
            {
                foreach (MeshRenderer part in outer.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!part.name.StartsWith("Terrace") && !part.name.StartsWith("Corner")) { continue; }

                    Assert.IsFalse(Overlaps(board.bounds, part.bounds), $"{spec.Id} {board.name}가 {part.name}과 겹친다");
                }
            }
        }
    }

    // 맞닿은 면은 겹침으로 보지 않는다(부동소수 여유 1cm)
    private static bool Overlaps(Bounds a, Bounds b)
    {
        const float Margin = 0.01f;
        if (a.max.x - Margin <= b.min.x || b.max.x - Margin <= a.min.x) { return false; }
        if (a.max.y - Margin <= b.min.y || b.max.y - Margin <= a.min.y) { return false; }
        if (a.max.z - Margin <= b.min.z || b.max.z - Margin <= a.min.z) { return false; }
        return true;
    }

    private static bool IsLineOrLed(MeshRenderer r)
    {
        return r.sharedMaterial.name == "FieldLine" || r.sharedMaterial.name == "LedBoard";
    }

    // 면당 판 1장, 통로가 있으면 먼 쪽만 홈 앞에서 끊겨 2장(11-14)
    private static int AdBoardCount(StadiumSpec spec)
    {
        if (!StadiumLayout.HasTunnel(spec)) { return spec.AdBoards.Count; }

        for (int i = 0; i < spec.AdBoards.Count; i++)
        {
            if (spec.AdBoards[i] == StandSide.Far) { return spec.AdBoards.Count + 1; }
        }
        return spec.AdBoards.Count;
    }

    // 11-06 더그아웃·LED: 광고판은 면당 판 1장·LedBoard 재질(11-14부터 먼 쪽은 통로 앞에서 끊긴 2장)
    [Test]
    public void 광고판은_LedBoard_재질이고_렌더러가_면당_1개_먼_쪽은_통로로_2개다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            Transform boards = Load(spec).transform.Find("AdBoards");
            Assert.IsNotNull(boards, $"{spec.Id} AdBoards 없음");
            MeshRenderer[] renderers = boards.GetComponentsInChildren<MeshRenderer>(true);
            Assert.AreEqual(AdBoardCount(spec), renderers.Length, $"{spec.Id} 광고판 렌더러 수");
            foreach (MeshRenderer r in renderers)
            {
                Assert.AreEqual($"{MaterialFolder}LedBoard.mat", AssetDatabase.GetAssetPath(r.sharedMaterial), $"{spec.Id} {r.name} 재질");
                Assert.AreEqual("PitchSimulator/LedBoard", r.sharedMaterial.shader.name, $"{spec.Id} {r.name} 셰이더");
            }
        }
    }

    // 벤치·더그아웃 지붕은 먼 쪽(+Z, 중계 카메라가 -Z라 화면 위쪽), 4개 부 모두 지붕이 있다
    [Test]
    public void 벤치와_더그아웃_지붕이_먼_쪽에_있고_모든_부에_지붕이_있다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            Transform benches = Load(spec).transform.Find("Benches");
            Assert.IsNotNull(benches, $"{spec.Id} Benches 없음");
            Assert.AreEqual(2, CountPrefix(Load(spec).transform, "Benches", "Dugout"), $"{spec.Id} 더그아웃 지붕");
            foreach (Transform t in benches)
            {
                Assert.Greater(t.localPosition.z, FieldBounds.HalfWidth, $"{spec.Id} {t.name} 터치라인 바깥(+Z)");
                Assert.Less(t.localPosition.z, FieldBounds.HalfWidth + spec.Runoff * 0.75f, $"{spec.Id} {t.name} 광고판 선 쪽");
            }
        }
    }

    [Test]
    public void 테크니컬_에어리어_선이_벤치마다_사각형으로_있다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            Transform areas = Load(spec).transform.Find("TechnicalAreas");
            Assert.IsNotNull(areas, $"{spec.Id} TechnicalAreas 없음");
            Assert.AreEqual(2, areas.childCount, $"{spec.Id} 영역 수");
            foreach (Transform area in areas)
            {
                Assert.AreEqual(4, area.childCount, $"{spec.Id} {area.name} 선 수");
                foreach (Transform line in area)
                {
                    Assert.Greater(line.localPosition.z, FieldBounds.HalfWidth, $"{spec.Id} {area.name} {line.name} 터치라인 바깥");
                    Assert.AreEqual("FieldLine", line.GetComponent<MeshRenderer>().sharedMaterial.name, $"{spec.Id} {line.name} 재질");
                }
            }
        }
    }

    // IFAB Law 1 테크니컬 에어리어: 앞 선은 터치라인에서 1m, 옆 선은 좌석 양끝에서 1m. 벤치는 앞 선을 덮지 않고, 지붕·뒤 선은 먼 쪽 광고판 앞면을 넘지 않는다
    [Test]
    public void 테크니컬_에어리어가_IFAB_간격이고_벤치와_지붕이_선과_광고판을_침범하지_않는다()
    {
        const float benchHalfLength = 2f;
        foreach (StadiumSpec spec in Specs())
        {
            Transform root = Load(spec).transform;
            Transform benches = root.Find("Benches");
            Transform areas = root.Find("TechnicalAreas");
            float frontLineZ = FieldBounds.HalfWidth + 1f;
            float adFrontZ = AdBoardFarFrontZ(root);
            for (int i = 0; i < 2; i++)
            {
                Transform bench = benches.Find($"Bench{i}");
                Transform dugout = benches.Find($"Dugout{i}");
                Transform area = areas.Find($"TechnicalArea{i}");
                float benchFrontZ = bench.localPosition.z - bench.localScale.z * 0.5f;
                Assert.Greater(benchFrontZ, frontLineZ + FieldBounds.LineWidth * 0.5f, $"{spec.Id} 벤치{i} 앞면이 앞 선 바깥");
                Assert.AreEqual(frontLineZ, area.Find("Line0").localPosition.z, Eps, $"{spec.Id} 영역{i} 앞 선 z");
                Assert.AreEqual(bench.localPosition.x - benchHalfLength - 1f, area.Find("Line2").localPosition.x, Eps, $"{spec.Id} 영역{i} 왼쪽 선 x");
                Assert.AreEqual(bench.localPosition.x + benchHalfLength + 1f, area.Find("Line3").localPosition.x, Eps, $"{spec.Id} 영역{i} 오른쪽 선 x");
                float roofBackZ = dugout.localPosition.z + dugout.localScale.z * 0.5f;
                Assert.Less(roofBackZ, adFrontZ, $"{spec.Id} 더그아웃{i} 지붕 뒤끝 < 광고판 앞면");
                Transform backLine = area.Find("Line1");
                float backLineOuterZ = backLine.localPosition.z + backLine.localScale.z * 0.5f;
                Assert.Less(backLineOuterZ, adFrontZ, $"{spec.Id} 영역{i} 뒤 선 < 광고판 앞면");
            }
        }
    }

    // 지금 CSV는 4개 부 모두 통로가 있어 먼 쪽 판이 끊긴 2장이다. −X 쪽 판으로 앞면 z를 잰다(두 장의 z는 같다)
    private static float AdBoardFarFrontZ(Transform root)
    {
        Transform board = root.Find("AdBoards/AdBoard_Far_0");
        Assert.IsNotNull(board, $"{root.name} AdBoard_Far_0 없음");
        return board.localPosition.z - board.localScale.z * 0.5f;
    }

    // 11-14 fix/tunnel-recess: 기단 앞 판(TunnelMouth)을 없애고 홈 지붕·뒤 벽·옆 벽을 TunnelOf 자리에 둔다. 먼 쪽 광고판·기단은 홈 폭을 비운다
    [Test]
    public void 통로_홈은_TunnelOf_자리에_있고_광고판과_기단이_홈_앞을_막지_않는다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            Transform root = Load(spec).transform;
            TunnelSpot tunnel = StadiumLayout.TunnelOf(spec);
            Transform parent = root.Find("Tunnel");
            Assert.IsNotNull(parent, $"{spec.Id} Tunnel 없음");
            Assert.IsNull(parent.Find("TunnelMouth"), $"{spec.Id} 옛 기단 앞 판이 남아 있다");
            Transform roof = parent.Find("TunnelRoof");
            Assert.IsNotNull(roof, $"{spec.Id} 홈 지붕 없음");
            Assert.AreEqual(tunnel.Height, roof.localPosition.y - roof.localScale.y * 0.5f, Eps, $"{spec.Id} 홈 지붕 아랫면");
            Assert.AreEqual(tunnel.RoofTop, roof.localPosition.y + roof.localScale.y * 0.5f, Eps, $"{spec.Id} 홈 지붕 윗면");
            Assert.AreEqual(tunnel.BackZ, roof.localPosition.z + roof.localScale.z * 0.5f, Eps, $"{spec.Id} 홈 지붕 뒤끝 = 뒤 벽");
            Transform back = parent.Find("TunnelBack");
            Assert.IsNotNull(back, $"{spec.Id} 뒤 벽 없음");
            Assert.AreEqual(tunnel.BackZ, back.localPosition.z + back.localScale.z * 0.5f, Eps, $"{spec.Id} 뒤 벽 z");
            Assert.AreEqual(-tunnel.HalfWidth, roof.localPosition.x - roof.localScale.x * 0.5f, Eps, $"{spec.Id} 홈 지붕 −X 끝 = 홈 폭");
            Assert.AreEqual(tunnel.HalfWidth, roof.localPosition.x + roof.localScale.x * 0.5f, Eps, $"{spec.Id} 홈 지붕 +X 끝 = 홈 폭");
            Assert.Less(roof.localPosition.z - roof.localScale.z * 0.5f, tunnel.MouthZ, $"{spec.Id} 홈 지붕이 입구 앞으로 차양처럼 나온다");
            for (int i = 0; i < 2; i++)
            {
                Transform wall = parent.Find($"TunnelWall_{i}");
                Assert.IsNotNull(wall, $"{spec.Id} 옆 벽 {i} 없음");
                Assert.AreEqual(tunnel.MouthZ, wall.localPosition.z - wall.localScale.z * 0.5f, Eps, $"{spec.Id} 옆 벽 {i} 앞끝 = 입구");
                Assert.AreEqual(tunnel.BackZ, wall.localPosition.z + wall.localScale.z * 0.5f, Eps, $"{spec.Id} 옆 벽 {i} 뒤끝 = 뒤 벽");
                Assert.LessOrEqual(Mathf.Abs(wall.localPosition.x) + wall.localScale.x * 0.5f, tunnel.HalfWidth + Eps, $"{spec.Id} 옆 벽 {i}가 홈 폭 안쪽");
            }

            foreach (Transform board in root.Find("AdBoards"))
            {
                if (!board.name.StartsWith("AdBoard_Far")) { continue; }

                Assert.IsTrue(ClearsTunnel(board, tunnel), $"{spec.Id} {board.name}가 통로 홈 앞을 막는다");
            }
            Transform farStands = root.Find($"{StandsName}/{StandSide.Far}");
            foreach (Transform part in farStands)
            {
                if (part.localPosition.z - part.localScale.z * 0.5f >= tunnel.BackZ - Eps) { continue; }   // 홈 뒤의 끊지 않은 단

                Assert.IsTrue(ClearsTunnel(part, tunnel), $"{spec.Id} {part.name}이 통로 홈을 덮는다");
            }
        }
    }

    // 상자가 x로 홈 폭(±HalfWidth) 바깥에만 있다(맞닿는 것은 허용)
    private static bool ClearsTunnel(Transform box, TunnelSpot tunnel)
    {
        float minX = box.localPosition.x - box.localScale.x * 0.5f;
        float maxX = box.localPosition.x + box.localScale.x * 0.5f;
        if (maxX <= -tunnel.HalfWidth + Eps) { return true; }

        return minX >= tunnel.HalfWidth - Eps;
    }

    // 코너에서 광고판끼리 관통하지 않는다(면이 맞닿는 것은 허용)
    [Test]
    public void 광고판끼리_코너에서_겹치지_않는다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            Transform boards = Load(spec).transform.Find("AdBoards");
            var bounds = new List<Bounds>();
            foreach (Transform board in boards)
            {
                var b = new Bounds(board.localPosition, board.localScale);
                b.Expand(-Eps * 2f);
                bounds.Add(b);
            }
            for (int i = 0; i < bounds.Count; i++)
            {
                for (int j = i + 1; j < bounds.Count; j++)
                {
                    Assert.IsFalse(bounds[i].Intersects(bounds[j]), $"{spec.Id} 광고판 {i}·{j} 겹침");
                }
            }
        }
    }

    // 11-06 3단계: 부별 잔디 머티리얼(_Wear·_Stripe = Stadiums.csv), 4개 모두 같은 셰이더(SRP Batcher가 한 배치로 묶는 조건)
    [Test]
    public void 부별_프리팹_잔디가_그_부_머티리얼을_쓰고_마모와_줄무늬가_CSV와_같다()
    {
        foreach (StadiumSpec spec in Specs())
        {
            var renderer = Load(spec).transform.Find("Grass").GetComponent<MeshRenderer>();
            Material material = renderer.sharedMaterial;
            Assert.IsNotNull(material, $"{spec.Id} 잔디 재질 없음");
            Assert.AreEqual($"{MaterialFolder}Grass_t{spec.Tier}.mat", AssetDatabase.GetAssetPath(material), $"{spec.Id} 잔디 재질 경로");
            Assert.AreEqual(spec.Wear, material.GetFloat("_Wear"), Eps, $"{spec.Id} _Wear");
            Assert.IsTrue(material.HasProperty("_Stripe"), $"{spec.Id} 셰이더에 _Stripe 있음(없으면 GetFloat가 0을 돌려준다)");
            Assert.AreEqual(spec.Stripe, material.GetFloat("_Stripe"), Eps, $"{spec.Id} _Stripe");
            Assert.AreEqual(1, renderer.sharedMaterials.Length, $"{spec.Id} 잔디 재질 슬롯(드로우 1회)");
        }
    }

    // 셰이더는 uv × _PlaneSize로 미터 좌표를 만들고 _MaskSize로 마스크를 읽는다. 인스펙터에서 값이 어긋나면 마모 자리가 박스와 안 맞는다
    [Test]
    public void 잔디_머티리얼의_평면_크기는_프리팹_잔디_크기와_같고_마스크_크기는_4개가_같다()
    {
        float maxRunoff = 0f;
        foreach (StadiumSpec spec in Specs())
        {
            maxRunoff = Mathf.Max(maxRunoff, spec.Runoff);
        }
        float maskX = FieldBounds.HalfLength * 2f + maxRunoff * 2f;
        float maskZ = FieldBounds.HalfWidth * 2f + maxRunoff * 2f;
        foreach (StadiumSpec spec in Specs())
        {
            Transform grass = Load(spec).transform.Find("Grass");
            Material material = grass.GetComponent<MeshRenderer>().sharedMaterial;
            Vector4 plane = material.GetVector("_PlaneSize");
            Vector4 mask = material.GetVector("_MaskSize");
            Assert.AreEqual(grass.localScale.x * UnityPlaneSize, plane.x, Eps, $"{spec.Id} _PlaneSize x");
            Assert.AreEqual(grass.localScale.z * UnityPlaneSize, plane.y, Eps, $"{spec.Id} _PlaneSize z");
            Assert.AreEqual(maskX, mask.x, Eps, $"{spec.Id} _MaskSize x(가장 큰 런오프 평면)");
            Assert.AreEqual(maskZ, mask.y, Eps, $"{spec.Id} _MaskSize z");
            Assert.IsNotNull(material.GetTexture("_GoodTex"), $"{spec.Id} 좋은 잔디 텍스처");
            Assert.IsNotNull(material.GetTexture("_DryTex"), $"{spec.Id} 마른 잔디 텍스처");
            Assert.IsNotNull(material.GetTexture("_WearMask"), $"{spec.Id} 마모 마스크");
        }
    }

    // 모바일 임포트: 1K 이하·밉맵·Android ASTC 6x6. 마스크는 선형(sRGB 끔)·Clamp라야 값이 그대로 읽힌다
    [Test]
    public void 잔디_텍스처는_모바일_설정이고_마스크는_선형이다()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}Grass_t1.mat");
        Assert.IsNotNull(material, "Grass_t1.mat 없음");
        foreach (string property in new[] { "_GoodTex", "_DryTex", "_WearMask" })
        {
            string path = AssetDatabase.GetAssetPath(material.GetTexture(property));
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.IsNotNull(importer, $"{property} 임포터");
            Assert.LessOrEqual(importer.maxTextureSize, 1024, $"{property} 최대 크기");
            Assert.IsTrue(importer.mipmapEnabled, $"{property} 밉맵");
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            Assert.IsTrue(android.overridden, $"{property} Android 덮어쓰기");
            Assert.AreEqual(TextureImporterFormat.ASTC_6x6, android.format, $"{property} Android 포맷");
            bool isMask = property == "_WearMask";
            Assert.AreEqual(!isMask, importer.sRGBTexture, $"{property} sRGB(알베도 켬, 마스크 끔)");
            Assert.AreEqual(isMask ? TextureWrapMode.Clamp : TextureWrapMode.Repeat, importer.wrapMode, $"{property} 반복");
        }
    }

    [Test]
    public void 잔디_머티리얼_4개가_같은_셰이더이고_인스턴싱을_끈다()
    {
        Shader first = null;
        foreach (StadiumSpec spec in Specs())
        {
            Material material = Load(spec).transform.Find("Grass").GetComponent<MeshRenderer>().sharedMaterial;
            if (first == null) { first = material.shader; }

            Assert.AreSame(first, material.shader, $"{spec.Id} 셰이더");
            Assert.AreEqual("PitchSimulator/GrassWear", material.shader.name, $"{spec.Id} 셰이더 이름");
            Assert.IsFalse(material.enableInstancing, $"{spec.Id} 인스턴싱");
        }
    }

    private static int CountPrefix(Transform root, string groupName, string prefix)
    {
        Transform group = root.Find(groupName);
        if (group == null) { return 0; }

        int count = 0;
        foreach (Transform child in group)
        {
            if (child.name.StartsWith(prefix)) { count++; }
        }
        return count;
    }
}
