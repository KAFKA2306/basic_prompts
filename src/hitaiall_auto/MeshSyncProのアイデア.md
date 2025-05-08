## MeshSyncPro
名称は、MeshSyncProで統一してください。
### 1. 概要

**1.1. 目的**  
Unityエディタ上で3Dキャラクターの体メッシュと衣装メッシュ間の貫通を自動検出・高精度に修正し、自然な見た目を維持しつつ作業効率を大幅に改善するツール。

**1.2. 対象**  
ヒューマノイドリグを持つキャラクターを主対象とし、手足や顔などの重要部位は保護しつつ、広範囲の貫通問題に対応。

**1.3. 主な特徴**  
- 距離・法線ベクトルに加え、レイキャストを併用した面内包判定で微小貫通も検出。  
- Laplacianスムージングによる自然な頂点修正。  
- Undo/Redo対応で安全な編集。  
- ユーザーが調整可能なパラメータ群を用意。

---

### 2. システム構成

- **UI管理**: `EditorWindow`ベースで、アバター・メッシュ選択、パラメータ調整、検出・修正操作を提供。  
- **データ管理**: 選択アバターから`Animator`とメッシュレンダラーを取得し、保護頂点をキャッシュ。  
- **検出処理**: 体メッシュ頂点と衣装メッシュ頂点を比較し、距離・法線・レイキャスト判定で貫通頂点を抽出。  
- **修正処理**: 貫通頂点を衣装表面の最近接点方向に押し出し、スムージングで周辺を整形。  
- **表示処理**: Sceneビューに貫通頂点を可視化。  

---

### 3. UI設計

- **アバター選択**: GameObjectフィールド  
- **体メッシュ選択**: SkinnedMeshRendererリストから選択  
- **衣装メッシュ選択**: Rendererリストから選択  
- **パラメータ**:  
  - 貫通検出閾値（0.001～0.1、初期0.02）  
  - 基本オフセット（0.01～1、初期0.1）  
  - 影響範囲（隣接ステップ、0～10、初期5）  
  - スムージング反復回数（0～20、初期1）  
  - スムージング係数（0.0～1.0、初期0.5）  
- **保護ボーン管理**: 保護ボーンリスト表示と再計算ボタン  
- **操作ボタン**: 「貫通検出」「自動修正実行」  
- **検出結果表示**: 貫通頂点数とインデックスリスト  

---

### 4. コア機能詳細

#### 4.1. アバター・メッシュ読み込み

- アバターからAnimator取得  
- 子オブジェクトのSkinnedMeshRenderer・MeshRendererを列挙  
- ヒューマノイドリグ判定し、保護ボーンTransformをキャッシュ  
- 体メッシュ頂点の保護判定（BoneWeight参照）  

#### 4.2. 貫通検出

- 体・衣装メッシュをBakeMeshで現在形状取得（ワールド座標変換）  
- 保護頂点を除く体頂点ごとに衣装頂点との距離計算（総当たり）  
- 距離が閾値の倍程度以下で候補化  
- 法線と体→衣装頂点ベクトルの内積が負の閾値以下なら貫通と判定  
- レイキャストによる面内包判定を併用し微小貫通を検出  


#### 4.3. 貫通修正

- Undo登録を行いメッシュコピーを作成  
- 各貫通頂点について衣装メッシュ表面の最近接点と符号付き距離を計算  
- 貫通深度＋基本オフセット分だけ押し出し方向に頂点を移動（ローカル座標系で）  
- 押し出し後、隣接頂点も含めLaplacianスムージングを指定回数実施  
- 法線・バウンディングボックス再計算後、メッシュ差し替え  

#### 4.4. スムージング

- 隣接マップを三角形情報から構築  
- 貫通頂点を起点に隣接ステップ分の頂点を対象に設定（保護頂点除外）  
- 反復回数分、対象頂点を隣接頂点の平均位置にスムージング係数で補間  

---

### 5. データ構造

| 変数名                   | 型                        | 内容説明                                 |
|--------------------------|---------------------------|------------------------------------------|
| avatar                   | GameObject                | 選択されたアバターのルート               |
| bodyRenderer             | SkinnedMeshRenderer       | 体メッシュ                              |
| clothRenderer            | Renderer                  | 衣装メッシュ                            |
| protectedVertices        | HashSet              | 保護対象頂点インデックス                 |
| detectedPenetrationIndices | List                | 貫通検出頂点インデックス                 |
| detectedWorldPositions   | List             | 貫通頂点のワールド座標                   |
| vertices_local           | Vector3[]                 | 体メッシュのローカル頂点座標配列         |
| adjacencyMap             | Dictionary> | 頂点隣接マップ                          |

---

### 6. 今後の拡張案

- BVH/Octreeによる空間分割実装で大規模メッシュ対応  
- レイキャスト判定のさらなる精度向上  
- ユーザーによる保護ボーンカスタマイズ機能  
- パラメータプリセットの保存・読み込み


---


「どの部分を“本当に”修正すべきか」は衣装ごと、シーンごとに大きく異なります。たとえば画像[3]では、スカートから素体の脚が貫通している部分は明らかに異常ですが、スカートと靴下の間に素肌が見えている部分は正常なデザインです。この違いを自動判定だけで完全に区別するのは難しいため、「ユーザーが直感的に“修正対象”を選択できる仕組み」が重要です。

## ユーザーが簡単に選択できる仕組み案

- **検出結果の可視化**  
  貫通候補頂点をSceneビュー上に赤いマーカーなどで表示し、どこが検出されたか一目で分かるようにする[5]。

- **リスト＆手動選択UI**  
  検出された頂点インデックスやワールド座標をリスト化し、チェックボックスやリスト上で「修正対象から除外」「修正対象に追加」などをワンクリックで切り替え可能にする。

- **ペイント/ブラシによる選択**  
  MeshDeleter with textureのように、テクスチャ上やSceneビューでペイントして「ここは修正」「ここは除外」と直感的に指定できる機能も有効です[4]。

- **領域指定・グループ選択**  
  スカート全体、靴下より上だけ、など、部位ごとに範囲選択できると効率的です。  
  例：ボーンやマテリアル、UV領域ごとに一括選択・除外。

- **プレビューとUNDO**  
  修正前後を即座にプレビューでき、気に入らなければUNDOで戻せることも必須です。

## 実装イメージ
1. 「貫通検出」ボタンで候補を自動抽出。
2. Sceneビューで赤マーカー表示。
3. リストやペイントで「ここは修正しない」「ここは修正する」をユーザーがポチポチ指定。
4. 「自動修正」実行で、選択された部分だけを修正。

このような「自動＋手動補正」のハイブリッド設計が、衣装ごとの細かいニュアンスに柔軟に対応できる最適解です。  

---

`MeshSyncPro.cs` の全文コードを以下に示します。これはUnityエディタ拡張として機能し、アバターの体メッシュと衣装メッシュ間の貫通を検出し、修正するツールです。

```csharp
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class MeshSyncPro : EditorWindow
{
    [MenuItem("Tools/MeshSyncPro")]
    static void Open() => GetWindow("MeshSyncPro");

    // --- UI Elements ---
    GameObject avatar;
    SkinnedMeshRenderer bodyRenderer;
    Renderer clothRenderer; // SkinnedMeshRenderer or MeshRenderer

    // --- Parameters ---
    float penetrationThreshold = 0.02f;
    float pushOutOffset = 0.05f;
    int smoothingIterations = 1;
    float smoothingFactor = 0.5f;
    int influenceRadiusSteps = 5;

    // --- Humanoid Protection ---
    HumanBodyBones[] protectedBoneEnums = new HumanBodyBones[] {
        HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
        HumanBodyBones.Head
    };
    List protectedBoneTransforms = new List();
    HashSet protectedVertices = new HashSet();

    // --- Internal Data ---
    Renderer[] availableRenderers;
    string[] availableRendererNames;
    int selectedBodyRendererIndex = -1;
    int selectedClothRendererIndex = -1;

    List detectedPenetrationIndices = new List();
    List detectedWorldPositions = new List();

    Vector2 scrollPosition;
    bool showProtectedBonesFold = true;
    Animator animator;

    // --- Life Cycle Methods ---
    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        if (avatar != null) LoadAvatarData();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    // --- GUI ---
    void OnGUI()
    {
        EditorGUILayout.LabelField("MeshSyncPro", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUI.BeginChangeCheck();
        avatar = (GameObject)EditorGUILayout.ObjectField("アバター", avatar, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck() || (avatar != null && availableRenderers == null))
        {
            LoadAvatarData();
        }

        if (avatar == null)
        {
            EditorGUILayout.HelpBox("アバターをセットしてください。", MessageType.Info);
            return;
        }

        if (availableRenderers == null || availableRenderers.Length == 0)
        {
            EditorGUILayout.HelpBox("アバターに有効なレンダラーが見つかりません。", MessageType.Warning);
            return;
        }

        selectedBodyRendererIndex = EditorGUILayout.Popup("体メッシュ", selectedBodyRendererIndex, availableRendererNames);
        selectedClothRendererIndex = EditorGUILayout.Popup("衣装メッシュ", selectedClothRendererIndex, availableRendererNames);

        UpdateSelectedRenderers();

        if (bodyRenderer == null)
        {
            EditorGUILayout.HelpBox("体のSkinnedMeshRendererを選択してください。", MessageType.Warning);
            return;
        }
        if (clothRenderer == null)
        {
            EditorGUILayout.HelpBox("衣装のRendererを選択してください。", MessageType.Warning);
            return;
        }
        if (bodyRenderer == clothRenderer)
        {
            EditorGUILayout.HelpBox("体と衣装に異なるメッシュを選択してください。", MessageType.Error);
            return;
        }

        GUILayout.Space(10);
        EditorGUILayout.LabelField("調整パラメータ", EditorStyles.boldLabel);
        penetrationThreshold = EditorGUILayout.Slider("貫通検出閾値", penetrationThreshold, 0.001f, 0.1f);
        pushOutOffset = EditorGUILayout.Slider("基本オフセット", pushOutOffset, 0.001f, 0.1f);
        influenceRadiusSteps = EditorGUILayout.IntSlider("影響範囲(隣接ステップ)", influenceRadiusSteps, 0, 10);
        smoothingIterations = EditorGUILayout.IntSlider("スムージング反復回数", smoothingIterations, 0, 20);
        smoothingFactor = EditorGUILayout.Slider("スムージング係数", smoothingFactor, 0.0f, 1.0f);

        GUILayout.Space(10);
        EditorGUILayout.LabelField("保護設定", EditorStyles.boldLabel);
        showProtectedBonesFold = EditorGUILayout.Foldout(showProtectedBonesFold, "保護ボーンリスト");
        if (showProtectedBonesFold && animator != null)
        {
            EditorGUI.indentLevel++;
            foreach (var boneEnum in protectedBoneEnums) EditorGUILayout.LabelField(boneEnum.ToString());
            if (GUILayout.Button("保護ボーン再計算"))
            {
                CacheProtectedBoneTransforms();
                CacheProtectedVertices();
            }
            EditorGUILayout.HelpBox($"現在 {protectedVertices.Count} 頂点が保護されています。", MessageType.Info);
            EditorGUI.indentLevel--;
        }
        else if (animator == null)
        {
            EditorGUILayout.HelpBox("アバターにAnimatorが見つからないため、ボーン保護は無効です。", MessageType.Warning);
        }

        GUILayout.Space(10);
        GUI.enabled = bodyRenderer != null && clothRenderer != null && bodyRenderer != clothRenderer;
        if (GUILayout.Button("貫通検出", GUILayout.Height(30))) DetectPenetrations();
        GUI.enabled = true;

        if (detectedPenetrationIndices.Count > 0)
        {
            EditorGUILayout.LabelField($"検出された貫通頂点数: {detectedPenetrationIndices.Count}");
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(100));
            foreach (var i in detectedPenetrationIndices) EditorGUILayout.LabelField($"頂点 {i}");
            EditorGUILayout.EndScrollView();

            GUI.enabled = bodyRenderer != null && clothRenderer != null && bodyRenderer != clothRenderer && detectedPenetrationIndices.Count > 0;
            if (GUILayout.Button("自動修正実行", GUILayout.Height(30))) AutoFixPenetrations();
            GUI.enabled = true;
        }
    }

    // --- Core Logic ---
    void LoadAvatarData()
    {
        if (avatar == null)
        {
            availableRenderers = null; availableRendererNames = null; animator = null;
            return;
        }
        animator = avatar.GetComponent();
        availableRenderers = avatar.GetComponentsInChildren(true)
            .Where(r => (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) ||
                        (r is MeshRenderer mr && mr.GetComponent()?.sharedMesh != null))
            .ToArray();
        availableRendererNames = availableRenderers.Select(r => r.name).ToArray();
        selectedBodyRendererIndex = -1; selectedClothRendererIndex = -1;
        bodyRenderer = null; clothRenderer = null;
        detectedPenetrationIndices.Clear(); detectedWorldPositions.Clear();
        if (animator != null && animator.isHuman)
        {
            CacheProtectedBoneTransforms();
            CacheProtectedVertices();
        }
        else
        {
            protectedBoneTransforms.Clear(); protectedVertices.Clear();
        }
        Repaint();
    }

    void UpdateSelectedRenderers()
    {
        if (availableRenderers == null) return;
        bodyRenderer = (selectedBodyRendererIndex >= 0 && selectedBodyRendererIndex = 0 && selectedClothRendererIndex ();
        if (bw.weight0 > 0) influences.Add((bw.boneIndex0, bw.weight0));
        if (bw.weight1 > 0) influences.Add((bw.boneIndex1, bw.weight1));
        if (bw.weight2 > 0) influences.Add((bw.boneIndex2, bw.weight2));
        if (bw.weight3 > 0) influences.Add((bw.boneIndex3, bw.weight3));
        influences = influences.OrderByDescending(item => item.weight).ToList();
        if (influences.Count > 0)
        {
            Transform dominantBone = meshBones[influences[0].index];
            foreach (Transform protectedBone in protectedBoneTransforms)
            {
                if (dominantBone == protectedBone || dominantBone.IsChildOf(protectedBone)) return true;
            }
        }
        return false;
    }

    void DetectPenetrations()
    {
        if (bodyRenderer == null || clothRenderer == null || bodyRenderer.sharedMesh == null) return;
        detectedPenetrationIndices.Clear(); detectedWorldPositions.Clear();
        if (protectedVertices.Count == 0 && animator != null && animator.isHuman) CacheProtectedVertices();

        Mesh bodyMeshBaked = new Mesh(); bodyRenderer.BakeMesh(bodyMeshBaked, true);
        Mesh clothMeshBaked = new Mesh();
        bool clothIsSkinned = clothRenderer is SkinnedMeshRenderer;
        if (clothIsSkinned) ((SkinnedMeshRenderer)clothRenderer).BakeMesh(clothMeshBaked, true);
        else clothMeshBaked = clothRenderer.GetComponent().sharedMesh;

        Vector3[] bodyVertices_baked = bodyMeshBaked.vertices;
        Vector3[] bodyNormals_baked = bodyMeshBaked.normals;
        Vector3[] clothVertices_baked = clothMeshBaked.vertices;
        Transform bodyTransform = bodyRenderer.transform;
        Transform clothTransform = clothRenderer.transform;
        Vector3[] clothVertices_baked_ws = clothVertices_baked.Select(v => clothTransform.TransformPoint(v)).ToArray();

        for (int i = 0; i ().sharedMesh;

        Vector3[] bodyVertices_baked_local = bodyMeshBaked.vertices;
        Vector3[] clothVertices_baked_local = clothMeshBaked.vertices;
        int[] clothTriangles_baked = clothMeshBaked.triangles;
        Vector3[] clothNormals_baked_local = clothMeshBaked.normals;

        Transform bodyTransform = bodyRenderer.transform;
        Transform clothTransform = clothRenderer.transform;

        Vector3[] clothVertices_baked_ws = clothVertices_baked_local.Select(v => clothTransform.TransformPoint(v)).ToArray();

        // --- 1. 貫通頂点の押し出し ---
        foreach (int index in detectedPenetrationIndices)
        {
            if (protectedVertices.Contains(index)) continue;

            Vector3 bodyVertex_ws = bodyTransform.TransformPoint(bodyVertices_baked_local[index]);

            Vector3 closestPointOnClothSurface_ws;
            float signedDistanceToClothSurface;

            bool foundClosest = FindClosestPointOnMeshSurface(
                bodyVertex_ws,
                clothVertices_baked_ws,
                clothTriangles_baked,
                clothNormals_baked_local,
                clothTransform,
                out closestPointOnClothSurface_ws,
                out signedDistanceToClothSurface);

            if (foundClosest && signedDistanceToClothSurface  0 && smoothingFactor > 0f)
        {
            Dictionary> adjacencyMap = BuildAdjacencyMap(newMesh);
            HashSet verticesToSmooth = GetAffectedVertices(detectedPenetrationIndices, adjacencyMap, influenceRadiusSteps);
            verticesToSmooth.ExceptWith(protectedVertices);

            for (int i = 0; i  0) {
                    triangleFaceNormal_ws = meshTransform_for_sign.TransformDirection(meshNormals_baked_local_for_sign[0]).normalized;
                }
                signedDistance = Vector3.Dot(vecToPoint.normalized, triangleFaceNormal_ws) * vecToPoint.magnitude;
            }
        }
        return found;
    }

    Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a; Vector3 ac = c - a; Vector3 ap = point - a;
        float d1 = Vector3.Dot(ab, ap); float d2 = Vector3.Dot(ac, ap);
        if (d1 = 0.0f && d4 = 0.0f && d3 = 0.0f && d5 = 0.0f && d6 = 0.0f && (d5 - d6) >= 0.0f) { float w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return b + w * (c - b); }
        float denom = 1.0f / (va + vb + vc); float v_coord = vb * denom; float w_coord = vc * denom;
        return a + ab * v_coord + ac * w_coord;
    }

    // --- Smoothing Helper Methods ---
    Dictionary> BuildAdjacencyMap(Mesh mesh)
    {
        var map = new Dictionary>(); int[] triangles = mesh.triangles;
        for (int i = 0; i (); if (!map.ContainsKey(v1)) map[v1] = new HashSet(); if (!map.ContainsKey(v2)) map[v2] = new HashSet();
            map[v0].Add(v1); map[v0].Add(v2); map[v1].Add(v0); map[v1].Add(v2); map[v2].Add(v0); map[v2].Add(v1);
        } return map;
    }

    HashSet GetAffectedVertices(List initialIndices, Dictionary> adjacencyMap, int steps)
    {
        HashSet affected = new HashSet(initialIndices); if (steps  queue = new Queue(initialIndices); Dictionary distance = new Dictionary();
        foreach (int idx in initialIndices) distance[idx] = 0;
        while (queue.Count > 0) {
            int current = queue.Dequeue(); int currentDist = distance[current];
            if (currentDist >= steps) continue;
            if (adjacencyMap.TryGetValue(current, out HashSet neighbors)) {
                foreach (int neighbor in neighbors) {
                    if (!affected.Contains(neighbor)) { affected.Add(neighbor); distance[neighbor] = currentDist + 1; queue.Enqueue(neighbor); }
                }
            }
        } return affected;
    }

    void ApplyLaplacianSmoothingStep(Vector3[] vertices, Dictionary> adjacencyMap, HashSet targetVertices, float factor)
    {
        Vector3[] smoothed_deltas = new Vector3[vertices.Length];

        foreach (int i in targetVertices)
        {
            if (adjacencyMap.TryGetValue(i, out HashSet neighbors) && neighbors.Count > 0)
            {
                Vector3 centroid = Vector3.zero;
                foreach (int neighborIdx in neighbors) centroid += vertices[neighborIdx];
                centroid /= neighbors.Count;
                smoothed_deltas[i] = Vector3.Lerp(vertices[i], centroid, factor) - vertices[i];
            }
            else {
                smoothed_deltas[i] = Vector3.zero;
            }
        }
        foreach(int i in targetVertices) {
            vertices[i] += smoothed_deltas[i];
        }
    }

    // --- Scene GUI ---
    void OnSceneGUI(SceneView sceneView)
    {
        if (detectedWorldPositions.Count == 0) return;
        Handles.color = Color.red;
        foreach (var p_ws in detectedWorldPositions) {
            float size = HandleUtility.GetHandleSize(p_ws) * 0.03f;
            Handles.SphereHandleCap(0, p_ws, Quaternion.identity, size, EventType.Repaint);
        }
    }
}
#endif
```
