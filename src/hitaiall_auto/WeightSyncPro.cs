#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

// --- WeightSyncProウィンドウ本体 ---
public class WeightSyncProWindow : EditorWindow
{
    // 設定値
    GameObject avObj, clObj;
    SkinnedMeshRenderer avRen, clRen;
    string remP = "", remS = "";
    TransferOptions opts = new TransferOptions();

    // 診断・進捗
    string procStep = "";
    List<DiagnosticMessage> diags = new List<DiagnosticMessage>();

    [MenuItem("Tools/WeightSyncPro 1.1.0")]
    static void Init() => GetWindow<WeightSyncProWindow>("WeightSyncPro").minSize = new Vector2(500,600);

    void OnGUI()
    {
        GUILayout.Label("WeightSyncPro 1.1.0", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("1. アバターと衣装を選択し、2. オプションを設定して、3. ウェイト転送を実行してください。", MessageType.Info);

        avObj = (GameObject)EditorGUILayout.ObjectField("アバター(基準)", avObj, typeof(GameObject), true);
        clObj = (GameObject)EditorGUILayout.ObjectField("衣装(転送先)", clObj, typeof(GameObject), true);

        opts.maxBonesPerVertex = EditorGUILayout.IntSlider("Max Bones/Vertex", opts.maxBonesPerVertex, 1, 8);
        opts.minBoneWeightThreshold = EditorGUILayout.Slider("Min Bone Weight", opts.minBoneWeightThreshold, 0f, 0.1f);
        opts.autoNormalizeWeights = EditorGUILayout.Toggle("Auto Normalize Weights", opts.autoNormalizeWeights);
        remP = EditorGUILayout.TextField("除去プレフィックス", remP);
        remS = EditorGUILayout.TextField("除去サフィックス", remS);

        if (GUILayout.Button("ウェイト転送実行", GUILayout.Height(40)))
        {
            if (EditorUtility.DisplayDialog("最終確認", "ウェイト転送を実行しますか？", "実行", "キャンセル"))
                ExecTrans();
        }

        // 診断表示
        if (!string.IsNullOrEmpty(procStep)) EditorGUILayout.HelpBox($"状態:{procStep}", MessageType.None);
        if (diags.Count > 0)
        {
            foreach (var m in diags) EditorGUILayout.HelpBox($"[{m.type}]{m.message}", m.GetMessageType());
            if (GUILayout.Button("診断クリア", GUILayout.Width(150))) { diags.Clear(); Repaint(); }
        }
    }

    void ExecTrans()
    {
        procStep = "初期化...";
        diags.Clear();
        ValRens();
        if (!avRen || !clRen || !avRen.sharedMesh || !clRen.sharedMesh)
        {
            AddDiag("SMR/メッシュ無効", DiagnosticType.Error);
            procStep = "エラー:無効SMR/メッシュ";
            Repaint(); return;
        }
        Undo.RecordObject(clRen, "WSP:SMR Update");
        if (clRen.sharedMesh) Undo.RecordObject(clRen.sharedMesh, "WSP:Mesh Update");

        try
        {
            procStep = "ボーンマップ中...";
            var avR = BoneMappingCore.FindRootBone(avRen.transform, this);
            var clR = BoneMappingCore.FindRootBone(clRen.transform, this);
            var cbm = BoneMappingCore.CreateBoneMap(clR, avR, remP, remS, this);
            if (cbm.Count == 0) throw new Exception("ボーンマップ失敗");

            procStep = "ウェイト転送/最適化中...";
            WeightTransferCore.ProcessSkinnedMesh(clRen, avRen, cbm, opts, this);

            procStep = "完了";
            EditorUtility.DisplayDialog("成功", "WSP完了", "OK");
        }
        catch (Exception ex)
        {
            AddDiag($"致命的エラー:{ex.Message}", DiagnosticType.Error);
            procStep = $"致命的エラー:{ex.GetType().Name}";
        }
        finally { EditorUtility.ClearProgressBar(); Repaint(); }
    }

    void ValRens()
    {
        avRen = avObj ? avObj.GetComponentInChildren<SkinnedMeshRenderer>() : null;
        clRen = clObj ? clObj.GetComponentInChildren<SkinnedMeshRenderer>() : null;
    }

    public void AddDiag(string m, DiagnosticType t, Transform b1=null, Transform b2=null, int vIdx=-1)
    {
        diags.Add(new DiagnosticMessage { message = m, type = t, bone1 = b1, bone2 = b2, vertIdx = vIdx });
        Repaint();
    }
    public void Log(string m, UnityEngine.Object c=null) => Debug.Log($"[WSP]{m}", c);
    public void LogWarn(string m, UnityEngine.Object c=null) => Debug.LogWarning($"[WSP]{m}", c);
    public void LogError(string m, UnityEngine.Object c=null) => Debug.LogError($"[WSP]{m}", c);

    public class TransferOptions
    {
        public int maxBonesPerVertex = 4;
        public float minBoneWeightThreshold = 0.001f;
        public bool autoNormalizeWeights = true;
    }
    public enum DiagnosticType { Info, Warning, Error }
    public class DiagnosticMessage
    {
        public string message;
        public DiagnosticType type;
        public Transform bone1, bone2;
        public int vertIdx;
        public MessageType GetMessageType() =>
            type == DiagnosticType.Error ? MessageType.Error :
            type == DiagnosticType.Warning ? MessageType.Warning : MessageType.Info;
    }
}

// --- ボーンマッピング/ウェイト転送コアはWeightSyncPro.cs内に既に実装済み ---
// BoneMappingCore, WeightTransferCore も同ファイル内に存在
#endif
