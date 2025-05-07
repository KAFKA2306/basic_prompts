using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;

/// <summary>
/// レンダーキューマスター Pro - 抜本的な新構造・最高体験
/// 透明表現の問題を「可視化・自動化・教育・創造性」で解決する
/// ZWrite問題は自動修正の対象外。ユーザーが明示的にのみ操作可能。
/// </summary>
public class RenderQueueMasterPro : EditorWindow
{
    // --- データ定義 ---
    private static readonly Dictionary<int, string> RQ_LABELS = new() {
        {2000, "不透明"}, {2450, "カットアウト"}, {2460, "推奨透過"}, {2500, "スカイボックス境界"},
        {2999, "水中対策"}, {3000, "標準透過"}, {3900, "特殊エフェクト"}
    };
    private static readonly Dictionary<string, int[]> DANGER_RANGES = new() {
        {"透過", new[]{2399,2459}}, {"ファー", new[]{2001,2450}}, {"水中", new[]{3000,3500}}
    };

    // --- UI状態 ---
    private GameObject targetAvatar;
    private int activeTab = 0;
    private Vector2 scroll, previewScroll;
    private string filter = "";
    private string status = "";
    private Material previewMat;
    private float previewRot = 0f;
    private bool autoRotate = true;
    private string savePath = "";
    private bool showProblemsOnly = false;
    private bool showCompare = false;
    private PreviewRenderUtility[] previews = new PreviewRenderUtility[6];

    // --- データモデル ---
    private List<MatInfo> mats = new();
    private Dictionary<string, int> savedRQ = new();

    private class MatInfo
    {
        public Material Mat;
        public int OrigRQ, SavedRQ;
        public bool IsTransparent, HasZWriteOn;
        public int Problem; // 1:水中, 2:透視, 3:ZWrite, 4:スカイ, 5:ファー, 6:アクセ, 7:順序矛盾
        public string Path, Type;
        public List<GameObject> Objects = new();
        public bool Selected = true;
    }

    // --- メニュー ---
    [MenuItem("Tools/VRChat/レンダーキューマスター Pro")]
    public static void ShowWindow() => GetWindow<RenderQueueMasterPro>("レンダーキューマスター Pro");

    private void OnEnable()
    {
        for (int i = 0; i < 6; i++)
        {
            previews[i] ??= new PreviewRenderUtility();
            previews[i].camera.transform.position = new Vector3(0, 0, -5);
            previews[i].camera.transform.LookAt(Vector3.zero);
            previews[i].lights[0].intensity = 1.4f;
            previews[i].lights[0].transform.rotation = Quaternion.Euler(30, 30, 0);
        }
        var dir = Path.Combine(Application.dataPath, "RenderQueueMasterPro");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        savePath = Path.Combine(dir, "rq_save.json");
        LoadSaved();
    }
    private void OnDisable() { foreach (var pu in previews) pu?.Cleanup(); }

    // --- メインUI ---
    private void OnGUI()
    {
        EditorGUILayout.BeginVertical();
        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.HelpBox(status, MessageType.Info);
            if (GUILayout.Button("閉じる")) status = "";
            EditorGUILayout.Space();
        }
        EditorGUI.BeginChangeCheck();
        targetAvatar = (GameObject)EditorGUILayout.ObjectField("アバター", targetAvatar, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck() && targetAvatar) AnalyzeAll();
        string[] tabs = { "一覧", "修正", "プレビュー", "保存・復元", "チュートリアル" };
        activeTab = GUILayout.Toolbar(activeTab, tabs);
        EditorGUILayout.Space();
        switch (activeTab)
        {
            case 0: DrawList(); break;
            case 1: DrawFix(); break;
            case 2: DrawPreview(); break;
            case 3: DrawSave(); break;
            case 4: DrawTutorial(); break;
        }
        EditorGUILayout.EndVertical();
    }

    // --- マテリアル分析 ---
    private void AnalyzeAll()
    {
        mats.Clear();
        if (!targetAvatar) return;
        foreach (Renderer r in targetAvatar.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material m in r.sharedMaterials)
            {
                if (!m) continue;
                var mi = mats.FirstOrDefault(i => i.Mat == m);
                if (mi == null)
                {
                    mi = new MatInfo
                    {
                        Mat = m,
                        OrigRQ = m.renderQueue,
                        SavedRQ = GetSavedRQ(m),
                        IsTransparent = IsTransparent(m),
                        HasZWriteOn = IsZWriteOn(m),
                        Path = r.transform.GetPath(),
                        Type = GuessType(r.gameObject)
                    };
                    mi.Problem = DetectProblem(mi);
                    mats.Add(mi);
                }
                if (!mi.Objects.Contains(r.gameObject)) mi.Objects.Add(r.gameObject);
            }
        }
        status = $"分析完了: {mats.Count}個、問題: {mats.Count(m => m.Problem > 0)}個";
    }

    // --- 一覧タブ ---
    private void DrawList()
    {
        if (mats.Count == 0)
        {
            EditorGUILayout.HelpBox("アバターを選択して分析してください", MessageType.Info);
            if (GUILayout.Button("分析")) AnalyzeAll();
            return;
        }
        EditorGUILayout.BeginHorizontal();
        filter = EditorGUILayout.TextField("検索", filter);
        showProblemsOnly = EditorGUILayout.Toggle("問題のみ", showProblemsOnly);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox(string.Join(" / ", RQ_LABELS.Select(kv => $"{kv.Key}:{kv.Value}")), MessageType.Info);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("マテリアル", GUILayout.Width(140));
        EditorGUILayout.LabelField("RQ", GUILayout.Width(50));
        EditorGUILayout.LabelField("保存", GUILayout.Width(50));
        EditorGUILayout.LabelField("タイプ", GUILayout.Width(70));
        EditorGUILayout.LabelField("ZWrite", GUILayout.Width(50));
        EditorGUILayout.LabelField("問題", GUILayout.Width(120));
        EditorGUILayout.EndHorizontal();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var m in mats.Where(i =>
            (!showProblemsOnly || i.Problem > 0) &&
            (string.IsNullOrEmpty(filter) || i.Mat.name.ToLower().Contains(filter.ToLower()) || i.Path.ToLower().Contains(filter.ToLower()))
        ))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(m.Mat.name, EditorStyles.label, GUILayout.Width(140)))
            {
                Selection.activeObject = m.Mat;
                previewMat = m.Mat;
            }
            EditorGUILayout.LabelField(m.Mat.renderQueue.ToString(), GUILayout.Width(50));
            Color c = GUI.color;
            if (m.SavedRQ > 0 && m.Mat.renderQueue != m.SavedRQ) GUI.color = Color.cyan;
            EditorGUILayout.LabelField(m.SavedRQ > 0 ? m.SavedRQ.ToString() : "-", GUILayout.Width(50));
            GUI.color = c;
            EditorGUILayout.LabelField(m.Type, GUILayout.Width(70));
            EditorGUILayout.LabelField(m.HasZWriteOn ? "ON" : "OFF", GUILayout.Width(50));
            if (m.Problem > 0) GUI.color = Color.yellow;
            EditorGUILayout.LabelField(ProblemDesc(m.Problem), GUILayout.Width(120));
            GUI.color = c;
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    // --- 問題修正タブ ---
    private void DrawFix()
    {
        if (mats.Count == 0)
        {
            EditorGUILayout.HelpBox("アバターを選択して分析してください", MessageType.Info);
            return;
        }
        EditorGUILayout.LabelField("自動修正（ZWrite問題は除外）", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("全自動修正")) FixAll();
        if (GUILayout.Button("水中問題のみ")) FixType(1);
        if (GUILayout.Button("透視問題のみ")) FixType(2);
        if (GUILayout.Button("スカイボックスのみ")) FixType(4);
        if (GUILayout.Button("ファーのみ")) FixType(5);
        if (GUILayout.Button("アクセサリーのみ")) FixType(6);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("個別修正", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("全選択")) mats.ForEach(m => m.Selected = true);
        if (GUILayout.Button("全解除")) mats.ForEach(m => m.Selected = false);
        if (GUILayout.Button("問題のみ選択")) mats.ForEach(m => m.Selected = m.Problem > 0);
        EditorGUILayout.EndHorizontal();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var m in mats.Where(i => i.Problem > 0))
        {
            EditorGUILayout.BeginHorizontal();
            m.Selected = EditorGUILayout.Toggle(m.Selected, GUILayout.Width(20));
            if (GUILayout.Button(m.Mat.name, EditorStyles.label, GUILayout.Width(110))) Selection.activeObject = m.Mat;
            EditorGUILayout.LabelField($"RQ:{m.Mat.renderQueue}", GUILayout.Width(60));
            EditorGUILayout.LabelField(ProblemDesc(m.Problem), GUILayout.Width(120));
            if (m.Problem == 3)
            {
                EditorGUILayout.LabelField("ZWrite問題は自動修正不可", GUILayout.Width(160));
            }
            else
            {
                if (GUILayout.Button("修正", GUILayout.Width(60))) FixMaterial(m);
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
        if (GUILayout.Button("選択したものを修正")) FixSelected();
        EditorGUILayout.HelpBox("ZWrite問題は自動修正しません。必ずユーザーがプレビューで確認し、明示的に操作してください。", MessageType.Warning);
    }

    // --- プレビュータブ ---
    private void DrawPreview()
    {
        EditorGUILayout.BeginVertical();
        EditorGUILayout.BeginHorizontal();
        previewMat = (Material)EditorGUILayout.ObjectField("プレビュー対象", previewMat, typeof(Material), false);
        if (GUILayout.Button("選択中", GUILayout.Width(80)) && Selection.activeObject is Material sel) previewMat = sel;
        EditorGUILayout.EndHorizontal();
        autoRotate = EditorGUILayout.Toggle("自動回転", autoRotate);
        showCompare = EditorGUILayout.Toggle("修正前後比較", showCompare);
        if (previewMat != null)
        {
            EditorGUILayout.LabelField($"シェーダー: {previewMat.shader.name}");
            EditorGUILayout.LabelField($"RQ: {previewMat.renderQueue}");
            int saved = GetSavedRQ(previewMat);
            if (saved > 0 && saved != previewMat.renderQueue)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"保存値: {saved}");
                if (GUILayout.Button("復元", GUILayout.Width(60)))
                {
                    Undo.RecordObject(previewMat, "RQ復元");
                    previewMat.renderQueue = saved;
                }
                EditorGUILayout.EndHorizontal();
            }
            previewScroll = EditorGUILayout.BeginScrollView(previewScroll, GUILayout.Height(480));
            DrawMultiPreview(previewMat, showCompare);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField("クイック設定", EditorStyles.boldLabel);
            int newRQ = EditorGUILayout.IntSlider("レンダーキュー", previewMat.renderQueue, 1000, 5000);
            if (newRQ != previewMat.renderQueue)
            {
                Undo.RecordObject(previewMat, "RQ変更");
                previewMat.renderQueue = newRQ;
            }
            if (previewMat.HasProperty("_ZWrite"))
            {
                EditorGUILayout.BeginHorizontal();
                bool zw = previewMat.GetFloat("_ZWrite") > 0;
                bool newZW = EditorGUILayout.Toggle("ZWrite", zw);
                if (newZW != zw)
                {
                    Undo.RecordObject(previewMat, "ZWrite変更");
                    previewMat.SetFloat("_ZWrite", newZW ? 1 : 0);
                }
                EditorGUILayout.HelpBox("ZWriteの自動修正は行いません。透明マテリアルのZWriteはONで問題が悪化する場合があります。", MessageType.Warning);
                EditorGUILayout.EndHorizontal();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("プレビューするマテリアルを選択してください", MessageType.Info);
        }
        EditorGUILayout.EndVertical();
    }

    // --- 保存・復元タブ ---
    private void DrawSave()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("レンダーキュー値の保存・復元", EditorStyles.boldLabel);
        savePath = EditorGUILayout.TextField("保存ファイル", savePath);
        EditorGUILayout.Space();
        if (GUILayout.Button("全マテリアル値を保存"))
        {
            foreach (var m in mats) SaveRQ(m.Mat, m.Mat.renderQueue);
            SaveAll();
            status = "全マテリアルの値を保存しました";
        }
        if (GUILayout.Button("保存値を全削除"))
        {
            savedRQ.Clear();
            SaveAll();
            status = "保存値を全削除しました";
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("保存値一覧", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var pair in savedRQ)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(pair.Key, GUILayout.Width(180));
            EditorGUILayout.LabelField(pair.Value.ToString(), GUILayout.Width(60));
            var m = mats.FirstOrDefault(mi => mi.Mat != null && mi.Mat.name == pair.Key);
            if (m != null && m.Mat.renderQueue != pair.Value)
            {
                if (GUILayout.Button("復元", GUILayout.Width(60)))
                {
                    Undo.RecordObject(m.Mat, "RQ復元");
                    m.Mat.renderQueue = pair.Value;
                }
            }
            if (GUILayout.Button("削除", GUILayout.Width(60)))
            {
                savedRQ.Remove(pair.Key);
                SaveAll();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // --- チュートリアル ---
    private void DrawTutorial()
    {
        EditorGUILayout.HelpBox(
            "レンダーキューは描画順序を決める値です。半透明・水中・ガラス越し・特殊効果など、多様な環境での見え方を一括シミュレートできます。\n\n" +
            "ZWrite問題は自動修正せず、ユーザーがプレビューと解説を見ながら明示的に設定してください。\n\n" +
            "マテリアルを選択し、問題点・推奨値・影響をリアルタイムで確認できます。", MessageType.Info);
    }

    // --- 問題検出 ---
    private int DetectProblem(MatInfo m)
    {
        int rq = m.Mat.renderQueue;
        string path = m.Path.ToLower();
        if (m.IsTransparent && rq >= 3000 && rq < 3500) return 1; // 水中
        if (m.IsTransparent && (path.Contains("wing") || path.Contains("effect") || path.Contains("particle")) && rq < 3500) return 2; // 透視
        if (m.IsTransparent && m.HasZWriteOn) return 3; // ZWrite
        if (rq >= 2495 && rq <= 2505 && rq != 2500) return 4; // スカイ
        if ((path.Contains("fur") || path.Contains("hair")) && rq > 2000 && rq < 2451) return 5; // ファー
        if (m.IsTransparent && m.Type == "アクセサリー" && rq > 2400 && rq < 2460) return 6; // アクセ
        // 7:順序矛盾は拡張用
        return 0;
    }
    private string ProblemDesc(int type) => type switch
    {
        1 => "水中で消える",
        2 => "透視問題",
        3 => "ZWrite問題",
        4 => "スカイボックス干渉",
        5 => "ファー問題",
        6 => "アクセサリー透明問題",
        7 => "描画順序矛盾",
        _ => "問題なし"
    };

    // --- 修正処理 ---
    private void FixMaterial(MatInfo m)
    {
        if (m == null || m.Problem == 0 || m.Mat == null) return;
        if (m.Problem == 3) return; // ZWriteは自動修正しない
        Undo.RecordObject(m.Mat, "RQ修正");
        int before = m.Mat.renderQueue;
        switch (m.Problem)
        {
            case 1: m.Mat.renderQueue = m.IsTransparent ? 2999 : 2000; break;
            case 2: m.Mat.renderQueue = m.Path.ToLower().Contains("wing") ? 3900 : 3000; break;
            case 4: m.Mat.renderQueue = m.Mat.renderQueue < 2500 ? 2000 : 3000; break;
            case 5: m.Mat.renderQueue = 2451; break;
            case 6: m.Mat.renderQueue = 2460; break;
        }
        m.Problem = DetectProblem(m);
        SaveRQ(m.Mat, m.Mat.renderQueue);
        status = $"{m.Mat.name}を{before}→{m.Mat.renderQueue}に修正";
    }
    private void FixAll()
    {
        int cnt = 0;
        foreach (var m in mats.Where(i => i.Problem > 0 && i.Problem != 3)) { FixMaterial(m); cnt++; }
        SaveAll();
        status = $"自動修正: {cnt}個";
    }
    private void FixType(int type)
    {
        int cnt = 0;
        foreach (var m in mats.Where(i => i.Problem == type)) { FixMaterial(m); cnt++; }
        SaveAll();
        status = $"修正: {cnt}個";
    }
    private void FixSelected()
    {
        int cnt = 0;
        foreach (var m in mats.Where(i => i.Selected && i.Problem > 0 && i.Problem != 3)) { FixMaterial(m); cnt++; }
        SaveAll();
        status = $"選択修正: {cnt}個";
    }

    // --- プレビュー描画 ---
    private void DrawMultiPreview(Material mat, bool compare)
    {
        string[] envs = { "通常", "水中", "ガラス", "スカイ", "他アバター", "暗い" };
        Color[] overlays = { Color.clear, new(0,0.3f,0.8f,0.2f), new(0.8f,0.8f,0.9f,0.1f), Color.clear, Color.clear, new(0,0,0,0.5f) };
        int[] testRQ = { mat.renderQueue, mat.renderQueue, mat.renderQueue, 2500, mat.renderQueue, mat.renderQueue };
        float h = 450;
        Rect total = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth - 30, h);
        float w = total.width / 2, ph = h / 3;
        if (Event.current.type != EventType.Repaint) return;
        if (autoRotate) { previewRot += 0.5f; if (previewRot >= 360f) previewRot = 0f; EditorUtility.SetDirty(this); }
        for (int i = 0; i < 6; i++)
        {
            int x = i % 2, y = i / 2;
            Rect panel = new(total.x + x * w, total.y + y * ph, w, ph);
            Rect draw = new(panel.x + 5, panel.y + 5, panel.width - 10, panel.height - 25);
            RenderPreview(draw, i, testRQ[i], mat, compare);
            EditorGUI.DrawRect(draw, overlays[i]);
            Rect label = new(panel.x, panel.y + panel.height - 20, panel.width, 20);
            EditorGUI.LabelField(label, $"{envs[i]} (RQ:{testRQ[i]})", EditorStyles.boldLabel);
        }
    }
    private void RenderPreview(Rect rect, int idx, int rq, Material mat, bool compare)
    {
        var pu = previews[idx];
        pu.BeginPreview(rect, GUIStyle.none);
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.hideFlags = HideFlags.HideAndDontSave;
        Matrix4x4 matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, previewRot, 0), Vector3.one);
        Material renderMat = new Material(mat);
        renderMat.renderQueue = rq;
        pu.DrawMesh(sphere.GetComponent<MeshFilter>().sharedMesh, matrix, renderMat, 0);
        if (compare)
        {
            // 修正前の状態も半透明で重ねて表示
            Material orig = new Material(mat);
            orig.renderQueue = mat.renderQueue;
            orig.color = new Color(1, 0, 0, 0.3f);
            Matrix4x4 offset = Matrix4x4.TRS(new Vector3(0.3f, 0, 0), Quaternion.Euler(0, previewRot, 0), Vector3.one * 0.9f);
            pu.DrawMesh(sphere.GetComponent<MeshFilter>().sharedMesh, offset, orig, 0);
        }
        switch (idx)
        {
            case 1: DrawWaterPlane(pu); break;
            case 2: DrawGlassPlane(pu); break;
            case 4: DrawOtherAvatar(pu); break;
        }
        pu.camera.clearFlags = CameraClearFlags.Skybox;
        pu.Render();
        GUI.DrawTexture(rect, pu.EndPreview(), ScaleMode.ScaleToFit, false);
        Object.DestroyImmediate(sphere);
    }
    private void DrawWaterPlane(PreviewRenderUtility pu)
    {
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.hideFlags = HideFlags.HideAndDontSave;
        Material waterMat = new Material(Shader.Find("Standard"));
        waterMat.SetColor("_Color", new Color(0, 0.5f, 1f, 0.7f));
        waterMat.SetFloat("_Glossiness", 0.9f);
        waterMat.EnableKeyword("_ALPHABLEND_ON");
        waterMat.renderQueue = 3000;
        Matrix4x4 waterMatrix = Matrix4x4.TRS(new Vector3(0, 0.5f, 0), Quaternion.identity, new Vector3(0.1f, 1f, 0.1f));
        pu.DrawMesh(plane.GetComponent<MeshFilter>().sharedMesh, waterMatrix, waterMat, 0);
        Object.DestroyImmediate(plane);
    }
    private void DrawGlassPlane(PreviewRenderUtility pu)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.hideFlags = HideFlags.HideAndDontSave;
        Material glassMat = new Material(Shader.Find("Standard"));
        glassMat.SetColor("_Color", new Color(0.8f, 0.8f, 0.9f, 0.3f));
        glassMat.SetFloat("_Glossiness", 0.95f);
        glassMat.EnableKeyword("_ALPHABLEND_ON");
        glassMat.renderQueue = 3000;
        Matrix4x4 glassMatrix = Matrix4x4.TRS(new Vector3(0, 0, -2f), Quaternion.identity, new Vector3(5f, 5f, 1f));
        pu.DrawMesh(quad.GetComponent<MeshFilter>().sharedMesh, glassMatrix, glassMat, 0);
        Object.DestroyImmediate(quad);
    }
    private void DrawOtherAvatar(PreviewRenderUtility pu)
    {
        var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        cap.hideFlags = HideFlags.HideAndDontSave;
        Material avMat = new Material(Shader.Find("Standard"));
        avMat.SetColor("_Color", new Color(0.8f, 0.2f, 0.2f, 0.5f));
        avMat.EnableKeyword("_ALPHABLEND_ON");
        avMat.renderQueue = 3000;
        Matrix4x4 avMatrix = Matrix4x4.TRS(new Vector3(-0.5f, 0, -3f), Quaternion.Euler(0, 30, 0), new Vector3(0.5f, 1f, 0.5f));
        pu.DrawMesh(cap.GetComponent<MeshFilter>().sharedMesh, avMatrix, avMat, 0);
        Object.DestroyImmediate(cap);
    }

    // --- ユーティリティ ---
    private string GuessType(GameObject obj)
    {
        string n = obj.name.ToLower(), p = obj.transform.GetPath().ToLower();
        if (n.Contains("jewel") || p.Contains("jewel")) return "アクセサリー";
        if (n.Contains("hair") || p.Contains("/hair/")) return "髪";
        if (n.Contains("cloth") || n.Contains("wear") || p.Contains("/cloth/") || p.Contains("/wear/")) return "服";
        if (n.Contains("body") || n.Contains("face") || n.Contains("skin")) return "体";
        return "その他";
    }
    private bool IsTransparent(Material m)
    {
        if (!m || !m.shader) return false;
        string name = m.shader.name.ToLower();
        if (name.Contains("transparent") || name.Contains("cutout") || name.Contains("fade") || name.Contains("alpha")) return true;
        if (m.renderQueue >= 3000) return true;
        if (m.HasProperty("_Mode") && m.GetFloat("_Mode") > 0) return true;
        if (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0) return true;
        if (m.HasProperty("_BlendMode") && m.GetFloat("_BlendMode") > 0) return true;
        if (m.HasProperty("_Cutoff")) return true;
        return false;
    }
    private bool IsZWriteOn(Material m)
    {
        if (!m) return false;
        if (m.HasProperty("_ZWrite")) return m.GetFloat("_ZWrite") > 0;
        if (m.HasProperty("_ZWrite1")) return m.GetFloat("_ZWrite1") > 0;
        return !IsTransparent(m);
    }
    private void SaveRQ(Material m, int rq)
    {
        if (m == null) return;
        savedRQ[m.name] = rq;
        var info = mats.FirstOrDefault(i => i.Mat == m);
        if (info != null) info.SavedRQ = rq;
    }
    private int GetSavedRQ(Material m)
    {
        if (m == null) return -1;
        return savedRQ.TryGetValue(m.name, out int v) ? v : -1;
    }
    private void SaveAll()
    {
        try
        {
            var arr = savedRQ.Select(pair => new SaveItem { name = pair.Key, rq = pair.Value }).ToArray();
            string json = JsonUtility.ToJson(new SaveContainer { items = arr }, true);
            File.WriteAllText(savePath, json);
        }
        catch { }
    }
    private void LoadSaved()
    {
        savedRQ.Clear();
        if (string.IsNullOrEmpty(savePath) || !File.Exists(savePath)) return;
        try
        {
            string json = File.ReadAllText(savePath);
            SaveContainer c = JsonUtility.FromJson<SaveContainer>(json);
            if (c?.items != null) foreach (var item in c.items) savedRQ[item.name] = item.rq;
        }
        catch { }
    }

    [System.Serializable] private class SaveItem { public string name; public int rq; }
    [System.Serializable] private class SaveContainer { public SaveItem[] items; }
}

// --- Transform拡張 ---
public static class TransformExtensions
{
    public static string GetPath(this Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
