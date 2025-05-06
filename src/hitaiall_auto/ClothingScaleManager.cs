#if UNITY_EDITOR // このスクリプト全体がUnityエディタでのみコンパイルされるようにする

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Linq; // Linq を使うために追加

// --- MA関連の using は #if で囲む ---
#if MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif

// --- データ構造定義 (V3) ---
[System.Serializable]
public class ClothingScaleDataV3
{
    public string clothingName;      // 衣装オブジェクトの名前
    public string targetAvatarName;  // アバターオブジェクトの名前
    public float scaleValue;         // 抽出したスケール値 (X軸)
}

[System.Serializable]
public class CharacterScaleDatabaseV3
{
    public List<ClothingScaleDataV3> scaleEntries = new List<ClothingScaleDataV3>();
}


// --- メインの管理ツールウィンドウ (V3) ---
public class ClothingScaleManagerV3 : EditorWindow
{
    // --- 定数 ---
    private const string DB_FILE_PATH = "Assets/ScaleData/scale_database_v3.json";
    private const string MENU_SCRIPT_PATH = "Assets/Editor/ClothScaleAdjusterV3.cs";
    private const string WINDOW_TITLE = "衣装スケール管理 V3";
    private const string MENU_ROOT_PATH = "GameObject/Apply Clothing Scale V3"; // メニューのルートパス

    // --- MA利用可能フラグ (コンパイル時に決定) ---
#if MODULAR_AVATAR
    private const bool MA_AVAILABLE = true;
#else
    private const bool MA_AVAILABLE = false;
#endif

    // --- タブ定義 ---
    private enum Tab { Setup, Extract, Generate }
    private Tab currentTab = Tab.Setup;

    // --- セットアップタブ用変数 ---
    private GameObject setupAvatarObject;
    private GameObject setupClothingObject;
    private string selectedClothingNameForSetup = "";
    private string selectedTargetAvatarNameForSetup = "";

    // --- スケール抽出タブ用変数 ---
    private GameObject extractSourceClothingObject;
    private GameObject extractClothingRefObject;
    private GameObject extractTargetAvatarRefObject;

    // --- 共通変数 ---
    private CharacterScaleDatabaseV3 scaleDatabase = null;
    private Vector2 scrollPosition;

    // --- メニューからウィンドウを開く ---
    [MenuItem("Tools/" + WINDOW_TITLE)]
    public static void ShowWindow()
    {
        GetWindow<ClothingScaleManagerV3>(WINDOW_TITLE);
    }

    // --- 初期化 ---
    void OnEnable()
    {
        LoadScaleDatabase();
    }

    // --- GUI描画 ---
    void OnGUI()
    {
        GUILayout.Space(10);
        DrawTabs();
        GUILayout.Space(10);

        switch (currentTab)
        {
            case Tab.Setup:
                DrawSetupTab();
                break;
            case Tab.Extract:
                DrawExtractTab();
                break;
            case Tab.Generate:
                DrawGenerateTab();
                break;
        }

        if (!MA_AVAILABLE && currentTab == Tab.Setup)
        {
             EditorGUILayout.HelpBox("Modular Avatar がプロジェクトに見つからないか、認識されていません。\n(Scripting Define Symbols に 'MODULAR_AVATAR' が必要かもしれません)\nMA連携機能は無効になります。", MessageType.Warning);
        }
    }

    // タブ描画
    private void DrawTabs()
    {
        EditorGUILayout.BeginHorizontal();
        currentTab = GUILayout.Toggle(currentTab == Tab.Setup, "改変セットアップ", EditorStyles.toolbarButton) ? Tab.Setup : currentTab;
        currentTab = GUILayout.Toggle(currentTab == Tab.Extract, "スケール抽出", EditorStyles.toolbarButton) ? Tab.Extract : currentTab;
        currentTab = GUILayout.Toggle(currentTab == Tab.Generate, "メニュー生成", EditorStyles.toolbarButton) ? Tab.Generate : currentTab;
        EditorGUILayout.EndHorizontal();
    }

    // --- 「改変セットアップ」タブ ---
    void DrawSetupTab()
    {
        GUILayout.Label("非対応衣装 改変セットアップ", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1. アバターと衣装のルートオブジェクトを下の欄にドラッグ＆ドロップ。\n" +
            "2. 適用したい保存済みスケールデータをリストから選択。\n" +
            "3. 「スケール適用＆基本セットアップ実行」ボタンをクリック。\n" +
            (MA_AVAILABLE ? "(Modular Avatar 連携が有効です)" : "(注意: Modular Avatar が認識されていません)"), MessageType.Info);
        GUILayout.Space(10);

        setupAvatarObject = EditorGUILayout.ObjectField("アバターオブジェクト", setupAvatarObject, typeof(GameObject), true) as GameObject;
        setupClothingObject = EditorGUILayout.ObjectField("衣装オブジェクト", setupClothingObject, typeof(GameObject), true) as GameObject;

        GUILayout.Space(10);
        GUILayout.Label("適用するスケールデータを選択", EditorStyles.boldLabel);
        DrawScaleSelectionListForSetup();

        GUILayout.Space(10);
        EditorGUI.BeginDisabledGroup(setupAvatarObject == null || setupClothingObject == null || string.IsNullOrEmpty(selectedClothingNameForSetup) || string.IsNullOrEmpty(selectedTargetAvatarNameForSetup) || !MA_AVAILABLE);
        if (GUILayout.Button("スケール適用＆基本セットアップ実行", GUILayout.Height(40)))
        {
            ExecuteSetup();
        }
        EditorGUI.EndDisabledGroup();
    }

    // セットアップタブ用 スケールデータ選択リスト
    void DrawScaleSelectionListForSetup()
    {
        if (scaleDatabase == null || scaleDatabase.scaleEntries.Count == 0)
        {
            EditorGUILayout.HelpBox("保存されているスケールデータがありません。「スケール抽出」タブでデータを追加してください。", MessageType.Warning);
            return;
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(150));
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        string currentSelectionKey = $"{selectedClothingNameForSetup}/{selectedTargetAvatarNameForSetup}";

        foreach (var entry in scaleDatabase.scaleEntries.OrderBy(e => e.clothingName).ThenBy(e => e.targetAvatarName))
        {
            string entryLabel = $"{entry.clothingName} / {entry.targetAvatarName} (Scale: {entry.scaleValue})";
            string entryKey = $"{entry.clothingName}/{entry.targetAvatarName}";

            if (EditorGUILayout.ToggleLeft(entryLabel, currentSelectionKey == entryKey, EditorStyles.radioButton))
            {
                if (currentSelectionKey != entryKey)
                {
                    selectedClothingNameForSetup = entry.clothingName;
                    selectedTargetAvatarNameForSetup = entry.targetAvatarName;
                }
            }
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndScrollView();
    }

    // --- 「スケール抽出」タブ ---
    void DrawExtractTab()
    {
        GUILayout.Label("衣装スケール抽出 (オブジェクト指定)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1. 下の欄にそれぞれのGameObjectをドラッグ＆ドロップ。\n" +
            "   - スケール元: スケール値(X軸)を取得する衣装オブジェクト。\n" +
            "   - 衣装名参照: 保存する「衣装名」として使うオブジェクト。\n" +
            "   - 対象アバター参照: 保存する「対象アバター名」として使うオブジェクト。\n" +
            "2. 「現在の設定でスケールを抽出」ボタンをクリック。", MessageType.Info);
        GUILayout.Space(10);

        extractSourceClothingObject = EditorGUILayout.ObjectField("スケール元 衣装Obj", extractSourceClothingObject, typeof(GameObject), true) as GameObject;
        extractClothingRefObject = EditorGUILayout.ObjectField("衣装名 参照Obj", extractClothingRefObject, typeof(GameObject), true) as GameObject;
        extractTargetAvatarRefObject = EditorGUILayout.ObjectField("対象アバター 参照Obj", extractTargetAvatarRefObject, typeof(GameObject), true) as GameObject;

        GUILayout.Space(10);
        EditorGUI.BeginDisabledGroup(extractSourceClothingObject == null || extractClothingRefObject == null || extractTargetAvatarRefObject == null);
        if (GUILayout.Button("現在の設定でスケールを抽出", GUILayout.Height(30)))
        {
            SaveCurrentScaleFromObjects();
        }
        EditorGUI.EndDisabledGroup();

        GUILayout.Space(20);
        if (GUILayout.Button("スケールデータベースを保存 (手動)", GUILayout.Height(30)))
        {
            SaveScaleDatabase();
        }
    }

    // --- 「メニュー生成」タブ ---
    void DrawGenerateTab()
    {
        GUILayout.Label("GameObjectメニュー生成", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("保存されたスケール値を「GameObject」メニューから直接適用できるようにします。\nデータベースを更新したら、再度生成してUnityを再起動してください。", MessageType.Info);
        GUILayout.Space(10);

        if (GUILayout.Button("メニュースクリプトを生成", GUILayout.Height(30)))
        {
            GenerateMenuScript();
        }
    }

    // --- コア処理 ---

    // セットアップ実行
    private void ExecuteSetup()
    {
        if (!MA_AVAILABLE) { ShowModularAvatarError(); return; }
        if (setupAvatarObject == null || setupClothingObject == null || string.IsNullOrEmpty(selectedClothingNameForSetup) || string.IsNullOrEmpty(selectedTargetAvatarNameForSetup)) return;

        ClothingScaleDataV3 selectedData = scaleDatabase?.scaleEntries?.Find(
            e => e.clothingName == selectedClothingNameForSetup && e.targetAvatarName == selectedTargetAvatarNameForSetup);

        if (selectedData == null) { EditorUtility.DisplayDialog("エラー", "選択されたスケールデータが見つかりません。", "OK"); return; }

        Debug.Log($"セットアップ開始: 衣装 '{setupClothingObject.name}' を アバター '{setupAvatarObject.name}' に スケール {selectedData.scaleValue} で適用します。");

        Undo.SetTransformParent(setupClothingObject.transform, setupAvatarObject.transform, "Set Clothing Parent");
        Debug.Log($"衣装 '{setupClothingObject.name}' を アバター '{setupAvatarObject.name}' の子に設定しました。");

        ApplyScaleAndPosition(setupClothingObject.transform, selectedData.scaleValue);
        Debug.Log($"スケール {selectedData.scaleValue} を適用し、位置を調整しました。");

#if MODULAR_AVATAR
        SetupModularAvatarMergeArmature(setupClothingObject.transform);
#endif

        EditorUtility.DisplayDialog("完了", $"衣装 '{setupClothingObject.name}' の基本セットアップが完了しました。\n- 親子関係を設定\n- スケール {selectedData.scaleValue} を適用\n" + (MA_AVAILABLE ? "- Modular Avatar Merge Armature を設定\n" : "\n") + "\n次は手動でボーン位置の微調整や貫通対策を行ってください。", "OK");
    }

    // スケールと位置適用
    private void ApplyScaleAndPosition(Transform targetTransform, float scaleValue)
    {
        if (targetTransform == null) return;
        Vector3 originalWorldPosition = targetTransform.position;
        Quaternion originalWorldRotation = targetTransform.rotation;
        Undo.RecordObject(targetTransform, "Apply Scale and Position");
        targetTransform.localScale = Vector3.one * scaleValue;
        targetTransform.position = originalWorldPosition;
        targetTransform.rotation = originalWorldRotation;
        EditorUtility.SetDirty(targetTransform);
    }

    // MA設定メソッド
#if MODULAR_AVATAR
private void SetupModularAvatarMergeArmature(Transform clothingRoot)
{
    if (clothingRoot == null) return;
    Transform clothingArmatureRoot = FindArmatureRoot(clothingRoot);
    if (clothingArmatureRoot == null)
    {
        Debug.LogWarning($"衣装 '{clothingRoot.name}' の Armature ルートが見つかりませんでした。MA Merge Armature の設定をスキップします。");
        return;
    }

    ModularAvatarMergeArmature maMergeArmature = clothingArmatureRoot.gameObject.GetComponent<ModularAvatarMergeArmature>();
    if (maMergeArmature == null)
    {
        maMergeArmature = Undo.AddComponent<ModularAvatarMergeArmature>(clothingArmatureRoot.gameObject);
        Debug.Log($"ModularAvatarMergeArmature を '{clothingArmatureRoot.name}' に追加しました。");
    } else {
        Debug.Log($"既存の ModularAvatarMergeArmature を '{clothingArmatureRoot.name}' で使用します。");
    }

    // 以下の2行をコメントアウトまたは削除
    // maMergeArmature.pathMode = ArmaturePathMode.Relative;
    // maMergeArmature.matchAvatarExpressions = true;

    EditorUtility.SetDirty(maMergeArmature);
    Debug.Log($"ModularAvatarMergeArmature の設定を更新しました。");
}
#endif



    // Armatureルート検索
    private Transform FindArmatureRoot(Transform root)
    {
        if (root == null) return null;
        Transform armature = root.Find("Armature");
        if (armature != null) return armature;
        foreach (Transform child in root) { if (child.name == "Hips") return child.parent; }
        SkinnedMeshRenderer smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr != null && smr.rootBone != null)
        {
            Transform current = smr.rootBone;
            while (current != null && current.parent != null && current.parent != root) { current = current.parent; }
            if (current != null && current.parent == root) { return current; }
        }
        return null;
    }

    // --- データベース関連 ---

    private void LoadScaleDatabase()
    {
        if (File.Exists(DB_FILE_PATH))
        {
            try {
                string json = File.ReadAllText(DB_FILE_PATH);
                scaleDatabase = JsonUtility.FromJson<CharacterScaleDatabaseV3>(json);
            } catch (System.Exception e) {
                Debug.LogError($"データベースファイルの読み込みに失敗しました: {DB_FILE_PATH}\n{e}");
                scaleDatabase = null;
            }
        }
        if (scaleDatabase == null) scaleDatabase = new CharacterScaleDatabaseV3();
        if (scaleDatabase.scaleEntries == null) scaleDatabase.scaleEntries = new List<ClothingScaleDataV3>();
        Debug.Log($"スケールデータベース(V3)を読み込みました。{scaleDatabase.scaleEntries.Count} 件");
    }

    // スケール抽出 (ObjectField版)
    private void SaveCurrentScaleFromObjects()
    {
        if (extractSourceClothingObject == null || extractClothingRefObject == null || extractTargetAvatarRefObject == null)
        {
            EditorUtility.DisplayDialog("エラー", "「スケール元」「衣装名参照」「対象アバター参照」の全てのオブジェクトを指定してください。", "OK");
            return;
        }

        Transform sourceTransform = extractSourceClothingObject.transform;
        float scaleValue = sourceTransform.localScale.x;
        string clothingName = extractClothingRefObject.name;
        string targetAvatarName = extractTargetAvatarRefObject.name;

        ClothingScaleDataV3 newEntry = new ClothingScaleDataV3 {
            clothingName = clothingName, targetAvatarName = targetAvatarName, scaleValue = scaleValue
        };

        LoadScaleDatabase();

        int existingIndex = scaleDatabase.scaleEntries.FindIndex(
            e => e.clothingName == clothingName && e.targetAvatarName == targetAvatarName);

        if (existingIndex != -1) {
            scaleDatabase.scaleEntries[existingIndex] = newEntry;
            Debug.Log($"スケールデータ更新: {clothingName} / {targetAvatarName} = {scaleValue}");
        } else {
            scaleDatabase.scaleEntries.Add(newEntry);
            Debug.Log($"スケールデータ追加: {clothingName} / {targetAvatarName} = {scaleValue}");
        }

        SaveScaleDatabase(); // 自動保存

        EditorUtility.DisplayDialog("完了",
            $"スケール値 {scaleValue} を保存しました：\n衣装：{clothingName}\n対象：{targetAvatarName}", "OK");
    }

    // データベース保存
    private void SaveScaleDatabase()
    {
        if (scaleDatabase == null) return;
        try
        {
             string directoryPath = Path.GetDirectoryName(DB_FILE_PATH);
             if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);
             string json = JsonUtility.ToJson(scaleDatabase, true);
             File.WriteAllText(DB_FILE_PATH, json);
             AssetDatabase.Refresh();
             Debug.Log($"スケールデータベース(V3)を保存しました: {DB_FILE_PATH}");
        }
        catch (System.Exception e)
        {
             Debug.LogError($"スケールデータベース(V3)の保存に失敗: {e.Message}");
             EditorUtility.DisplayDialog("エラー", "スケールデータベース(V3)の保存に失敗しました。", "OK");
        }
    }

    // --- メニュー生成 ---
    private void GenerateMenuScript()
    {
        LoadScaleDatabase();
        if (scaleDatabase == null || scaleDatabase.scaleEntries.Count == 0)
        {
            EditorUtility.DisplayDialog("エラー", "スケールデータが存在しません。", "OK");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("using UnityEngine;");
        sb.AppendLine("using UnityEditor;");
        sb.AppendLine("");
        sb.AppendLine("// This script was automatically generated by ClothingScaleManagerV3. Do not edit directly.");
        sb.AppendLine("public class ClothScaleAdjusterV3");
        sb.AppendLine("{");
        sb.AppendLine($"    private const string MENU_ROOT = \"{MENU_ROOT_PATH}\";"); // 定数を生成コード内にも定義

        int menuOrder = 50;
        foreach (var entry in scaleDatabase.scaleEntries.OrderBy(e => e.clothingName).ThenBy(e => e.targetAvatarName))
        {
            string methodName = $"Apply_{CleanName(entry.clothingName)}_To_{CleanName(entry.targetAvatarName)}";
            string menuPath = $"{MENU_ROOT_PATH}/{CleanNameForMenu(entry.clothingName)}/{CleanNameForMenu(entry.targetAvatarName)} (Scale {entry.scaleValue})";

            sb.AppendLine($"    [MenuItem(\"{menuPath}\", false, {menuOrder})]");
            // ApplyScaleToSelectionメソッドを呼び出すように変更。引数としてscaleValueとundoName文字列を渡す。
            // undoName文字列を正しくエスケープする (\")
            string undoNameString = $"Apply Scale ({entry.clothingName} for {entry.targetAvatarName})";
            sb.AppendLine($"    private static void {methodName}() => ApplyScaleToSelection({entry.scaleValue}f, \"{undoNameString.Replace("\"", "\\\"")}\");"); // f を忘れずに
            sb.AppendLine("");
            menuOrder++;
        }

        // --- ★★★ここからが修正箇所★★★ ---
        // ヘルパーメソッド（ApplyScaleToSelection）の定義。引数名を正しく記述。
        sb.AppendLine("    private static void ApplyScaleToSelection(float scaleValue, string undoName)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (Selection.activeTransform != null)");
        sb.AppendLine("        {");
        // Undoメッセージには引数 undoName をそのまま使用
        sb.AppendLine("            Undo.RecordObject(Selection.activeTransform, undoName);"); // $"" を削除し、変数名を直接使用
        sb.AppendLine("            Vector3 originalPosition = Selection.activeTransform.position;");
        sb.AppendLine("            Quaternion originalRotation = Selection.activeTransform.rotation;");
        // スケール適用には引数 scaleValue をそのまま使用
        sb.AppendLine("            Selection.activeTransform.localScale = Vector3.one * scaleValue;"); // $"" を削除し、変数名を直接使用
        sb.AppendLine("            Selection.activeTransform.position = originalPosition;");
        sb.AppendLine("            Selection.activeTransform.rotation = originalRotation;");
        sb.AppendLine("            EditorUtility.SetDirty(Selection.activeTransform);");
        // Debugログには引数 scaleValue をそのまま使用し、文字列補間を正しく使う
        sb.AppendLine("            Debug.Log($\"Applied scale {scaleValue} to {Selection.activeTransform.name} via menu.\");"); // 変数名を{}で囲む
        sb.AppendLine("        }");
        sb.AppendLine("        else { Debug.LogWarning(\"No object selected to apply scale.\"); }");
        sb.AppendLine("    }");
        sb.AppendLine("");
        // --- ★★★修正箇所ここまで★★★ ---

        // バリデーションメソッド
        sb.AppendLine($"    [MenuItem(MENU_ROOT, true)]"); // 定数を参照
        sb.AppendLine("    private static bool ValidateSelection()");
        sb.AppendLine("    { return Selection.activeTransform != null; }");

        sb.AppendLine("}");

        // ファイル書き込み
        try
        {
            string directory = Path.GetDirectoryName(MENU_SCRIPT_PATH);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(MENU_SCRIPT_PATH, sb.ToString());
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("完了", "スケールメニューを生成しました (V3)。\nUnityエディタの再起動または再コンパイル後にメニューが更新されます。", "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"メニュースクリプト(V3)の生成に失敗: {e.Message}");
            EditorUtility.DisplayDialog("エラー", "メニュースクリプト(V3)の生成に失敗しました。", "OK");
        }
    }

    // --- ヘルパーメソッド ---
    private string CleanName(string input)
    {
        if (string.IsNullOrEmpty(input)) return "Default";
        StringBuilder sb = new StringBuilder();
        bool nextUpper = true;
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(nextUpper ? char.ToUpper(c) : c); nextUpper = false; }
            else { nextUpper = true; }
        }
        return sb.Length > 0 ? sb.ToString() : "Default";
    }

    private string CleanNameForMenu(string input)
    {
         if (string.IsNullOrEmpty(input)) return "_";
        return input.Replace("/", "／").Replace(".", "_").Replace(" ", "_");
    }

     // MAエラー表示
     private void ShowModularAvatarError()
     {
          EditorUtility.DisplayDialog("エラー", "Modular Avatar がプロジェクトにインポートされていないか、正しく認識されていません。\n(Scripting Define Symbols に 'MODULAR_AVATAR' が必要かもしれません)", "OK");
     }

} // ClothingScaleManagerV3 クラスの終わり

#endif // UNITY_EDITOR の終わり
