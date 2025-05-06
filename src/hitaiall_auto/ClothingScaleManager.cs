#if UNITY_EDITOR // このスクript全体がUnityエディタでのみコンパイルされるようにする

using UnityEngine;
using UnityEditor;
using System.IO; // Pathクラスのために必要 (ScaleSyncProEditor側で使うため)
using System.Collections.Generic;
using System.Text;
using System.Linq;

// --- MA関連の using は #if で囲む ---
#if MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif

// --- データ構造定義 ---
[System.Serializable]
public class ScaleSyncData
{
    public string clothingName;
    public string targetAvatarName;
    public float scaleValue;

    public ScaleSyncData(string clothing, string avatar, float scale)
    {
        clothingName = clothing;
        targetAvatarName = avatar;
        scaleValue = scale;
    }
}

[System.Serializable]
public class ScaleSyncDatabase
{
    public List<ScaleSyncData> scaleEntries = new List<ScaleSyncData>();
}


// --- メインのエディタウィンドウクラス ---
public class ScaleSyncProEditor : EditorWindow
{
    // --- 定数 ---
    private const string PRODUCT_NAME = "ScaleSync Pro";
    private const string DATABASE_FILENAME = "ScaleSyncPro_Database.json";
    private const string GENERATED_MENU_SCRIPT_FILENAME = "ScaleSyncPro_GeneratedMenu.cs";
    private const string DATABASE_DIRECTORY = "Assets/ScaleSyncProData";
    private const string MENU_ROOT_PATH_IN_GENERATED_SCRIPT = "GameObject/" + PRODUCT_NAME + "/Apply Saved Scale";

    // Pathクラスを使うのはこちらのスクリプト内のみなので、static readonlyで定義
    private static readonly string DatabasePath = Path.Combine(DATABASE_DIRECTORY, DATABASE_FILENAME);
    private static readonly string GeneratedMenuScriptPath = Path.Combine("Assets", "Editor", GENERATED_MENU_SCRIPT_FILENAME);
    // ★ 生成されるスクリプト内で使うクラス名を定数化 (固定文字列として)
    private static readonly string GeneratedMenuScriptClassName = "ScaleSyncPro_GeneratedMenu";


#if MODULAR_AVATAR
    private const bool IS_MA_AVAILABLE = true;
#else
    private const bool IS_MA_AVAILABLE = false;
#endif

    private enum Tab { Setup, Extract, Generate }
    private Tab currentTab = Tab.Setup;
    private Vector2 scrollPosition;

    private GameObject avatarForSetup;
    private GameObject clothingForSetup;
    private ScaleSyncData selectedDataForSetup;

    private GameObject sourceClothingObject;
    private GameObject clothingRefObject;
    private GameObject targetAvatarRefObject;

    private ScaleSyncDatabase scaleDatabase;

    [MenuItem("Tools/" + PRODUCT_NAME)]
    public static void ShowWindow()
    {
        GetWindow<ScaleSyncProEditor>(PRODUCT_NAME);
    }

    private void OnEnable()
    {
        LoadDatabase();
    }

    private void OnGUI()
    {
        GUILayout.Space(10);
        DrawTabs();
        GUILayout.Space(10);

        switch (currentTab)
        {
            case Tab.Setup: DrawSetupTab(); break;
            case Tab.Extract: DrawExtractTab(); break;
            case Tab.Generate: DrawGenerateTab(); break;
        }

        if (!IS_MA_AVAILABLE && currentTab == Tab.Setup)
        {
             EditorGUILayout.HelpBox(
                "Modular Avatar がプロジェクトにインポートされていないか、正しく認識されていません。\n" +
                "(Scripting Define Symbols に 'MODULAR_AVATAR' が設定されているか確認してください)\n" +
                "Modular Avatar 連携機能は現在無効です。",
                MessageType.Warning);
        }
    }

    private void DrawTabs()
    {
        EditorGUILayout.BeginHorizontal();
        currentTab = GUILayout.Toggle(currentTab == Tab.Setup, "改変セットアップ", EditorStyles.toolbarButton) ? Tab.Setup : currentTab;
        currentTab = GUILayout.Toggle(currentTab == Tab.Extract, "スケール抽出", EditorStyles.toolbarButton) ? Tab.Extract : currentTab;
        currentTab = GUILayout.Toggle(currentTab == Tab.Generate, "GameObjectメニュー生成", EditorStyles.toolbarButton) ? Tab.Generate : currentTab;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSetupTab()
    {
        GUILayout.Label("非対応衣装 改変セットアップ", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1. アバターと衣装のルートオブジェクトを下の欄にドラッグ＆ドロップします。\n" +
            "2. 適用したい保存済みスケールデータをリストから選択します。\n" +
            "3. 「実行」ボタンをクリックすると、スケール適用と基本的なMA設定が行われます。\n" + // 衣装オンオフメニューの記述は削除
            (IS_MA_AVAILABLE ? "(Modular Avatar 連携が有効です)" : "(注意: Modular Avatar 連携は無効です)"),
            MessageType.Info);
        GUILayout.Space(10);

        avatarForSetup = EditorGUILayout.ObjectField("アバターオブジェクト", avatarForSetup, typeof(GameObject), true) as GameObject;
        clothingForSetup = EditorGUILayout.ObjectField("衣装オブジェクト", clothingForSetup, typeof(GameObject), true) as GameObject;
        GUILayout.Space(10);

        GUILayout.Label("適用するスケールデータを選択", EditorStyles.boldLabel);
        DrawScaleSelectionListForSetupGUI();
        GUILayout.Space(10);

        // MAが利用できない場合でもスケール適用だけはできるようにするか検討 → MA連携が主目的なので無効のまま
        EditorGUI.BeginDisabledGroup(avatarForSetup == null || clothingForSetup == null || selectedDataForSetup == null || !IS_MA_AVAILABLE);
        if (GUILayout.Button("スケール適用＆基本MAセットアップ実行", GUILayout.Height(40))) // ボタン名を戻す
        {
            ExecuteAutomatedSetup();
        }
        EditorGUI.EndDisabledGroup();
    }

    private void DrawScaleSelectionListForSetupGUI()
    {
        if (scaleDatabase == null || !scaleDatabase.scaleEntries.Any())
        {
            EditorGUILayout.HelpBox("保存されているスケールデータがありません。「スケール抽出」タブでデータを追加してください。", MessageType.Warning);
            return;
        }
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(150));
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        foreach (var entry in scaleDatabase.scaleEntries.OrderBy(e => e.clothingName).ThenBy(e => e.targetAvatarName))
        {
            string entryLabel = $"{entry.clothingName} / {entry.targetAvatarName} (Scale: {entry.scaleValue:F3})";
            bool isSelected = selectedDataForSetup == entry;
            if (EditorGUILayout.ToggleLeft(entryLabel, isSelected, EditorStyles.radioButton))
            {
                if (!isSelected) selectedDataForSetup = entry;
            }
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndScrollView();
    }

    private void DrawExtractTab()
    {
        GUILayout.Label("衣装スケール抽出 (オブジェクト指定)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1. 各フィールドに参照するGameObjectをドラッグ＆ドロップします。\n" +
            "   - スケール元 衣装: スケール値(X軸)を取得する対象の衣装オブジェクト。\n" +
            "   - 衣装名 参照: 保存する「衣装名」として名前を使用するオブジェクト。\n" +
            "   - 対象アバター 参照: 保存する「対象アバター名」として名前を使用するオブジェクト。\n" +
            "2. 「現在の設定でスケールを抽出・保存」ボタンをクリックします。",
            MessageType.Info);
        GUILayout.Space(10);
        sourceClothingObject = EditorGUILayout.ObjectField("スケール元 衣装Obj", sourceClothingObject, typeof(GameObject), true) as GameObject;
        clothingRefObject = EditorGUILayout.ObjectField("衣装名 参照Obj", clothingRefObject, typeof(GameObject), true) as GameObject;
        targetAvatarRefObject = EditorGUILayout.ObjectField("対象アバター 参照Obj", targetAvatarRefObject, typeof(GameObject), true) as GameObject;
        GUILayout.Space(10);
        EditorGUI.BeginDisabledGroup(sourceClothingObject == null || clothingRefObject == null || targetAvatarRefObject == null);
        if (GUILayout.Button("現在の設定でスケールを抽出・保存", GUILayout.Height(30)))
        {
            ExtractAndSaveScaleFromObjects();
        }
        EditorGUI.EndDisabledGroup();
        GUILayout.Space(20);
        if (GUILayout.Button("現在のデータベースをJSONに保存 (手動)", GUILayout.Height(30)))
        {
            SaveDatabase();
        }
    }

    private void DrawGenerateTab()
    {
        GUILayout.Label("GameObjectメニュー自動生成", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"保存されているスケールデータを、UnityのGameObjectメニューから直接適用できるようにするスクリプト ({GENERATED_MENU_SCRIPT_FILENAME}) を自動生成します。\n" +
            "この機能は、スケール値の適用のみを行います。(MA設定やその他の自動設定は行いません)\n" +
            "データベースを更新した場合は、再度このボタンを押してスクリプトを再生成し、Unityを再起動するかスクリプトを再コンパイルしてください。",
            MessageType.Info);
        GUILayout.Space(10);

        if (GUILayout.Button($"メニュースクリプトを生成 ({GENERATED_MENU_SCRIPT_FILENAME})", GUILayout.Height(30)))
        {
            GenerateMenuScript();
        }
    }

    private void ExecuteAutomatedSetup()
    {
        // MAが利用できない場合は処理を中断（ボタンが無効化されているはずだが念のため）
        if (!IS_MA_AVAILABLE) { ShowMAErrorDialog(); return; }
        if (avatarForSetup == null || clothingForSetup == null || selectedDataForSetup == null)
        {
            Debug.LogError($"{PRODUCT_NAME}: セットアップに必要なオブジェクトまたはデータが選択されていません。");
            return;
        }

        string clothingName = clothingForSetup.name;
        string avatarName = avatarForSetup.name;
        float scaleToApply = selectedDataForSetup.scaleValue;

        Debug.Log($"{PRODUCT_NAME}: セットアップ開始 - 衣装「{clothingName}」をアバター「{avatarName}」にスケール {scaleToApply:F3} で適用します。");

        // アバターへの親子付け
        Undo.SetTransformParent(clothingForSetup.transform, avatarForSetup.transform, "Set Clothing Parent to Avatar");
        Debug.Log($"{PRODUCT_NAME}: 衣装「{clothingName}」をアバター「{avatarName}」の子オブジェクトに設定しました。");

        // スケール適用（ワールド位置・回転保持）
        ApplyScaleAndPreserveWorldTransform(clothingForSetup.transform, scaleToApply);
        Debug.Log($"{PRODUCT_NAME}: スケール {scaleToApply:F3} を適用し、ワールド座標を保持しました。");

        // Modular Avatar Merge Armature コンポーネントの追加
#if MODULAR_AVATAR
        SetupMAComponent(clothingForSetup.transform);
#endif

        // 完了ダイアログ
        EditorUtility.DisplayDialog(PRODUCT_NAME,
            $"衣装「{clothingName}」の基本セットアップが完了しました。\n" +
            $"- 親子関係: 設定済み\n" +
            $"- スケール: {scaleToApply:F3} に適用済み\n" +
            (IS_MA_AVAILABLE ? "- Modular Avatar Merge Armature: 設定/確認済み\n" : "\n") +
            "\n【次のステップ】\n" +
            "手動でボーン位置の微調整、ウェイト調整、メッシュの貫通対策などを行ってください。",
            "OK");
    }

    // スケールを適用しつつワールド座標を保持
    private void ApplyScaleAndPreserveWorldTransform(Transform targetTransform, float scaleValue)
    {
        if (targetTransform == null) { Debug.LogError($"{PRODUCT_NAME}: スケール適用対象のTransformがnullです。"); return; }
        Vector3 originalWorldPosition = targetTransform.position;
        Quaternion originalWorldRotation = targetTransform.rotation;
        Undo.RecordObject(targetTransform, "Apply Scale and Preserve World Transform");
        targetTransform.localScale = Vector3.one * scaleValue;
        targetTransform.position = originalWorldPosition;
        targetTransform.rotation = originalWorldRotation;
        EditorUtility.SetDirty(targetTransform);
    }

#if MODULAR_AVATAR
    // Modular Avatar Merge Armature コンポーネントを追加する（シンプル版）
    private void SetupMAComponent(Transform clothingRootTransform)
    {
        if (clothingRootTransform == null) return;
        // Armatureを探す、なければ衣装ルートを使う
        Transform armatureTargetTransform = FindArmatureRootRecursive(clothingRootTransform) ?? clothingRootTransform;

        ModularAvatarMergeArmature maComponent = armatureTargetTransform.gameObject.GetComponent<ModularAvatarMergeArmature>();
        if (maComponent == null)
        {
            maComponent = Undo.AddComponent<ModularAvatarMergeArmature>(armatureTargetTransform.gameObject);
            Debug.Log($"{PRODUCT_NAME}: ModularAvatarMergeArmature を「{armatureTargetTransform.name}」に追加しました。");
        }
        else
        {
            Debug.Log($"{PRODUCT_NAME}: 既存の ModularAvatarMergeArmature を「{armatureTargetTransform.name}」で使用します。");
        }
        EditorUtility.SetDirty(maComponent);
        Debug.Log($"{PRODUCT_NAME}: ModularAvatarMergeArmature の設定を確認/更新しました。（基本的なボーンマージ用）");
    }
#endif

    // Armature ルートを探す（再帰）
    private Transform FindArmatureRootRecursive(Transform currentTransform)
    {
        if (currentTransform == null) return null;
        // 一般的な名前でチェック
        if (currentTransform.name.Equals("Armature", System.StringComparison.OrdinalIgnoreCase)) return currentTransform;
        if (currentTransform.name.Equals("armature", System.StringComparison.OrdinalIgnoreCase)) return currentTransform; // 小文字も考慮

        // Hipsの親をチェック（ただしルート直下でない場合）
        if (currentTransform.name.Equals("Hips", System.StringComparison.OrdinalIgnoreCase) && currentTransform.parent != null)
        {
            // 親がアバター/衣装ルートでなければ、それがArmatureの可能性が高い
            bool isRootChildHips = (avatarForSetup != null && currentTransform.parent == avatarForSetup.transform) ||
                                   (clothingForSetup != null && currentTransform.parent == clothingForSetup.transform);
            if (!isRootChildHips) return currentTransform.parent;
        }

        // SkinnedMeshRendererのrootBoneから辿る
        SkinnedMeshRenderer smr = currentTransform.GetComponentInChildren<SkinnedMeshRenderer>(true); // 子要素も含めて検索
        if (smr != null && smr.rootBone != null)
        {
            Transform boneWalker = smr.rootBone;
            // rootBoneが現在のTransformの直接の子 or 孫(...)階層にあるかチェック
            Transform parentCheck = boneWalker;
            while(parentCheck != null)
            {
                if(parentCheck == currentTransform) break; // currentTransformの子孫である
                parentCheck = parentCheck.parent;
            }

            if(parentCheck == currentTransform) // rootBoneがcurrentTransformの子孫の場合
            {
                // HipsがrootBoneならその親を返す
                if (boneWalker.name.Equals("Hips", System.StringComparison.OrdinalIgnoreCase) && boneWalker.parent != currentTransform)
                    return boneWalker.parent;
                // そうでなければrootBone自体か、その親がArmatureかもしれないので親を返す
                return boneWalker.parent != currentTransform ? boneWalker.parent : boneWalker;
            }
        }

        // 子を再帰的に探索
        foreach (Transform child in currentTransform)
        {
            Transform found = FindArmatureRootRecursive(child);
            if (found != null) return found;
        }

        return null; // 見つからなければnull
    }

    // データベースをロード
    private void LoadDatabase()
    {
        if (File.Exists(DatabasePath))
        {
            try {
                string json = File.ReadAllText(DatabasePath);
                scaleDatabase = JsonUtility.FromJson<ScaleSyncDatabase>(json);
            } catch (System.Exception e) {
                Debug.LogError($"{PRODUCT_NAME}: データベースファイルの読み込みに失敗しました。\nPath: {DatabasePath}\nError: {e.Message}");
                scaleDatabase = null; // エラー時はnullリセット
            }
        }
        // nullチェックと初期化を強化
        if (scaleDatabase == null) scaleDatabase = new ScaleSyncDatabase();
        if (scaleDatabase.scaleEntries == null) scaleDatabase.scaleEntries = new List<ScaleSyncData>();
        Debug.Log($"{PRODUCT_NAME}: スケールデータベースを読み込みました。登録数: {scaleDatabase.scaleEntries.Count}件");
    }

    // スケール抽出＆保存
    private void ExtractAndSaveScaleFromObjects()
    {
        if (sourceClothingObject == null || clothingRefObject == null || targetAvatarRefObject == null)
        {
            EditorUtility.DisplayDialog("エラー", "「スケール元 衣装」「衣装名 参照」「対象アバター 参照」の全てのオブジェクトを指定してください。", "OK");
            return;
        }
        float extractedScale = sourceClothingObject.transform.localScale.x;
        string clothingObjectName = clothingRefObject.name;
        string targetAvatarObjectName = targetAvatarRefObject.name;
        var newEntry = new ScaleSyncData(clothingObjectName, targetAvatarObjectName, extractedScale);
        LoadDatabase(); // 最新をロード
        int existingIndex = scaleDatabase.scaleEntries.FindIndex(
            e => e.clothingName == clothingObjectName && e.targetAvatarName == targetAvatarObjectName);
        if (existingIndex != -1) {
            scaleDatabase.scaleEntries[existingIndex] = newEntry;
            Debug.Log($"{PRODUCT_NAME}: スケールデータを更新しました - 衣装: {clothingObjectName}, アバター: {targetAvatarObjectName}, スケール: {extractedScale:F3}");
        } else {
            scaleDatabase.scaleEntries.Add(newEntry);
            Debug.Log($"{PRODUCT_NAME}: 新しいスケールデータを追加しました - 衣装: {clothingObjectName}, アバター: {targetAvatarObjectName}, スケール: {extractedScale:F3}");
        }
        SaveDatabase(); // 保存実行
        EditorUtility.DisplayDialog(PRODUCT_NAME,
            $"スケール値 {extractedScale:F3} を保存しました。\n" +
            $"衣装: {clothingObjectName}\n" +
            $"対象アバター: {targetAvatarObjectName}", "OK");
    }

    // データベースを保存
    private void SaveDatabase()
    {
        if (scaleDatabase == null) { Debug.LogError($"{PRODUCT_NAME}: データベースがnullのため保存できませんでした。"); return; }
        try {
            string directory = Path.GetDirectoryName(DatabasePath);
            if (!Directory.Exists(directory)) { Directory.CreateDirectory(directory); Debug.Log($"{PRODUCT_NAME}: データ保存用ディレクトリを作成しました: {directory}"); }
            string json = JsonUtility.ToJson(scaleDatabase, true);
            File.WriteAllText(DatabasePath, json);
            AssetDatabase.Refresh();
            Debug.Log($"{PRODUCT_NAME}: スケールデータベースを保存しました。Path: {DatabasePath}");
        } catch (System.Exception e) {
            Debug.LogError($"{PRODUCT_NAME}: スケールデータベースの保存に失敗しました。\nPath: {DatabasePath}\nError: {e.Message}");
            EditorUtility.DisplayDialog("エラー", "スケールデータベースの保存に失敗しました。詳細はコンソールを確認してください。", "OK");
        }
    }

    // --- ★★★ ここからがメニュー生成のエラー修正箇所 ★★★ ---
    private void GenerateMenuScript()
    {
        LoadDatabase();
        if (scaleDatabase == null || !scaleDatabase.scaleEntries.Any())
        {
            EditorUtility.DisplayDialog("エラー", "保存されているスケールデータがありません。「スケール抽出」タブでデータを追加してください。", "OK");
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine("#if UNITY_EDITOR");
        sb.AppendLine("using UnityEngine;");
        sb.AppendLine("using UnityEditor;");
        // sb.AppendLine("using System.IO;"); // ★ Pathクラスを使わないので不要
        sb.AppendLine("");
        sb.AppendLine($"// This script was automatically generated by {PRODUCT_NAME}. Do not edit directly.");
        sb.AppendLine($"public class {GeneratedMenuScriptClassName}"); // ★ 定数を使用
        sb.AppendLine("{");
        sb.AppendLine($"    private const string GENERATED_MENU_ROOT = \"{MENU_ROOT_PATH_IN_GENERATED_SCRIPT}\";");
        sb.AppendLine("");
        int menuOrder = 10;
        foreach (var entry in scaleDatabase.scaleEntries.OrderBy(e => e.clothingName).ThenBy(e => e.targetAvatarName))
        {
            string cleanClothingName = SanitizeForMenu(entry.clothingName);
            string cleanAvatarName = SanitizeForMenu(entry.targetAvatarName);
            string methodName = $"Apply_{SanitizeForMethodName(entry.clothingName)}_To_{SanitizeForMethodName(entry.targetAvatarName)}";
            // 生成されるコード内で GENERATED_MENU_ROOT 定数を文字列結合で使う
            string menuPath = $"GENERATED_MENU_ROOT + \"/{cleanClothingName}/{cleanAvatarName} (Scale {entry.scaleValue:F3})\"";
            string undoName = $"Apply Scale ({entry.clothingName} for {entry.targetAvatarName})";
            sb.AppendLine($"    [MenuItem({menuPath}, false, {menuOrder})]");
            sb.AppendLine($"    private static void {methodName}() => ApplyScaleToSelection_GeneratedMenu({entry.scaleValue}f, \"{EscapeStringForCode(undoName)}\");");
            sb.AppendLine("");
            menuOrder++;
        }
        sb.AppendLine("    private static void ApplyScaleToSelection_GeneratedMenu(float scale, string undoMsg)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (Selection.activeTransform != null)");
        sb.AppendLine("        {");
        sb.AppendLine("            Undo.RecordObject(Selection.activeTransform, undoMsg);");
        sb.AppendLine("            Vector3 originalPos = Selection.activeTransform.position;");
        sb.AppendLine("            Quaternion originalRot = Selection.activeTransform.rotation;");
        sb.AppendLine("            Selection.activeTransform.localScale = Vector3.one * scale;");
        sb.AppendLine("            Selection.activeTransform.position = originalPos;");
        sb.AppendLine("            Selection.activeTransform.rotation = originalRot;");
        sb.AppendLine("            EditorUtility.SetDirty(Selection.activeTransform);");
        // ★ デバッグログの修正: Path や GeneratedMenuScriptPath を参照せず、固定のクラス名を使用
        sb.AppendLine($"            Debug.Log($\"[{GeneratedMenuScriptClassName}] Applied scale {{scale:F3}} to {{Selection.activeTransform.name}}.\");");
        sb.AppendLine("        }");
        sb.AppendLine("        else { Debug.LogWarning(\"No object selected to apply scale.\"); }");
        sb.AppendLine("    }");
        sb.AppendLine("");
        sb.AppendLine($"    [MenuItem(GENERATED_MENU_ROOT, true)]");
        sb.AppendLine("    private static bool ValidateGeneratedMenuSelection()");
        sb.AppendLine("    { return Selection.activeTransform != null; }");
        sb.AppendLine("}");
        sb.AppendLine("#endif // UNITY_EDITOR");
        try {
            string directory = Path.GetDirectoryName(GeneratedMenuScriptPath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(GeneratedMenuScriptPath, sb.ToString());
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog(PRODUCT_NAME,
                "GameObjectメニュー用のスクリプトを生成しました。\n" +
                "Unityエディタの再起動、またはスクリプトの再コンパイル後にメニューが更新されます。", "OK");
        } catch (System.Exception e) {
            Debug.LogError($"{PRODUCT_NAME}: メニュースクリプトの生成に失敗しました。\nPath: {GeneratedMenuScriptPath}\nError: {e.Message}");
            EditorUtility.DisplayDialog("エラー", "メニュースクリプトの生成に失敗しました。詳細はコンソールを確認してください。", "OK");
        }
    }
    // --- ★★★ メニュー生成のエラー修正箇所ここまで ★★★ ---

    // --- ヘルパーメソッド ---
    private string SanitizeForMethodName(string input)
    {
        if (string.IsNullOrEmpty(input)) return "Default";
        var sb = new StringBuilder();
        bool capitalizeNext = true;
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(capitalizeNext ? char.ToUpper(c) : c); capitalizeNext = false; }
            else { capitalizeNext = true; }
        }
        // メソッド名の先頭が数字にならないようにする (もしあれば "_" を追加)
        if (sb.Length > 0 && char.IsDigit(sb[0])) sb.Insert(0, "_");
        return sb.Length > 0 ? sb.ToString() : "Default";
    }

    private string SanitizeForMenu(string input)
    {
         if (string.IsNullOrEmpty(input)) return "_";
        // メニューパスに使えない可能性のある文字を置換
        return input.Replace("/", "／").Replace(".", "_").Replace(" ", "_").Replace("(", "（").Replace(")", "）").Replace("[", "【").Replace("]", "】").Replace(":", "：");
    }

    private string EscapeStringForCode(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return input.Replace("\\", "\\\\").Replace("\"", "\\\""); // バックスラッシュとダブルクォートをエスケープ
    }

    private void ShowMAErrorDialog()
    {
        EditorUtility.DisplayDialog($"エラー: {PRODUCT_NAME} - Modular Avatar 未検出",
            "この機能を利用するには、Modular Avatar がプロジェクトにインポートされ、正しく認識されている必要があります。\n\n" +
            "【確認事項】\n" +
            "1. VRChat Creator Companion (VCC) から Modular Avatar が最新版でインストールされているか。\n" +
            "2. Unityの Console に Modular Avatar 関連のエラーが出ていないか。\n" +
            "3. (稀なケース) Project Settings > Player > Scripting Define Symbols に 'MODULAR_AVATAR' が含まれているか。",
            "OK");
    }
}
#endif // UNITY_EDITOR の終わり
