using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// 「レンダーキューマスター」 - VRChatアバターの透明表現を最適化するツール
public class RenderQueueMaster : EditorWindow
{
    class MaterialInfo
    {
        public Material material; // 対象マテリアル
        public int originalRenderQueue; // 分析時の初期値
        public int savedRenderQueue;    // ユーザー保存値
        public int detectedProblemType; // 0=問題なし, 1=水中, 2=透視, ...
    }
    GameObject targetAvatar, previewAvatarInstance;
    List<MaterialInfo> materialInfos = new();
    Dictionary<string, int> savedRenderQueueDict = new();
    Vector2 scrollPosition;
    int selectedTab;
    float previewRotationY;
    PreviewRenderUtility[] previewUtils = new PreviewRenderUtility[6];
    Material selectedMaterial;
    [MenuItem("Tools/レンダーキューマスター")]
    public static void OpenWindow() => GetWindow<RenderQueueMaster>("レンダーキューマスター");

    void OnEnable()
    {
        for (int i = 0; i < 6; i++)
        {
            previewUtils[i] = new PreviewRenderUtility();
            previewUtils[i].camera.transform.position = new Vector3(0, 1, -4);
        }
    }
    void OnDisable()
    {
        for (int i = 0; i < 6; i++) previewUtils[i]?.Cleanup();
        if (previewAvatarInstance) DestroyImmediate(previewAvatarInstance);
    }
    void OnGUI()
    {
        EditorGUILayout.LabelField("VRChatアバターのレンダーキュー最適化・復元・プレビュー", EditorStyles.boldLabel);
        targetAvatar = (GameObject)EditorGUILayout.ObjectField("対象アバター", targetAvatar, typeof(GameObject), true);
        selectedTab = GUILayout.Toolbar(selectedTab, new[] { "一覧", "自動修正", "6環境プレビュー", "保存/復元" });
        if (GUILayout.Button("アバターを分析")) AnalyzeMaterials();
        if (selectedTab == 0) // 一覧
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            foreach (var info in materialInfos)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(info.material.name, GUILayout.Width(120))) selectedMaterial = info.material;
                EditorGUILayout.LabelField($"現在:{info.material.renderQueue} 初期:{info.originalRenderQueue} 保存:{info.savedRenderQueue}", GUILayout.Width(180));
                if (GUILayout.Button("保存値復元", GUILayout.Width(70))) info.material.renderQueue = info.savedRenderQueue > 0 ? info.savedRenderQueue : info.originalRenderQueue;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
        if (selectedTab == 1) // 自動修正
        {
            EditorGUILayout.HelpBox("ボタン1つで推奨値に一括修正できます。", MessageType.Info);
            if (GUILayout.Button("全マテリアルを水中対策(2999)に")) FixAll(info => info.material.renderQueue = 2999);
            if (GUILayout.Button("全マテリアルをカットアウト(2450)に")) FixAll(info => info.material.renderQueue = 2450);
            if (GUILayout.Button("全マテリアルを透過標準(2460)に")) FixAll(info => info.material.renderQueue = 2460);
        }
        if (selectedTab == 2) DrawMultiEnvironmentPreview();
        if (selectedTab == 3) // 保存・復元
        {
            EditorGUILayout.HelpBox("現在値を保存、または保存値・初期値へ一括復元できます。", MessageType.Info);
            if (GUILayout.Button("現在値を保存")) { foreach (var info in materialInfos) savedRenderQueueDict[info.material.name] = info.material.renderQueue; SaveAll(); }
            if (GUILayout.Button("保存値で全復元")) { foreach (var info in materialInfos) if (savedRenderQueueDict.TryGetValue(info.material.name, out var v)) info.material.renderQueue = v; }
            if (GUILayout.Button("初期値で全復元")) { foreach (var info in materialInfos) info.material.renderQueue = info.originalRenderQueue; }
        }
    }
    void AnalyzeMaterials()
    {
        materialInfos.Clear();
        if (previewAvatarInstance) DestroyImmediate(previewAvatarInstance);
        if (!targetAvatar) return;
        previewAvatarInstance = Instantiate(targetAvatar); previewAvatarInstance.hideFlags = HideFlags.HideAndDontSave;
        foreach (Renderer renderer in targetAvatar.GetComponentsInChildren<Renderer>(true))
            foreach (var mat in renderer.sharedMaterials)
                if (mat && materialInfos.All(x => x.material != mat))
                    materialInfos.Add(new MaterialInfo
                    {
                        material = mat,
                        originalRenderQueue = mat.renderQueue,
                        savedRenderQueue = savedRenderQueueDict.TryGetValue(mat.name, out var v) ? v : 0
                    });
    }
    void FixAll(System.Action<MaterialInfo> act) { foreach (var info in materialInfos) act(info); }
    void DrawMultiEnvironmentPreview()
    {
        if (!previewAvatarInstance) { EditorGUILayout.HelpBox("アバターを分析してください", MessageType.Info); return; }
        string[] envNames = { "通常", "水中", "ガラス", "スカイ", "他アバ", "暗" };
        int[] envRQ = { 2000, 3000, 3000, 2500, 2460, 2460 };
        float h = 400, w = (position.width - 20) / 2, ph = h / 3;
        Rect t = GUILayoutUtility.GetRect(position.width - 20, h);
        for (int i = 0; i < 6; i++)
        {
            int x = i % 2, y = i / 2;
            Rect r = new Rect(t.x + x * w, t.y + y * ph, w, ph - 5);
            previewUtils[i].BeginPreview(r, GUIStyle.none);
            var inst = Instantiate(previewAvatarInstance); inst.hideFlags = HideFlags.HideAndDontSave; inst.transform.rotation = Quaternion.Euler(0, previewRotationY, 0);
            foreach (var renderer in inst.GetComponentsInChildren<Renderer>(true))
                foreach (var mat in renderer.sharedMaterials)
                    if (mat && selectedMaterial && mat.name == selectedMaterial.name) mat.renderQueue = envRQ[i];
            Bounds b = CalcBounds(inst); float d = Mathf.Max(b.size.x, b.size.y, b.size.z) * 1.5f;
            previewUtils[i].camera.transform.position = new Vector3(0, b.center.y, -d); previewUtils[i].camera.transform.LookAt(b.center);
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = new Mesh(); smr.BakeMesh(mesh);
                for (int si = 0; si < smr.sharedMaterials.Length; si++)
                    previewUtils[i].DrawMesh(mesh, smr.transform.localToWorldMatrix, smr.sharedMaterials[si], si);
            }
            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh)
                    for (int si = 0; si < mr.sharedMaterials.Length; si++)
                        previewUtils[i].DrawMesh(mf.sharedMesh, mr.transform.localToWorldMatrix, mr.sharedMaterials[si], si);
            }
            previewUtils[i].camera.clearFlags = CameraClearFlags.Skybox; previewUtils[i].Render();
            GUI.DrawTexture(r, previewUtils[i].EndPreview(), ScaleMode.ScaleToFit, false); DestroyImmediate(inst);
            EditorGUI.LabelField(new Rect(r.x, r.y + r.height - 18, r.width, 18), $"{envNames[i]}(RQ:{envRQ[i]})", EditorStyles.boldLabel);
        }
        previewRotationY = (previewRotationY + 0.5f) % 360; Repaint();
    }
    Bounds CalcBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = renderers[0].bounds; for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds); return b;
    }
    void SaveAll() => EditorPrefs.SetString("rqm_save", JsonUtility.ToJson(new SaveObj { data = savedRenderQueueDict.Select(x => new SaveItem { name = x.Key, rq = x.Value }).ToList() }));
    [System.Serializable] class SaveItem { public string name; public int rq; }
    [System.Serializable] class SaveObj { public List<SaveItem> data; }
}
