#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class MeshSyncPro : EditorWindow
{
    [MenuItem("Tools/MeshSyncPro")]
    static void Open() => GetWindow<MeshSyncPro>("MeshSyncPro");
    GameObject avatar; Renderer[] rends;
    int bodyIdx = -1, clothIdx = -1;
    float thres = 0.005f, offset = 0.003f;
    List<int> indices = new();
    List<Vector3> worldPos = new();
    Vector2 scroll;

    void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;
    void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

    void OnGUI()
    {
        avatar = (GameObject)EditorGUILayout.ObjectField("アバター", avatar, typeof(GameObject), true);
        if (GUILayout.Button("読込") && avatar != null)
        {
            rends = avatar.GetComponentsInChildren<Renderer>(true)
                .Where(r => (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) ||
                            (r is MeshRenderer mr && mr.GetComponent<MeshFilter>()?.sharedMesh != null)).ToArray();
            bodyIdx = rends.Length > 0 ? 0 : -1;
            clothIdx = rends.Length > 1 ? 1 : -1;
            indices.Clear(); worldPos.Clear();
        }
        if (rends == null || rends.Length < 2) return;
        var names = rends.Select(r => r.name).ToArray();
        bodyIdx = EditorGUILayout.Popup("体", bodyIdx, names);
        clothIdx = EditorGUILayout.Popup("衣装", clothIdx, names);
        thres = EditorGUILayout.Slider("閾値", thres, 0.001f, 0.02f);
        offset = EditorGUILayout.Slider("オフセット", offset, 0.001f, 0.01f);

        GUI.enabled = bodyIdx != clothIdx && bodyIdx >= 0 && clothIdx >= 0;
        if (GUILayout.Button("検出")) Detect();
        GUI.enabled = true;

        if (indices.Count > 0)
        {
            EditorGUILayout.LabelField($"検出数: {indices.Count}");
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(80));
            foreach (var i in indices) EditorGUILayout.LabelField($"頂点{i}");
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("自動修正")) AutoFix();
        }
    }

    void Detect()
    {
        indices.Clear(); worldPos.Clear();
        var br = rends[bodyIdx]; var cr = rends[clothIdx];
        Mesh bm = new Mesh(), cm = new Mesh();
        try
        {
            if (br is SkinnedMeshRenderer bsmr) bsmr.BakeMesh(bm, true);
            else if (br is MeshRenderer bmr) bm = bmr.GetComponent<MeshFilter>().sharedMesh;
            if (cr is SkinnedMeshRenderer csmr) csmr.BakeMesh(cm, true);
            else if (cr is MeshRenderer cmr) cm = cmr.GetComponent<MeshFilter>().sharedMesh;
            var bvs = bm.vertices.Select(v => br.transform.TransformPoint(v)).ToArray();
            var bns = bm.normals.Select(n => br.transform.TransformDirection(n)).ToArray();
            var cvs = cm.vertices.Select(v => cr.transform.TransformPoint(v)).ToArray();
            for (int i = 0; i < bvs.Length; i++)
                for (int j = 0; j < cvs.Length; j++)
                    if ((bvs[i] - cvs[j]).magnitude < thres && Vector3.Dot(bns[i], (cvs[j] - bvs[i]).normalized) < -0.1f)
                    { indices.Add(i); worldPos.Add(bvs[i]); break; }
        }
        finally { DestroyImmediate(bm); DestroyImmediate(cm); }
        Repaint(); SceneView.RepaintAll();
    }

    void AutoFix()
    {
        var smr = rends[bodyIdx] as SkinnedMeshRenderer;
        if (smr == null) return;
        var mesh = Instantiate(smr.sharedMesh);
        Undo.RecordObject(mesh, "Penetration AutoFix"); // Undo対応[2][4]
        var verts = mesh.vertices;
        var norms = mesh.normals;
        foreach (var i in indices)
            verts[i] += norms[i].normalized * offset;
        mesh.vertices = verts;
        mesh.vertices = mesh.vertices; // Undo反映のため再代入[4]
        mesh.RecalculateBounds();
        smr.sharedMesh = null;
        smr.sharedMesh = mesh;
        indices.Clear(); worldPos.Clear();
        Repaint(); SceneView.RepaintAll();
    }

    void OnSceneGUI(SceneView s)
    {
        if (worldPos.Count == 0) return;
        Handles.color = Color.red;
        foreach (var p in worldPos)
            Handles.SphereHandleCap(0, p, Quaternion.identity, HandleUtility.GetHandleSize(p) * 0.03f, EventType.Repaint);
    }
}
#endif
