#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class MeshSyncPro : EditorWindow
{
    [MenuItem("Tools/MeshSyncPro")]
    static void Open() => GetWindow<MeshSyncPro>("MeshSyncPro");

    // --- UI Elements ---
    GameObject avatar;
    SkinnedMeshRenderer bodyRenderer;
    Renderer clothRenderer;

    // --- Parameters ---
    float penetrationThreshold = 0.02f;
    float pushOutOffset = 0.02f;
    int influenceRadiusSteps = 5;
    int smoothingIterations = 1;
    float smoothingFactor = 0.5f;

    // --- Protection ---
    HumanBodyBones[] protectedBoneEnums = new HumanBodyBones[]
    {
        HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
        HumanBodyBones.Head
    };
    List<Transform> protectedBoneTransforms = new List<Transform>();
    HashSet<int> protectedVertices = new HashSet<int>();

    // --- Internal Data ---
    Renderer[] availableRenderers;
    string[] availableRendererNames;
    int selectedBodyRendererIndex = -1;
    int selectedClothRendererIndex = -1;
    List<int> detectedPenetrationIndices = new List<int>();
    List<Vector3> detectedWorldPositions = new List<Vector3>();
    HashSet<int> excludedIndices = new HashSet<int>();
    Vector2 scrollPosition;
    bool showProtectedBonesFold = true;
    bool showDetectionInScene = true;
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
        EditorGUILayout.LabelField("MeshSyncPro - 高品質貫通修正", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUI.BeginChangeCheck();
        avatar = (GameObject)EditorGUILayout.ObjectField("アバター", avatar, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck() || (avatar != null && availableRenderers == null))
            LoadAvatarData();

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
        showDetectionInScene = EditorGUILayout.Toggle("Sceneビューで検出点可視化", showDetectionInScene);

        GUI.enabled = bodyRenderer != null && clothRenderer != null && bodyRenderer != clothRenderer;
        if (GUILayout.Button("貫通検出", GUILayout.Height(30)))
        {
            DetectPenetrations();
            excludedIndices.Clear();
        }
        GUI.enabled = true;

        if (detectedPenetrationIndices.Count > 0)
        {
            EditorGUILayout.LabelField($"検出された貫通頂点数: {detectedPenetrationIndices.Count}");
            EditorGUILayout.HelpBox("リスト上でチェックを外すと、その頂点は修正から除外されます。", MessageType.Info);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(120));
            foreach (var idx in detectedPenetrationIndices)
            {
                bool excluded = excludedIndices.Contains(idx);
                bool newExcluded = !EditorGUILayout.ToggleLeft($"頂点 {idx}", !excluded);
                if (newExcluded != excluded)
                {
                    if (newExcluded) excludedIndices.Add(idx);
                    else excludedIndices.Remove(idx);
                }
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("全て除外/全て選択"))
            {
                if (excludedIndices.Count < detectedPenetrationIndices.Count)
                    excludedIndices = new HashSet<int>(detectedPenetrationIndices);
                else
                    excludedIndices.Clear();
            }
        }

        GUI.enabled = bodyRenderer != null && clothRenderer != null && bodyRenderer != clothRenderer && detectedPenetrationIndices.Count > 0;
        if (GUILayout.Button("自動修正実行", GUILayout.Height(30))) AutoFixPenetrations();
        GUI.enabled = true;
    }

    void LoadAvatarData()
    {
        if (avatar == null)
        {
            availableRenderers = null; availableRendererNames = null; animator = null;
            return;
        }
        animator = avatar.GetComponent<Animator>();
        availableRenderers = avatar.GetComponentsInChildren<Renderer>(true)
            .Where(r => (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) ||
                        (r is MeshRenderer mr && mr.GetComponent<MeshFilter>()?.sharedMesh != null))
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
        Mesh mesh = bodyRenderer.sharedMesh;
        BoneWeight[] boneWeights = mesh.boneWeights;
        Transform[] bones = bodyRenderer.bones;
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            if (IsBoneWeightProtected(boneWeights[i], bones)) protectedVertices.Add(i);
        }
    }

    bool IsBoneWeightProtected(BoneWeight bw, Transform[] meshBones)
    {
        var influences = new List<(int index, float weight)>();
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
        else clothMeshBaked = clothRenderer.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] bodyVertices_baked = bodyMeshBaked.vertices;
        Vector3[] bodyNormals_baked = bodyMeshBaked.normals;
        Vector3[] clothVertices_baked = clothMeshBaked.vertices;
        Transform bodyTransform = bodyRenderer.transform;
        Transform clothTransform = clothRenderer.transform;
        Vector3[] clothVertices_baked_ws = clothVertices_baked.Select(v => clothTransform.TransformPoint(v)).ToArray();
        for (int i = 0; i < bodyVertices_baked.Length; i++)
        {
            if (protectedVertices.Contains(i)) continue;
            Vector3 bodyVertex_ws = bodyTransform.TransformPoint(bodyVertices_baked[i]);
            Vector3 bodyNormal_ws = bodyTransform.TransformDirection(bodyNormals_baked[i]);
            float minSqDistToClothVertex = float.MaxValue;
            Vector3 closestClothVertex_ws = Vector3.zero;
            foreach (Vector3 cv_ws in clothVertices_baked_ws)
            {
                float sqDist = (bodyVertex_ws - cv_ws).sqrMagnitude;
                if (sqDist < minSqDistToClothVertex)
                {
                    minSqDistToClothVertex = sqDist;
                    closestClothVertex_ws = cv_ws;
                }
            }
            if (Mathf.Sqrt(minSqDistToClothVertex) < penetrationThreshold * 2.0f)
            {
                Vector3 directionToCloth = (closestClothVertex_ws - bodyVertex_ws).normalized;
                if (Vector3.Dot(bodyNormal_ws, directionToCloth) < -0.1f)
                {
                    detectedPenetrationIndices.Add(i);
                    detectedWorldPositions.Add(bodyVertex_ws);
                }
            }
        }
        DestroyImmediate(bodyMeshBaked);
        if (clothIsSkinned) DestroyImmediate(clothMeshBaked);
        Repaint(); SceneView.RepaintAll();
        EditorUtility.DisplayDialog("検出完了", $"{detectedPenetrationIndices.Count} 個の貫通候補頂点を検出しました。", "OK");
    }

    void AutoFixPenetrations()
    {
        if (bodyRenderer == null || bodyRenderer.sharedMesh == null || detectedPenetrationIndices.Count == 0) return;
        Mesh originalMesh = bodyRenderer.sharedMesh;
        Mesh newMesh = Instantiate(originalMesh);
        Undo.RecordObject(bodyRenderer, "Mesh Penetration AutoFix");
        Vector3[] vertices_local = newMesh.vertices;
        Mesh bodyMeshBaked = new Mesh(); bodyRenderer.BakeMesh(bodyMeshBaked, true);
        Mesh clothMeshBaked = new Mesh();
        bool clothIsSkinned = clothRenderer is SkinnedMeshRenderer;
        if (clothIsSkinned) ((SkinnedMeshRenderer)clothRenderer).BakeMesh(clothMeshBaked, true);
        else clothMeshBaked = clothRenderer.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] bodyVertices_baked_local = bodyMeshBaked.vertices;
        Vector3[] clothVertices_baked_local = clothMeshBaked.vertices;
        int[] clothTriangles_baked = clothMeshBaked.triangles;
        Vector3[] clothNormals_baked_local = clothMeshBaked.normals;
        Transform bodyTransform = bodyRenderer.transform;
        Transform clothTransform = clothRenderer.transform;
        Vector3[] clothVertices_baked_ws = clothVertices_baked_local.Select(v => clothTransform.TransformPoint(v)).ToArray();

        foreach (int index in detectedPenetrationIndices)
        {
            if (protectedVertices.Contains(index)) continue;
            if (excludedIndices.Contains(index)) continue;
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
            if (foundClosest && signedDistanceToClothSurface < -0.0001f)
            {
                float penetrationDepth = -signedDistanceToClothSurface;
                Vector3 pushDirection_ws = (bodyVertex_ws - closestPointOnClothSurface_ws).normalized;
                float pushDistance = penetrationDepth + pushOutOffset;
                Vector3 displacement_ws = pushDirection_ws * pushDistance;
                Vector3 displacement_local = bodyTransform.InverseTransformVector(displacement_ws);
                vertices_local[index] += displacement_local;
            }
        }
        DestroyImmediate(bodyMeshBaked);
        if (clothIsSkinned) DestroyImmediate(clothMeshBaked);

        // --- Smoothing ---
        if (smoothingIterations > 0 && smoothingFactor > 0f)
        {
            Dictionary<int, HashSet<int>> adjacencyMap = BuildAdjacencyMap(newMesh);
            HashSet<int> verticesToSmooth = GetAffectedVertices(
                detectedPenetrationIndices.Where(i => !excludedIndices.Contains(i)).ToList(),
                adjacencyMap,
                influenceRadiusSteps);
            verticesToSmooth.ExceptWith(protectedVertices);
            for (int i = 0; i < smoothingIterations; i++)
                ApplyLaplacianSmoothingStep(vertices_local, adjacencyMap, verticesToSmooth, smoothingFactor);
        }

        newMesh.vertices = vertices_local;
        newMesh.RecalculateNormals();
        newMesh.RecalculateBounds();
        bodyRenderer.sharedMesh = newMesh;
        detectedPenetrationIndices.Clear();
        detectedWorldPositions.Clear();
        excludedIndices.Clear();
        Repaint();
        SceneView.RepaintAll();
        EditorUtility.DisplayDialog("修正完了", "メッシュの自動修正を試みました。", "OK");
    }

    // --- Geometry Helper Methods ---
    bool FindClosestPointOnMeshSurface(Vector3 point_ws, Vector3[] meshVertices_baked_ws, int[] meshTriangles, Vector3[] meshNormals_baked_local_for_sign, Transform meshTransform_for_sign, out Vector3 closestPointOnSurface_ws, out float signedDistance)
    {
        closestPointOnSurface_ws = Vector3.zero;
        signedDistance = float.MaxValue;
        bool found = false;
        float minSqrDistance = float.MaxValue;
        int bestTriIdx = -1;
        if (meshVertices_baked_ws == null || meshTriangles == null) return false;
        for (int i = 0; i < meshTriangles.Length; i += 3)
        {
            Vector3 p0_ws = meshVertices_baked_ws[meshTriangles[i]];
            Vector3 p1_ws = meshVertices_baked_ws[meshTriangles[i+1]];
            Vector3 p2_ws = meshVertices_baked_ws[meshTriangles[i+2]];
            Vector3 currentClosestOnTri_ws = ClosestPointOnTriangle(point_ws, p0_ws, p1_ws, p2_ws);
            float sqrDist = (point_ws - currentClosestOnTri_ws).sqrMagnitude;
            if (sqrDist < minSqrDistance)
            {
                minSqrDistance = sqrDist;
                closestPointOnSurface_ws = currentClosestOnTri_ws;
                bestTriIdx = i;
                found = true;
            }
        }
        if (found)
        {
            Vector3 vecToPoint = point_ws - closestPointOnSurface_ws;
            if (vecToPoint.sqrMagnitude < 0.000001f)
                signedDistance = 0f;
            else
            {
                Vector3 triangleFaceNormal_ws = Vector3.zero;
                if (bestTriIdx >= 0)
                {
                    Vector3 p0 = meshVertices_baked_ws[meshTriangles[bestTriIdx]];
                    Vector3 p1 = meshVertices_baked_ws[meshTriangles[bestTriIdx+1]];
                    Vector3 p2 = meshVertices_baked_ws[meshTriangles[bestTriIdx+2]];
                    triangleFaceNormal_ws = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                }
                else if (meshNormals_baked_local_for_sign != null && meshNormals_baked_local_for_sign.Length > 0)
                {
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
        if (d1 <= 0.0f && d2 <= 0.0f) return a;
        Vector3 bp = point - b; float d3 = Vector3.Dot(ab, bp); float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0.0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0.0f && d1 >= 0.0f && d3 <= 0.0f) { float v = d1 / (d1 - d3); return a + v * ab; }
        Vector3 cp = point - c; float d5 = Vector3.Dot(ab, cp); float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0.0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0.0f && d2 >= 0.0f && d6 <= 0.0f) { float w = d2 / (d2 - d6); return a + w * ac; }
        float va = d3 * d6 - d5 * d4;
        if (va <= 0.0f && (d4 - d3) >= 0.0f && (d5 - d6) >= 0.0f) { float w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return b + w * (c - b); }
        float denom = 1.0f / (va + vb + vc); float v_coord = vb * denom; float w_coord = vc * denom;
        return a + ab * v_coord + ac * w_coord;
    }

    Dictionary<int, HashSet<int>> BuildAdjacencyMap(Mesh mesh)
    {
        var map = new Dictionary<int, HashSet<int>>(); int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int v0 = triangles[i]; int v1 = triangles[i + 1]; int v2 = triangles[i + 2];
            if (!map.ContainsKey(v0)) map[v0] = new HashSet<int>();
            if (!map.ContainsKey(v1)) map[v1] = new HashSet<int>();
            if (!map.ContainsKey(v2)) map[v2] = new HashSet<int>();
            map[v0].Add(v1); map[v0].Add(v2); map[v1].Add(v0); map[v1].Add(v2); map[v2].Add(v0); map[v2].Add(v1);
        }
        return map;
    }

    HashSet<int> GetAffectedVertices(List<int> initialIndices, Dictionary<int, HashSet<int>> adjacencyMap, int steps)
    {
        HashSet<int> affected = new HashSet<int>(initialIndices); if (steps <= 0) return affected;
        Queue<int> queue = new Queue<int>(initialIndices); Dictionary<int, int> distance = new Dictionary<int, int>();
        foreach (int idx in initialIndices) distance[idx] = 0;
        while (queue.Count > 0)
        {
            int current = queue.Dequeue(); int currentDist = distance[current];
            if (currentDist >= steps) continue;
            if (adjacencyMap.TryGetValue(current, out HashSet<int> neighbors))
            {
                foreach (int neighbor in neighbors)
                {
                    if (!affected.Contains(neighbor))
                    {
                        affected.Add(neighbor); distance[neighbor] = currentDist + 1; queue.Enqueue(neighbor);
                    }
                }
            }
        }
        return affected;
    }

    void ApplyLaplacianSmoothingStep(Vector3[] vertices, Dictionary<int, HashSet<int>> adjacencyMap, HashSet<int> targetVertices, float factor)
    {
        Vector3[] smoothed_deltas = new Vector3[vertices.Length];
        foreach (int i in targetVertices)
        {
            if (adjacencyMap.TryGetValue(i, out HashSet<int> neighbors) && neighbors.Count > 0)
            {
                Vector3 centroid = Vector3.zero;
                foreach (int neighborIdx in neighbors) centroid += vertices[neighborIdx];
                centroid /= neighbors.Count;
                smoothed_deltas[i] = Vector3.Lerp(vertices[i], centroid, factor) - vertices[i];
            }
            else smoothed_deltas[i] = Vector3.zero;
        }
        foreach (int i in targetVertices) vertices[i] += smoothed_deltas[i];
    }

    void OnSceneGUI(SceneView sceneView)
    {
        if (!showDetectionInScene) return;
        if (detectedWorldPositions.Count == 0) return;
        Handles.color = Color.red;
        for (int i = 0; i < detectedWorldPositions.Count; i++)
        {
            if (excludedIndices.Contains(detectedPenetrationIndices[i])) continue;
            float size = HandleUtility.GetHandleSize(detectedWorldPositions[i]) * 0.03f;
            Handles.SphereHandleCap(0, detectedWorldPositions[i], Quaternion.identity, size, EventType.Repaint);
        }
        // クリックで除外/選択切り替え
        Event e = Event.current;
        if (e != null && e.type == EventType.MouseDown && e.button == 0 && e.control)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            float minDist = float.MaxValue;
            int nearestIdx = -1;
            for (int i = 0; i < detectedWorldPositions.Count; i++)
            {
                float dist = HandleUtility.DistanceToCircle(detectedWorldPositions[i], 0.03f);
                if (dist < minDist && dist < 15f)
                {
                    minDist = dist; nearestIdx = i;
                }
            }
            if (nearestIdx >= 0)
            {
                int idx = detectedPenetrationIndices[nearestIdx];
                if (excludedIndices.Contains(idx)) excludedIndices.Remove(idx);
                else excludedIndices.Add(idx);
                e.Use();
                Repaint();
            }
        }
    }
}
#endif
