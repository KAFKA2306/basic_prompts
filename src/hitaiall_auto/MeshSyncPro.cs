#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// アプリ名を MeshSyncPro に変更
public class MeshSyncPro : EditorWindow
{
    [MenuItem("Tools/MeshSyncPro")] // メニューパスも変更
    static void Open() => GetWindow<MeshSyncPro>("MeshSyncPro"); // ウィンドウタイトルも変更

    // --- UI Elements ---
    GameObject avatar;
    SkinnedMeshRenderer bodyRenderer; // 体はSkinnedMeshRendererを想定
    Renderer clothRenderer; // 衣装はSkinnedMeshRenderer or MeshRenderer

    // --- Parameters (添付ファイル[1]の値を維持) ---
    float penetrationThreshold = 0.006f;
    float pushOutOffset = 0.02f;
    // 詳細設定へ移動するパラメータ
    int influenceRadiusSteps = 5;
    int smoothingIterations = 5;
    float smoothingFactor = 1.0f;

    // --- Automatic Iteration (添付ファイル[1]の値を維持) ---
    int autoFixTotalIterations = 5;
    bool isAutoIterating = false; // 自動反復処理中かどうかのフラグ

    // --- Protection (添付ファイル[1]の構成を維持) ---
    HumanBodyBones[] protectedBoneEnums = new HumanBodyBones[]
    {
        HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
        HumanBodyBones.Head
    };
    List<Transform> protectedBoneTransforms = new List<Transform>();
    HashSet<int> protectedVertices = new HashSet<int>();

    // --- Exclusion Zones (添付ファイル[1]の構成を維持し、初期サイズ変更) ---
    [System.Serializable]
    public class ExclusionZone
    {
        public string name = "修正対象外エリア"; // 用語変更
        public Vector3 center = Vector3.zero;
        public Vector3 size = Vector3.one * 0.4f; // 初期サイズを0.4mに変更
        public bool isActive = true;
    }
    List<ExclusionZone> exclusionZones = new List<ExclusionZone>();

    // --- Internal Data (添付ファイル[1]の構成を維持) ---
    Renderer[] availableRenderers;
    string[] availableRendererNames;
    int selectedBodyRendererIndex = -1;
    int selectedClothRendererIndex = -1;

    List<int> detectedPenetrationIndices = new List<int>();
    List<Vector3> detectedWorldPositions = new List<Vector3>();
    HashSet<int> excludedIndices = new HashSet<int>(); // 修正から除外する頂点インデックス

    Vector2 scrollPosition;
    bool showProtectedBonesFold = true; // 保護ボーンは最初から表示
    bool showDetectionInScene = true;
    bool showExclusionZonesFold = true; // 修正対象外エリアは最初から表示
    bool showAdvancedSettings = false; // 詳細設定は最初は非表示
    Animator animator;

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        if (avatar != null) LoadAvatarData();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    void OnGUI()
    {
        // タイトルと操作ガイド
        EditorGUILayout.LabelField("MeshSyncPro", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "ようこそ MeshSyncPro へ！ ✨\n" +
            "1. 「アバターオブジェクト」にキャラクターをドラッグ＆ドロップ！\n" +
            "2. 「本体メッシュ」と「衣装メッシュ」をプルダウンから選んでね。\n" +
            "3. 必要なら「修正対象外エリア」を作って、肌を見せたい部分などを設定！\n" +
            "4. 準備ができたら「貫通チェック」ボタンを押してみよう！\n" +
            "5. 赤い点が見つかったら「自動修正」ボタンでキレイにできるよ！\n\n" +
            "もっと細かく調整したい？ そんな時は「詳細設定」を開いてみてね！😉",
            MessageType.Info);
        GUILayout.Space(10);

        // --- アバターとメッシュ選択 ---
        EditorGUI.BeginChangeCheck();
        avatar = (GameObject)EditorGUILayout.ObjectField(new GUIContent("アバターオブジェクト", "修正したいキャラクターのルートオブジェクトを入れてね。"), avatar, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck() || (avatar != null && availableRenderers == null))
        {
            LoadAvatarData(); // アバターが変更されたらデータを再読み込み
        }

        if (avatar == null)
        {
            EditorGUILayout.HelpBox("キャラクター（アバターオブジェクト）を上の欄にセットしてね！", MessageType.Warning);
            return;
        }
        if (availableRenderers == null || availableRenderers.Length == 0)
        {
            EditorGUILayout.HelpBox("キャラクターにメッシュが見つからないみたい…？\nアバターオブジェクトが正しいか確認してみてね。", MessageType.Error);
            return;
        }

        selectedBodyRendererIndex = EditorGUILayout.Popup(new GUIContent("本体メッシュ", "キャラクターの「体」部分のメッシュを選んでね。"), selectedBodyRendererIndex, availableRendererNames);
        selectedClothRendererIndex = EditorGUILayout.Popup(new GUIContent("衣装メッシュ", "貫通を直したい「服」や「アクセサリー」のメッシュを選んでね。"), selectedClothRendererIndex, availableRendererNames);
        UpdateSelectedRenderers(); // 選択されたレンダラーを更新

        if (bodyRenderer == null) { EditorGUILayout.HelpBox("「本体メッシュ」を選んでね！", MessageType.Error); return; }
        if (clothRenderer == null) { EditorGUILayout.HelpBox("「衣装メッシュ」を選んでね！", MessageType.Error); return; }
        if (bodyRenderer == clothRenderer) { EditorGUILayout.HelpBox("「本体」と「衣装」には、違うメッシュを選んでね！", MessageType.Error); return; }
        GUILayout.Space(10);

        // --- 基本設定 ---
        EditorGUILayout.LabelField("基本設定", EditorStyles.boldLabel);
        penetrationThreshold = EditorGUILayout.Slider(new GUIContent("貫通判定のしきい値", "体が衣装にどれくらい近づいたら「貫通！」って判定するかの敏感さだよ。小さいほど敏感になるよ。"), penetrationThreshold, 0.001f, 0.05f);
        // エラー修正: GUIContentW -> GUIContent
        pushOutOffset = EditorGUILayout.Slider(new GUIContent("押し出し距離（安全マージン）", "貫通を直す時、衣装からどれだけ体を押し出すかの距離だよ。少し余裕を持たせると再貫通しにくいよ。"), pushOutOffset, 0.001f, 0.05f);
        GUILayout.Space(10);

        // --- 修正対象外エリア ---
        showExclusionZonesFold = EditorGUILayout.Foldout(showExclusionZonesFold, new GUIContent("修正対象外エリア", "「ここは肌を見せたいから貫通しててもOK！」っていう場所を設定できるよ。"));
        if (showExclusionZonesFold)
        {
            EditorGUI.indentLevel++;
            for (int i = 0; i < exclusionZones.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                exclusionZones[i].name = EditorGUILayout.TextField(new GUIContent("エリア名", "分かりやすい名前を付けてね。"), exclusionZones[i].name);
                exclusionZones[i].isActive = EditorGUILayout.Toggle(new GUIContent("有効", "このエリアを判定に使うかどうか。"), exclusionZones[i].isActive, GUILayout.Width(40));
                EditorGUILayout.EndHorizontal();
                exclusionZones[i].center = EditorGUILayout.Vector3Field(new GUIContent("エリア中心座標", "エリアの中心位置だよ。Sceneビューでも動かせるよ！"), exclusionZones[i].center);
                exclusionZones[i].size = EditorGUILayout.Vector3Field(new GUIContent("エリアサイズ（m）", "エリアの幅、高さ、奥行だよ。単位はメートル。"), exclusionZones[i].size);
                if (GUILayout.Button("このエリアを削除", GUILayout.Width(120))) { exclusionZones.RemoveAt(i); break; }
                EditorGUILayout.Space();
            }
            if (GUILayout.Button("新しいエリアを追加")) exclusionZones.Add(new ExclusionZone());
            EditorGUI.indentLevel--;
        }
        GUILayout.Space(10);

        // --- 保護ボーン ---
        showProtectedBonesFold = EditorGUILayout.Foldout(showProtectedBonesFold, new GUIContent("保護ボーンリスト", "手や足みたいに、形を崩したくない大事な体の部分を貫通修正から守るよ。"));
        if (showProtectedBonesFold)
        {
            if (animator != null && animator.isHuman)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("以下のボーン周辺は貫通修正の影響を受けにくくなります：");
                foreach (var boneEnum in protectedBoneEnums) EditorGUILayout.LabelField("  - " + boneEnum.ToString());
                if (GUILayout.Button("保護する体の部分を再計算する")) { CacheProtectedBoneTransforms(); CacheProtectedVertices(); }
                EditorGUILayout.HelpBox($"現在、約 {protectedVertices.Count} 個の体の頂点が保護されています。", MessageType.None);
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.HelpBox("アバターに人型ボーン（Humanoid）が見つからないか、Animatorがありません。\nこの機能はHumanoidアバターで利用できます。", MessageType.Warning);
            }
        }
        GUILayout.Space(10);

        // --- 表示設定 ---
        showDetectionInScene = EditorGUILayout.Toggle(new GUIContent("シーンで貫通箇所を表示", "貫通チェックで見つかった場所をSceneビューに赤い点で表示するよ。"), showDetectionInScene);
        GUILayout.Space(10);

        // --- 詳細設定 (Foldout) ---
        showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "詳細設定（上級者向け）");
        if (showAdvancedSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("ここはちょっと難しい設定だよ！よく分からない時は触らなくても大丈夫！😉", MessageType.None);
            influenceRadiusSteps = EditorGUILayout.IntSlider(new GUIContent("修正範囲の広さ", "貫通を直す時、周りのメッシュをどれくらい滑らかに馴染ませるかの範囲だよ。"), influenceRadiusSteps, 0, 10);
            smoothingIterations = EditorGUILayout.IntSlider(new GUIContent("スムージング回数", "修正した場所を滑らかにする処理を何回繰り返すかだよ。多いほど滑らかになるけど、処理も重くなるよ。"), smoothingIterations, 0, 20);
            smoothingFactor = EditorGUILayout.Slider(new GUIContent("スムージング強さ", "修正した場所をどれくらい強く滑らかにするかだよ。大きいほど強く滑らかになるよ。"), smoothingFactor, 0.0f, 1.0f);

            if (detectedPenetrationIndices.Count > 0)
            {
                GUILayout.Space(5);
                EditorGUILayout.LabelField("検出された貫通点の個別修正ON/OFF", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("チェックを外した頂点は「自動修正」の対象外になるよ。", MessageType.None);
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(Mathf.Min(120, detectedPenetrationIndices.Count * EditorGUIUtility.singleLineHeight + 5)));
                for (int i = 0; i < detectedPenetrationIndices.Count; i++)
                {
                    int vertexIndex = detectedPenetrationIndices[i];
                    bool isSelectedToFix = !excludedIndices.Contains(vertexIndex);
                    bool newIsSelectedToFix = EditorGUILayout.ToggleLeft(new GUIContent($"頂点 {vertexIndex} を修正する", $"体のメッシュの頂点番号 {vertexIndex} を修正対象にするかどうか。"), isSelectedToFix);
                    if (newIsSelectedToFix && !isSelectedToFix) excludedIndices.Remove(vertexIndex);
                    else if (!newIsSelectedToFix && isSelectedToFix) excludedIndices.Add(vertexIndex);
                }
                EditorGUILayout.EndScrollView();
                if (GUILayout.Button(new GUIContent("全ての検出点を修正対象にする", "リストの全ての点のチェックをONにします。"), GUILayout.Width(200))) excludedIndices.Clear();
                if (GUILayout.Button(new GUIContent("全ての検出点を修正対象外にする", "リストの全ての点のチェックをOFFにします。"), GUILayout.Width(220))) excludedIndices.UnionWith(detectedPenetrationIndices);
            }
            EditorGUI.indentLevel--;
        }
        GUILayout.Space(15);

        // --- 実行ボタンセクション ---
        EditorGUILayout.LabelField("実行コマンド", EditorStyles.boldLabel);
        GUI.enabled = bodyRenderer != null && clothRenderer != null && bodyRenderer != clothRenderer;

        if (GUILayout.Button(new GUIContent("ステップ1：貫通チェック！", "衣装が体にめり込んでいないかチェックします。"), GUILayout.Height(35)))
        {
            DetectPenetrationsWithPhysics();
            excludedIndices.Clear();
        }

        GUI.enabled = bodyRenderer != null && clothRenderer != null && bodyRenderer != clothRenderer && detectedPenetrationIndices.Count > 0;
        if (GUILayout.Button(new GUIContent("ステップ2：選択した貫通を自動修正！", "チェックで見つかった貫通（詳細設定で修正対象にしたもの）を自動で直します。"), GUILayout.Height(35)))
        {
            AutoFixPenetrations();
        }
        GUI.enabled = bodyRenderer != null && clothRenderer != null && bodyRenderer != clothRenderer;
        GUILayout.Space(5);
        autoFixTotalIterations = EditorGUILayout.IntSlider(new GUIContent("自動反復回数", "「貫通チェック」と「自動修正」を連続で何回繰り返すか。"), autoFixTotalIterations, 1, 10);
        if (GUILayout.Button(new GUIContent($"連続自動修正 ({autoFixTotalIterations} 回実行)", $"「貫通チェック」と「全検出点の自動修正」を指定回数繰り返します。\n途中で止めたくなったらESCキーを押してね。"), GUILayout.Height(35)))
        {
            StartAutoFixIterations();
        }
        GUI.enabled = true;
    }

    void StartAutoFixIterations()
    {
        if (bodyRenderer == null || clothRenderer == null || bodyRenderer == clothRenderer)
        {
            EditorUtility.DisplayDialog("おっと！", "「本体メッシュ」と「衣装メッシュ」を正しく選んでから試してみてね！", "OK");
            return;
        }
        isAutoIterating = true;
        string progressTitle = "連続自動修正中...";
        try
        {
            for (int i = 0; i < autoFixTotalIterations; i++)
            {
                bool cancel = EditorUtility.DisplayCancelableProgressBar(
                    progressTitle,
                    $"処理中: {i + 1} / {autoFixTotalIterations} 回目 (ステップ1：貫通チェック中...)",
                    (float)(i + 0.1f) / autoFixTotalIterations);
                if (cancel) { Debug.Log("連続自動修正がキャンセルされました。"); break; }

                DetectPenetrationsWithPhysics();

                cancel = EditorUtility.DisplayCancelableProgressBar(
                    progressTitle,
                    $"処理中: {i + 1} / {autoFixTotalIterations} 回目 (ステップ2：自動修正の準備中... {detectedPenetrationIndices.Count}点検出)",
                    (float)(i + 0.5f) / autoFixTotalIterations);
                if (cancel) { Debug.Log("連続自動修正がキャンセルされました。"); break; }

                if (detectedPenetrationIndices.Count > 0)
                {
                    excludedIndices.Clear();
                    AutoFixPenetrations();
                }
                else
                {
                    Debug.LogWarning($"反復 {i + 1}回目: 貫通が見つからなかったため、修正をスキップしました。");
                    if (i > 0)
                    {
                        EditorUtility.DisplayDialog("やったね！", $"反復 {i + 1}回目で貫通が見つからなくなりました！\nたぶんキレイになったよ！✨", "OK");
                        break;
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            isAutoIterating = false;
        }
        if (!EditorUtility.DisplayCancelableProgressBar(progressTitle,"",1f))
            EditorUtility.DisplayDialog("完了！", $"{autoFixTotalIterations}回の連続自動修正が終わりました！\n仕上がりを確認してみてね！😊", "OK");
        Repaint();
    }

    void LoadAvatarData()
    {
        if (avatar == null) { availableRenderers = null; availableRendererNames = null; animator = null; selectedBodyRendererIndex = -1; selectedClothRendererIndex = -1; return; }
        animator = avatar.GetComponent<Animator>();

        availableRenderers = avatar.GetComponentsInChildren<Renderer>(true)
            .Where(r => (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) || (r is MeshRenderer mr && mr.GetComponent<MeshFilter>()?.sharedMesh != null))
            .ToArray();
        availableRendererNames = availableRenderers.Select(r => $"{r.name} ({r.GetType().Name})").ToArray();

        selectedBodyRendererIndex = -1;
        selectedClothRendererIndex = -1;

        if (availableRenderers.Length > 0) {
            selectedBodyRendererIndex = System.Array.FindIndex(availableRenderers, r =>
                r is SkinnedMeshRenderer && (r.name.ToLower().Contains("body") || r.name.ToLower().Contains("face") || r.name.ToLower().Contains("head")));
            if (selectedBodyRendererIndex == -1)
                selectedBodyRendererIndex = System.Array.FindIndex(availableRenderers, r => r is SkinnedMeshRenderer);
            if (selectedBodyRendererIndex == -1 && availableRenderers.Length > 0)
                 selectedBodyRendererIndex = 0;

            Renderer bodyCand = (selectedBodyRendererIndex != -1) ? availableRenderers[selectedBodyRendererIndex] : null;
            selectedClothRendererIndex = System.Array.FindIndex(availableRenderers, r =>
                r != bodyCand && (r.name.ToLower().Contains("cloth") || r.name.ToLower().Contains("dress") || r.name.ToLower().Contains("shirt") || r.name.ToLower().Contains("outer")));
            if (selectedClothRendererIndex == -1)
                 selectedClothRendererIndex = System.Array.FindIndex(availableRenderers, r => r != bodyCand);

            if (selectedClothRendererIndex == -1 && availableRenderers.Length > 1 && selectedBodyRendererIndex == 0)
                selectedClothRendererIndex = 1;
            else if (selectedClothRendererIndex == -1 && availableRenderers.Length > 0 && selectedBodyRendererIndex != 0)
                 selectedClothRendererIndex = 0;
        }

        UpdateSelectedRenderers();
        detectedPenetrationIndices.Clear(); detectedWorldPositions.Clear(); excludedIndices.Clear();
        if (animator != null && animator.isHuman) { CacheProtectedBoneTransforms(); CacheProtectedVertices(); }
        else { protectedBoneTransforms.Clear(); protectedVertices.Clear(); }
        Repaint();
    }

    void UpdateSelectedRenderers()
    {
        if (availableRenderers == null) return;
        bodyRenderer = (selectedBodyRendererIndex >= 0 && selectedBodyRendererIndex < availableRenderers.Length) ? availableRenderers[selectedBodyRendererIndex] as SkinnedMeshRenderer : null;
        clothRenderer = (selectedClothRendererIndex >= 0 && selectedClothRendererIndex < availableRenderers.Length) ? availableRenderers[selectedClothRendererIndex] : null;
    }

    void CacheProtectedBoneTransforms()
    {
        protectedBoneTransforms.Clear();
        if (animator == null || !animator.isHuman) return;
        foreach (var boneEnum in protectedBoneEnums)
        {
            Transform boneTransform = animator.GetBoneTransform(boneEnum);
            if (boneTransform != null) protectedBoneTransforms.Add(boneTransform);
        }
    }

    void CacheProtectedVertices()
    {
        protectedVertices.Clear();
        if (bodyRenderer == null || bodyRenderer.sharedMesh == null || animator == null || !animator.isHuman || protectedBoneTransforms.Count == 0) return;
        Mesh mesh = bodyRenderer.sharedMesh; BoneWeight[] boneWeights = mesh.boneWeights; Transform[] bones = bodyRenderer.bones;
        if (bones == null || bones.Length == 0) { return; }
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            if (IsBoneWeightProtected(boneWeights[i], bones, protectedBoneTransforms)) protectedVertices.Add(i);
        }
    }

    bool IsBoneWeightProtected(BoneWeight bw, Transform[] meshBones, List<Transform> currentProtectedBoneTransforms)
    {
        var influences = new List<(int index, float weight)>();
        if (bw.weight0 > 0) influences.Add((bw.boneIndex0, bw.weight0)); if (bw.weight1 > 0) influences.Add((bw.boneIndex1, bw.weight1));
        if (bw.weight2 > 0) influences.Add((bw.boneIndex2, bw.weight2)); if (bw.weight3 > 0) influences.Add((bw.boneIndex3, bw.weight3));
        if (influences.Count == 0) return false;
        influences = influences.OrderByDescending(item => item.weight).ToList();
        if (influences[0].index < 0 || influences[0].index >= meshBones.Length) return false;
        Transform dominantBone = meshBones[influences[0].index]; if (dominantBone == null) return false;
        foreach (Transform protectedBone in currentProtectedBoneTransforms)
        {
            if (protectedBone == null) continue;
            if (dominantBone == protectedBone || dominantBone.IsChildOf(protectedBone)) return true;
        }
        return false;
    }

    void DetectPenetrationsWithPhysics()
    {
        if (bodyRenderer == null || clothRenderer == null || bodyRenderer.sharedMesh == null) { return; }
        detectedPenetrationIndices.Clear(); detectedWorldPositions.Clear();
        if (protectedVertices.Count == 0 && animator != null && animator.isHuman) CacheProtectedVertices();

        Mesh bodyMeshBaked = new Mesh(); bodyRenderer.BakeMesh(bodyMeshBaked, true);
        Mesh clothMeshForCollider = new Mesh();
        Mesh clothMeshBakedForVertexCheck = new Mesh();

        bool clothIsSkinned = clothRenderer is SkinnedMeshRenderer;
        if (clothIsSkinned) {
            ((SkinnedMeshRenderer)clothRenderer).BakeMesh(clothMeshForCollider, true);
            ((SkinnedMeshRenderer)clothRenderer).BakeMesh(clothMeshBakedForVertexCheck, true);
        } else if (clothRenderer is MeshRenderer clothMr && clothMr.GetComponent<MeshFilter>()?.sharedMesh != null) {
            clothMeshForCollider = clothMr.GetComponent<MeshFilter>().sharedMesh;
            clothMeshBakedForVertexCheck = clothMr.GetComponent<MeshFilter>().sharedMesh;
        } else {
            Debug.LogError("[MeshSyncPro] 衣装メッシュの取得に失敗。"); DestroyImmediate(bodyMeshBaked); return;
        }

        bool physicsPenetrationDetected = false;
        MeshCollider bodyCol = null; MeshCollider clothCol = null;
        try {
            bodyCol = bodyRenderer.gameObject.GetComponent<MeshCollider>(); bool bodyColAdded = bodyCol == null;
            if (bodyColAdded) bodyCol = bodyRenderer.gameObject.AddComponent<MeshCollider>();
            bodyCol.sharedMesh = bodyMeshBaked; bodyCol.convex = true;

            clothCol = clothRenderer.gameObject.GetComponent<MeshCollider>(); bool clothColAdded = clothCol == null;
            if (clothColAdded) clothCol = clothRenderer.gameObject.AddComponent<MeshCollider>();
            clothCol.sharedMesh = clothMeshForCollider; clothCol.convex = true;

            physicsPenetrationDetected = Physics.ComputePenetration(
                bodyCol, bodyRenderer.transform.position, bodyRenderer.transform.rotation,
                clothCol, clothRenderer.transform.position, clothRenderer.transform.rotation,
                out Vector3 penetrationDirection, out float penetrationDistance);

            if (physicsPenetrationDetected) Debug.Log($"[MeshSyncPro] Physicsベース貫通検出: 方向 {penetrationDirection}, 距離 {penetrationDistance}");

            if (bodyColAdded) DestroyImmediate(bodyCol); else bodyCol.sharedMesh = null;
            if (clothColAdded) DestroyImmediate(clothCol); else clothCol.sharedMesh = null;
        } catch (System.Exception ex) {
            Debug.LogError($"[MeshSyncPro] Physics.ComputePenetration中にエラー: {ex.Message}");
            if (bodyCol != null && bodyCol.gameObject == bodyRenderer.gameObject && bodyRenderer.gameObject.GetComponent<MeshCollider>() == bodyCol) DestroyImmediate(bodyCol);
            if (clothCol != null && clothCol.gameObject == clothRenderer.gameObject && clothRenderer.gameObject.GetComponent<MeshCollider>() == clothCol) DestroyImmediate(clothCol);
        }

        Vector3[] bodyVertices_baked = bodyMeshBaked.vertices;
        Vector3[] clothVertices_baked_ws = clothMeshBakedForVertexCheck.vertices.Select(v => clothRenderer.transform.TransformPoint(v)).ToArray();
        Transform bodyTransform = bodyRenderer.transform;

        for (int i = 0; i < bodyVertices_baked.Length; i++) {
            if (protectedVertices.Contains(i)) continue;
            Vector3 bodyVertex_ws = bodyTransform.TransformPoint(bodyVertices_baked[i]);
            bool inExclusion = false; foreach(var zone in exclusionZones) { if (zone.isActive) { Bounds worldBounds = new Bounds(zone.center, zone.size); if (worldBounds.Contains(bodyVertex_ws)) { inExclusion = true; break; } } } if (inExclusion) continue;
            float minSqDistToCloth = float.MaxValue;
            foreach (Vector3 clothV_ws in clothVertices_baked_ws) minSqDistToCloth = Mathf.Min(minSqDistToCloth, (bodyVertex_ws - clothV_ws).sqrMagnitude);
            if (Mathf.Sqrt(minSqDistToCloth) < penetrationThreshold) {
                detectedPenetrationIndices.Add(i);
                detectedWorldPositions.Add(bodyVertex_ws);
            }
        }

        DestroyImmediate(bodyMeshBaked);
        if (clothIsSkinned) {
             DestroyImmediate(clothMeshForCollider);
             DestroyImmediate(clothMeshBakedForVertexCheck);
        }

        Repaint(); SceneView.RepaintAll();
        if (!isAutoIterating)
            EditorUtility.DisplayDialog("貫通チェック完了！", $"{detectedPenetrationIndices.Count} 個の貫通候補が見つかりました！\nSceneビューで赤い点を確認してね。", "OK");
    }

    void AutoFixPenetrations()
    {
        if (bodyRenderer == null || bodyRenderer.sharedMesh == null || detectedPenetrationIndices.Count == 0) { return; }
        Mesh originalBodyMesh = bodyRenderer.sharedMesh;
        Mesh newBodyMesh = Instantiate(originalBodyMesh);
        Undo.RecordObject(bodyRenderer, "MeshSyncPro 自動修正");

        Vector3[] vertices_local = newBodyMesh.vertices;
        Mesh bodyMeshBaked = new Mesh(); bodyRenderer.BakeMesh(bodyMeshBaked, true);
        Mesh clothMeshBaked = new Mesh();
        bool clothIsSkinned = clothRenderer is SkinnedMeshRenderer;
        if (clothIsSkinned) ((SkinnedMeshRenderer)clothRenderer).BakeMesh(clothMeshBaked, true);
        else if (clothRenderer is MeshRenderer mr && mr.GetComponent<MeshFilter>()?.sharedMesh != null) clothMeshBaked = mr.GetComponent<MeshFilter>().sharedMesh;
        else { Debug.LogError("[MeshSyncPro] 衣装メッシュの取得に失敗。"); DestroyImmediate(bodyMeshBaked); DestroyImmediate(newBodyMesh); return; }

        Vector3[] bodyVertices_baked_local = bodyMeshBaked.vertices;
        Transform bodyTransform = bodyRenderer.transform;
        Transform clothTransform = clothRenderer.transform;
        Vector3[] clothVertices_baked_ws = clothMeshBaked.vertices.Select(v => clothTransform.TransformPoint(v)).ToArray();
        int[] clothTriangles_baked = clothMeshBaked.triangles;
        Vector3[] clothNormals_baked_local = clothMeshBaked.normals;

        foreach (int indexInOriginalMesh in detectedPenetrationIndices) {
            if (protectedVertices.Contains(indexInOriginalMesh) || excludedIndices.Contains(indexInOriginalMesh)) continue;
            Vector3 bodyVertex_ws_current_pose = bodyTransform.TransformPoint(bodyVertices_baked_local[indexInOriginalMesh]);
            Vector3 closestPointOnClothSurface_ws; float signedDistanceToClothSurface;
            bool foundClosest = FindClosestPointOnMeshSurface(
                bodyVertex_ws_current_pose, clothVertices_baked_ws, clothTriangles_baked,
                clothNormals_baked_local, clothTransform,
                out closestPointOnClothSurface_ws, out signedDistanceToClothSurface);

            if (foundClosest && signedDistanceToClothSurface < -0.0001f) {
                float penetrationDepth = -signedDistanceToClothSurface;
                Vector3 pushDirection_ws = (bodyVertex_ws_current_pose - closestPointOnClothSurface_ws).normalized;
                if (pushDirection_ws == Vector3.zero)
                     pushDirection_ws = bodyTransform.TransformDirection(bodyMeshBaked.normals[indexInOriginalMesh]).normalized;
                float pushDistance = penetrationDepth + pushOutOffset;
                Vector3 displacement_ws = pushDirection_ws * pushDistance;
                Vector3 displacement_local = bodyTransform.InverseTransformVector(displacement_ws);
                vertices_local[indexInOriginalMesh] += displacement_local;
            }
        }

        if (smoothingIterations > 0 && smoothingFactor > 0f) {
            Dictionary<int, HashSet<int>> adjacencyMap = BuildAdjacencyMap(newBodyMesh);
            HashSet<int> verticesToSmooth = GetAffectedVertices(
                detectedPenetrationIndices.Where(i => !excludedIndices.Contains(i) && !protectedVertices.Contains(i)).ToList(),
                adjacencyMap, influenceRadiusSteps);
            for (int i = 0; i < smoothingIterations; i++) ApplyLaplacianSmoothingStep(vertices_local, adjacencyMap, verticesToSmooth, smoothingFactor);
        }

        newBodyMesh.vertices = vertices_local;
        newBodyMesh.RecalculateNormals();
        newBodyMesh.RecalculateBounds();
        bodyRenderer.sharedMesh = newBodyMesh;

        detectedPenetrationIndices.Clear();
        detectedWorldPositions.Clear();
        Repaint(); SceneView.RepaintAll();

        if (!isAutoIterating)
            EditorUtility.DisplayDialog("自動修正完了！", "貫通修正を試みました！\n仕上がりを確認してみてね！✨", "OK");

        DestroyImmediate(bodyMeshBaked);
        if (clothIsSkinned) DestroyImmediate(clothMeshBaked);
    }

    bool FindClosestPointOnMeshSurface(Vector3 point_ws, Vector3[] meshVertices_baked_ws, int[] meshTriangles, Vector3[] meshNormals_baked_local_for_sign, Transform meshTransform_for_sign, out Vector3 closestPointOnSurface_ws, out float signedDistance)
    {
        closestPointOnSurface_ws = Vector3.zero; signedDistance = float.MaxValue; bool found = false; float minSqrDistance = float.MaxValue; int bestTriIdx = -1;
        if (meshVertices_baked_ws == null || meshTriangles == null) return false;
        for (int i = 0; i < meshTriangles.Length; i += 3) {
            if (meshTriangles[i] >= meshVertices_baked_ws.Length || meshTriangles[i+1] >= meshVertices_baked_ws.Length || meshTriangles[i+2] >= meshVertices_baked_ws.Length) continue;
            Vector3 p0_ws = meshVertices_baked_ws[meshTriangles[i]]; Vector3 p1_ws = meshVertices_baked_ws[meshTriangles[i + 1]]; Vector3 p2_ws = meshVertices_baked_ws[meshTriangles[i + 2]];
            Vector3 currentClosestOnTri_ws = ClosestPointOnTriangle(point_ws, p0_ws, p1_ws, p2_ws); float sqrDist = (point_ws - currentClosestOnTri_ws).sqrMagnitude;
            if (sqrDist < minSqrDistance) { minSqrDistance = sqrDist; closestPointOnSurface_ws = currentClosestOnTri_ws; bestTriIdx = i; found = true; }
        }
        if (found) {
            Vector3 vecToPoint = point_ws - closestPointOnSurface_ws;
            if (vecToPoint.sqrMagnitude < 0.000001f) signedDistance = 0f;
            else { Vector3 triangleFaceNormal_ws = Vector3.zero;
                if (bestTriIdx != -1 && meshTriangles[bestTriIdx] < meshVertices_baked_ws.Length && meshTriangles[bestTriIdx+1] < meshVertices_baked_ws.Length && meshTriangles[bestTriIdx+2] < meshVertices_baked_ws.Length) {
                    Vector3 p0 = meshVertices_baked_ws[meshTriangles[bestTriIdx]]; Vector3 p1 = meshVertices_baked_ws[meshTriangles[bestTriIdx + 1]]; Vector3 p2 = meshVertices_baked_ws[meshTriangles[bestTriIdx + 2]];
                    triangleFaceNormal_ws = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                }
                else if (meshNormals_baked_local_for_sign != null && meshNormals_baked_local_for_sign.Length > 0 && meshTransform_for_sign != null) {
                    triangleFaceNormal_ws = meshTransform_for_sign.TransformDirection(meshNormals_baked_local_for_sign[0]).normalized;
                }
                else triangleFaceNormal_ws = (point_ws - closestPointOnSurface_ws).normalized;
                signedDistance = Vector3.Dot(vecToPoint, triangleFaceNormal_ws);
            }
        } return found;
    }
    Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c) {
        Vector3 ab = b-a; Vector3 ac = c-a; Vector3 ap = point-a; float d1=Vector3.Dot(ab,ap); float d2=Vector3.Dot(ac,ap); if(d1<=0f&&d2<=0f)return a;
        Vector3 bp = point-b; float d3=Vector3.Dot(ab,bp); float d4=Vector3.Dot(ac,bp); if(d3>=0f&&d4<=d3)return b;
        float vc=d1*d4-d3*d2; if(vc<=0f&&d1>=0f&&d3<=0f){float v=d1/(d1-d3); return a+v*ab;}
        Vector3 cp = point-c; float d5=Vector3.Dot(ab,cp); float d6=Vector3.Dot(ac,cp); if(d6>=0f&&d5<=d6)return c;
        float vb=d5*d2-d1*d6; if(vb<=0f&&d2>=0f&&d6<=0f){float w=d2/(d2-d6); return a+w*ac;}
        float va=d3*d6-d5*d4; if(va<=0f&&(d4-d3)>=0f&&(d5-d6)>=0f){float w_bc=(d4-d3)/((d4-d3)+(d5-d6)); return b+w_bc*(c-b);}
        float denom=1f/(va+vb+vc); if (Mathf.Approximately(denom, 0f)) return (a+b+c)/3f;
        float v_coord=vb*denom; float w_coord=vc*denom; return a+ab*v_coord+ac*w_coord;
    }
    Dictionary<int, HashSet<int>> BuildAdjacencyMap(Mesh mesh) {
        var map=new Dictionary<int,HashSet<int>>(); int[] triangles=mesh.triangles;
        for(int i=0;i<triangles.Length;i+=3){
            int v0=triangles[i];int v1=triangles[i+1];int v2=triangles[i+2];
            if(!map.ContainsKey(v0))map[v0]=new HashSet<int>();if(!map.ContainsKey(v1))map[v1]=new HashSet<int>();if(!map.ContainsKey(v2))map[v2]=new HashSet<int>();
            map[v0].Add(v1);map[v0].Add(v2);map[v1].Add(v0);map[v1].Add(v2);map[v2].Add(v0);map[v2].Add(v1);
        } return map;
    }
    HashSet<int> GetAffectedVertices(List<int> initialIndices, Dictionary<int, HashSet<int>> adjacencyMap, int steps) {
        HashSet<int> affected=new HashSet<int>(initialIndices);if(steps<=0)return affected;
        Queue<(int index,int dist)> queue=new Queue<(int,int)>();
        foreach(int idx in initialIndices)queue.Enqueue((idx,0));
        while(queue.Count>0){var current=queue.Dequeue();if(current.dist>=steps)continue;
            if(adjacencyMap.TryGetValue(current.index,out HashSet<int> neighbors)){
                foreach(int neighbor in neighbors)if(affected.Add(neighbor))queue.Enqueue((neighbor,current.dist+1));
            }
        } return affected;
    }
    void ApplyLaplacianSmoothingStep(Vector3[] vertices, Dictionary<int, HashSet<int>> adjacencyMap, HashSet<int> targetVertices, float factor) {
        Vector3[] smoothedDeltas=new Vector3[vertices.Length];
        foreach(int i in targetVertices){
            if(protectedVertices.Contains(i))continue;
            if(adjacencyMap.TryGetValue(i,out HashSet<int> neighbors)&&neighbors.Count>0){
                Vector3 centroid=Vector3.zero;int validNeighborCount=0;
                foreach(int neighborIdx in neighbors){centroid+=vertices[neighborIdx];validNeighborCount++;}
                if(validNeighborCount>0){centroid/=validNeighborCount;smoothedDeltas[i]=(centroid-vertices[i])*factor;}
            }
        }
        foreach(int i in targetVertices){
            if(protectedVertices.Contains(i))continue;
            vertices[i]+=smoothedDeltas[i];
        }
    }

    void OnSceneGUI(SceneView sceneView)
    {
        if (avatar == null) return;
        if (showExclusionZonesFold)
        {
            for (int i = 0; i < exclusionZones.Count; i++)
            {
                if (!exclusionZones[i].isActive) continue;
                var zone = exclusionZones[i];
                Handles.color = new Color(0.2f, 0.8f, 0.2f, 0.1f); // 半透明グリーン (塗りつぶし用)
                Handles.DrawSolidRectangleWithOutline( // 下面
                    new Vector3[] {
                        zone.center + new Vector3(-zone.size.x, -zone.size.y, -zone.size.z) * 0.5f,
                        zone.center + new Vector3( zone.size.x, -zone.size.y, -zone.size.z) * 0.5f,
                        zone.center + new Vector3( zone.size.x, -zone.size.y,  zone.size.z) * 0.5f,
                        zone.center + new Vector3(-zone.size.x, -zone.size.y,  zone.size.z) * 0.5f
                    }, Handles.color, Color.green * 0.8f);
                 Handles.DrawSolidRectangleWithOutline( // 上面
                    new Vector3[] {
                        zone.center + new Vector3(-zone.size.x, zone.size.y, -zone.size.z) * 0.5f,
                        zone.center + new Vector3( zone.size.x, zone.size.y, -zone.size.z) * 0.5f,
                        zone.center + new Vector3( zone.size.x, zone.size.y,  zone.size.z) * 0.5f,
                        zone.center + new Vector3(-zone.size.x, zone.size.y,  zone.size.z) * 0.5f
                    }, Handles.color, Color.green * 0.8f);
                Handles.color = Color.green;
                Handles.DrawWireCube(zone.center, zone.size);
                EditorGUI.BeginChangeCheck();
                Vector3 newPosition = Handles.PositionHandle(zone.center, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(this, "修正対象外エリアを移動");
                    exclusionZones[i].center = newPosition;
                }
            }
        }
        if (showDetectionInScene && detectedWorldPositions.Count > 0)
        {
            Handles.color = Color.red;
            foreach (var p_ws in detectedWorldPositions)
            {
                float size = HandleUtility.GetHandleSize(p_ws) * 0.03f;
                Handles.SphereHandleCap(0, p_ws, Quaternion.identity, size, EventType.Repaint);
            }
        }
    }
}
#endif
