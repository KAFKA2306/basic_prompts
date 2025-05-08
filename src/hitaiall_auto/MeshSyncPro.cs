```csharp
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using HhotateA.AvatarModifyTools.Core; // MeshCreater.cs必須

public class MeshSyncPro : EditorWindow
{
    [MenuItem("Tools/MeshSyncPro/貫通修正ツール")]
    static void Open() => GetWindow("MeshSyncPro - 貫通修正");

    GameObject avatar;
    Renderer[] rends;
    MeshCreater[] meshCreators;
    int bodyIndex = -1, clothingIndex = -1;
    float threshold = 0.005f;
    float offset = 0.003f;
    List results = new();
    Vector2 scroll;

    struct PenetrationInfo
    {
        public int index;
        public Vector3 worldPos;
        public Vector3 normal;
        public float depth;
        public PenetrationInfo(int i, Vector3 p, Vector3 n, float d) { index = i; worldPos = p; normal = n; depth = d; }
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("MeshSyncPro - 貫通修正", EditorStyles.boldLabel);
        avatar = (GameObject)EditorGUILayout.ObjectField("対象アバター", avatar, typeof(GameObject), true);
        if (GUILayout.Button("メッシュ読込") && avatar != null) LoadMeshes();

        if (rends == null || rends.Length  r != null ? r.name : "N/A").ToArray();
        bodyIndex = EditorGUILayout.Popup("体メッシュ", bodyIndex, names);
        clothingIndex = EditorGUILayout.Popup("衣装メッシュ", clothingIndex, names);

        threshold = EditorGUILayout.Slider("検出閾値(m)", threshold, 0.001f, 0.02f);
        offset = EditorGUILayout.Slider("修正オフセット(m)", offset, 0.001f, 0.01f);

        GUI.enabled = bodyIndex != clothingIndex && bodyIndex >= 0 && clothingIndex >= 0;
        if (GUILayout.Button("貫通検出")) Detect();
        GUI.enabled = true;

        if (results.Count > 0)
        {
            EditorGUILayout.LabelField($"検出数: {results.Count}");
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(120));
            foreach (var r in results)
                EditorGUILayout.LabelField($"頂点{r.index} 深度{r.depth:F4}m");
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("自動修正")) AutoFix();
        }
        else
        {
            EditorGUILayout.HelpBox("貫通が検出されていません。", MessageType.Info);
        }
    }

    void LoadMeshes()
    {
        rends = avatar.GetComponentsInChildren(true)
            .Where(r => (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) ||
                        (r is MeshRenderer mr && mr.GetComponent()?.sharedMesh != null))
            .ToArray();
        meshCreators = rends.Select(r => new MeshCreater(r, avatar.transform)).ToArray();
        bodyIndex = rends.Length > 0 ? 0 : -1;
        clothingIndex = rends.Length > 1 ? 1 : -1;
        results.Clear();
    }

    void Detect()
    {
        results.Clear();
        if (meshCreators == null || bodyIndex ()?.sharedMesh != null)
            {
                var src = bmr.GetComponent().sharedMesh;
                bodyMesh.vertices = src.vertices; bodyMesh.normals = src.normals; bodyMesh.triangles = src.triangles;
            }
            if (clothR is SkinnedMeshRenderer csmr) csmr.BakeMesh(clothMesh, true);
            else if (clothR is MeshRenderer cmr && cmr.GetComponent()?.sharedMesh != null)
            {
                var src = cmr.GetComponent().sharedMesh;
                clothMesh.vertices = src.vertices; clothMesh.normals = src.normals; clothMesh.triangles = src.triangles;
            }

            var bodyVerts = bodyMesh.vertices.Select(v => bodyR.transform.TransformPoint(v)).ToArray();
            var bodyNorms = bodyMesh.normals.Select(n => bodyR.transform.TransformDirection(n).normalized).ToArray();
            var clothVerts = clothMesh.vertices.Select(v => clothR.transform.TransformPoint(v)).ToArray();

            for (int i = 0; i () != null)
            mr.GetComponent().sharedMesh = updated;
        results.Clear();
        SceneView.RepaintAll();
        Repaint();
    }

    void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;
    void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

    void OnSceneGUI(SceneView sceneView)
    {
        if (results.Count == 0) return;
        Handles.color = Color.red;
        foreach (var r in results)
        {
            Handles.SphereHandleCap(0, r.worldPos, Quaternion.identity, HandleUtility.GetHandleSize(r.worldPos) * 0.02f, EventType.Repaint);
            Handles.DrawLine(r.worldPos, r.worldPos + r.normal * 0.01f);
        }
    }
}
#endif
```

---

**使い方：**

1. Unityメニュー「**Tools > MeshSyncPro > 貫通修正ツール**」を開く。
2. アバター（GameObject）をセットし「メッシュ読込」。
3. 「体メッシュ」「衣装メッシュ」を選択。
4. 「貫通検出」ボタンで貫通箇所を検出。
5. 必要に応じて「自動修正」ボタンで体メッシュの貫通頂点を自動で押し出し修正。

---
