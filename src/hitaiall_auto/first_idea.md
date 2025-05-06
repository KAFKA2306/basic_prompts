# スケール値を外部JSONファイルで管理するUnityエディタ拡張ツール

以下は、全てのコードセクションで`JsonUtility.FromJson()`メソッドに適切なジェネリック型パラメータを追加した、完全に修正されたコードです。

## スケールデータ構造

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ClothingScaleData
{
    public string clothingName;
    public string targetCharacter;
    public float scaleValue;
}

[Serializable]
public class CharacterScaleDatabase
{
    public List scaleEntries = new List();
}
```

## スケール抽出ツール

```csharp
using UnityEngine;
using UnityEditor;
using System.IO;

public class ScaleExtractor : EditorWindow
{
    private string clothingName = "";
    private string targetCharacter = "";
    private CharacterScaleDatabase scaleDatabase = null;
    private string databasePath = "Assets/ScaleData/scale_database.json";

    [MenuItem("Tools/Scale Extractor")]
    public static void ShowWindow()
    {
        GetWindow("スケール抽出");
    }

    void OnGUI()
    {
        GUILayout.Label("キャラクター衣装スケール抽出", EditorStyles.boldLabel);
        
        clothingName = EditorGUILayout.TextField("衣装名", clothingName);
        targetCharacter = EditorGUILayout.TextField("着せるキャラクター", targetCharacter);
        
        if (GUILayout.Button("現在の選択オブジェクトのスケールを保存"))
        {
            SaveCurrentScale();
        }
        
        if (GUILayout.Button("スケールデータベースを保存"))
        {
            SaveScaleDatabase();
        }
    }

    void OnEnable()
    {
        LoadScaleDatabase();
    }

    private void LoadScaleDatabase()
    {
        if (File.Exists(databasePath))
        {
            string json = File.ReadAllText(databasePath);
            scaleDatabase = JsonUtility.FromJson(json);
        }
        else
        {
            scaleDatabase = new CharacterScaleDatabase();
        }
    }

    private void SaveCurrentScale()
    {
        if (Selection.activeTransform == null)
        {
            EditorUtility.DisplayDialog("エラー", "オブジェクトを選択してください", "OK");
            return;
        }

        if (string.IsNullOrEmpty(clothingName) || string.IsNullOrEmpty(targetCharacter))
        {
            EditorUtility.DisplayDialog("エラー", "衣装名とキャラクター名を入力してください", "OK");
            return;
        }

        float scaleValue = Selection.activeTransform.localScale.x;

        ClothingScaleData newEntry = new ClothingScaleData
        {
            clothingName = clothingName,
            targetCharacter = targetCharacter,
            scaleValue = scaleValue
        };

        // 既存エントリの更新またはリストへの追加
        bool entryUpdated = false;
        for (int i = 0; i  foldouts = new Dictionary();

    [MenuItem("Tools/Scale Applicator")]
    public static void ShowWindow()
    {
        GetWindow("スケール適用");
    }

    void OnEnable()
    {
        LoadScaleDatabase();
    }

    void OnGUI()
    {
        GUILayout.Label("キャラクター衣装スケール適用", EditorStyles.boldLabel);

        if (scaleDatabase == null || scaleDatabase.scaleEntries.Count == 0)
        {
            EditorGUILayout.HelpBox("スケールデータが存在しません", MessageType.Info);
            if (GUILayout.Button("データベースの再読込"))
            {
                LoadScaleDatabase();
            }
            return;
        }

        // 衣装ごとにグループ化
        Dictionary> groupedEntries = new Dictionary>();
        
        foreach (var entry in scaleDatabase.scaleEntries)
        {
            if (!groupedEntries.ContainsKey(entry.clothingName))
            {
                groupedEntries[entry.clothingName] = new List();
            }
            groupedEntries[entry.clothingName].Add(entry);
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        // 各衣装ごとにフォールドアウトとボタン表示
        foreach (var clothingGroup in groupedEntries)
        {
            string clothingName = clothingGroup.Key;
            
            if (!foldouts.ContainsKey(clothingName))
            {
                foldouts[clothingName] = false;
            }
            
            foldouts[clothingName] = EditorGUILayout.Foldout(foldouts[clothingName], clothingName, true);
            
            if (foldouts[clothingName])
            {
                EditorGUI.indentLevel++;
                
                foreach (var entry in clothingGroup.Value)
                {
                    if (GUILayout.Button($"{entry.targetCharacter}に着せる (Scale: {entry.scaleValue})"))
                    {
                        ApplyScale(entry.scaleValue);
                    }
                }
                
                EditorGUI.indentLevel--;
            }
        }
        
        EditorGUILayout.EndScrollView();
    }

    private void LoadScaleDatabase()
    {
        if (File.Exists(databasePath))
        {
            string json = File.ReadAllText(databasePath);
            scaleDatabase = JsonUtility.FromJson(json);
        }
        else
        {
            scaleDatabase = new CharacterScaleDatabase();
        }
    }

    private void ApplyScale(float scaleValue)
    {
        if (Selection.activeTransform == null)
        {
            EditorUtility.DisplayDialog("エラー", "スケールを適用するオブジェクトを選択してください", "OK");
            return;
        }

        Undo.RecordObject(Selection.activeTransform, "Apply Scale");
        Selection.activeTransform.localScale = Vector3.one * scaleValue;
    }
}
```

## メニュー生成ツール

```csharp
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Collections.Generic;

public class ScaleMenuGenerator : EditorWindow
{
    private CharacterScaleDatabase scaleDatabase = null;
    private string databasePath = "Assets/ScaleData/scale_database.json";
    private string scriptOutputPath = "Assets/Editor/ClothScaleAdjuster.cs";
    
    [MenuItem("Tools/Generate Scale Menu")]
    public static void ShowWindow()
    {
        GetWindow("スケールメニュー生成");
    }
    
    void OnEnable()
    {
        LoadScaleDatabase();
    }
    
    void OnGUI()
    {
        GUILayout.Label("スケールメニュー生成", EditorStyles.boldLabel);
        
        if (GUILayout.Button("メニュースクリプトを生成"))
        {
            GenerateMenuScript();
        }
    }
    
    private void LoadScaleDatabase()
    {
        if (File.Exists(databasePath))
        {
            string json = File.ReadAllText(databasePath);
            scaleDatabase = JsonUtility.FromJson(json);
        }
        else
        {
            scaleDatabase = new CharacterScaleDatabase();
        }
    }
    
    private void GenerateMenuScript()
    {
        if (scaleDatabase == null || scaleDatabase.scaleEntries.Count == 0)
        {
            EditorUtility.DisplayDialog("エラー", "スケールデータが存在しません", "OK");
            return;
        }
        
        StringBuilder sb = new StringBuilder();
        
        // スクリプト冒頭部分
        sb.AppendLine("using UnityEngine;");
        sb.AppendLine("using UnityEditor;");
        sb.AppendLine("");
        sb.AppendLine("public class ClothScaleAdjuster");
        sb.AppendLine("{");
        
        // 衣装ごとにグループ化
        Dictionary> groupedEntries = new Dictionary>();
        
        foreach (var entry in scaleDatabase.scaleEntries)
        {
            if (!groupedEntries.ContainsKey(entry.clothingName))
            {
                groupedEntries[entry.clothingName] = new List();
            }
            groupedEntries[entry.clothingName].Add(entry);
        }
        
        int menuOrder = 50;
        
        // 各衣装・キャラクターの組み合わせごとにメソッドを生成
        foreach (var clothingGroup in groupedEntries)
        {
            string clothingName = clothingGroup.Key;
            
            // コメント
            sb.AppendLine($"    // {clothingName}衣装");
            
            foreach (var entry in clothingGroup.Value)
            {
                string methodName = $"Adjust{CleanName(clothingName)}To{CleanName(entry.targetCharacter)}";
                
                // メソッド生成
                sb.AppendLine($"    [MenuItem(\"GameObject/ClothScaleAdjuster/{clothingName}/{entry.targetCharacter}に着せる\", false, {menuOrder})]");
                sb.AppendLine($"    public static void {methodName}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        Selection.activeTransform.localScale = Vector3.one * {entry.scaleValue}f;");
                sb.AppendLine("    }");
                sb.AppendLine("");
                
                menuOrder++;
            }
        }
        
        // バリデーションメソッド
        sb.AppendLine("    // バリデーションメソッド");
        sb.AppendLine("    [MenuItem(\"GameObject/ClothScaleAdjuster\", true)]");
        sb.AppendLine("    public static bool ValidateSelection()");
        sb.AppendLine("    {");
        sb.AppendLine("        return Selection.activeTransform != null;");
        sb.AppendLine("    }");
        
        // スクリプト終了
        sb.AppendLine("}");
        
        // ディレクトリ作成
        string directory = Path.GetDirectoryName(scriptOutputPath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        // スクリプト保存
        File.WriteAllText(scriptOutputPath, sb.ToString());
        AssetDatabase.Refresh();
        
        EditorUtility.DisplayDialog("完了", "スケールメニューを生成しました", "OK");
    }
    
    private string CleanName(string input)
    {
        // 特殊文字や空白を削除して有効なC#識別子にする
        StringBuilder sb = new StringBuilder();
        bool nextUpper = true;
        
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(nextUpper ? char.ToUpper(c) : c);
                nextUpper = false;
            }
            else
            {
                nextUpper = true;
            }
        }
        
        return sb.ToString();
    }
}
```

## 統合ツール（ClothingScaleManager）

```csharp
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text;

public class ClothingScaleManager : EditorWindow
{
    private enum Tab { Extract, Apply, Generate }
    
    private Tab currentTab = Tab.Extract;
    private string clothingName = "";
    private string targetCharacter = "";
    private CharacterScaleDatabase scaleDatabase = null;
    private string databasePath = "Assets/ScaleData/scale_database.json";
    private string scriptOutputPath = "Assets/Editor/ClothScaleAdjuster.cs";
    private Vector2 scrollPosition;
    private Dictionary foldouts = new Dictionary();
    
    [MenuItem("Tools/Clothing Scale Manager")]
    public static void ShowWindow()
    {
        GetWindow("衣装スケール管理");
    }
    
    void OnEnable()
    {
        LoadScaleDatabase();
    }
    
    void OnGUI()
    {
        GUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Toggle(currentTab == Tab.Extract, "スケール抽出", EditorStyles.toolbarButton))
            currentTab = Tab.Extract;
        
        if (GUILayout.Toggle(currentTab == Tab.Apply, "スケール適用", EditorStyles.toolbarButton))
            currentTab = Tab.Apply;
        
        if (GUILayout.Toggle(currentTab == Tab.Generate, "メニュー生成", EditorStyles.toolbarButton))
            currentTab = Tab.Generate;
        
        EditorGUILayout.EndHorizontal();
        
        GUILayout.Space(10);
        
        switch (currentTab)
        {
            case Tab.Extract:
                DrawExtractTab();
                break;
            case Tab.Apply:
                DrawApplyTab();
                break;
            case Tab.Generate:
                DrawGenerateTab();
                break;
        }
    }
    
    void DrawExtractTab()
    {
        GUILayout.Label("キャラクター衣装スケール抽出", EditorStyles.boldLabel);
        
        clothingName = EditorGUILayout.TextField("衣装名", clothingName);
        targetCharacter = EditorGUILayout.TextField("着せるキャラクター", targetCharacter);
        
        GUILayout.Space(10);
        
        EditorGUILayout.HelpBox("1. スケールを抽出したい衣装オブジェクトを選択\n2. 衣装名とキャラクター名を入力\n3. 「スケール抽出」ボタンをクリック", MessageType.Info);
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("現在の選択オブジェクトのスケールを抽出", GUILayout.Height(30)))
        {
            SaveCurrentScale();
        }
        
        GUILayout.Space(20);
        
        if (GUILayout.Button("スケールデータベースを保存", GUILayout.Height(30)))
        {
            SaveScaleDatabase();
        }
    }
    
    void DrawApplyTab()
    {
        GUILayout.Label("キャラクター衣装スケール適用", EditorStyles.boldLabel);
        
        if (scaleDatabase == null || scaleDatabase.scaleEntries.Count == 0)
        {
            EditorGUILayout.HelpBox("スケールデータが存在しません", MessageType.Info);
            if (GUILayout.Button("データベースの再読込"))
            {
                LoadScaleDatabase();
            }
            return;
        }
        
        EditorGUILayout.HelpBox("1. スケールを適用したい衣装オブジェクトを選択\n2. 目的のキャラクター「に着せる」ボタンをクリック", MessageType.Info);
        
        GUILayout.Space(10);
        
        // 衣装ごとにグループ化
        Dictionary> groupedEntries = new Dictionary>();
        
        foreach (var entry in scaleDatabase.scaleEntries)
        {
            if (!groupedEntries.ContainsKey(entry.clothingName))
            {
                groupedEntries[entry.clothingName] = new List();
            }
            groupedEntries[entry.clothingName].Add(entry);
        }
        
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        // 各衣装ごとにフォールドアウトとボタン表示
        foreach (var clothingGroup in groupedEntries)
        {
            string clothingName = clothingGroup.Key;
            
            if (!foldouts.ContainsKey(clothingName))
            {
                foldouts[clothingName] = false;
            }
            
            foldouts[clothingName] = EditorGUILayout.Foldout(foldouts[clothingName], clothingName, true);
            
            if (foldouts[clothingName])
            {
                EditorGUI.indentLevel++;
                
                foreach (var entry in clothingGroup.Value)
                {
                    if (GUILayout.Button($"{entry.targetCharacter}に着せる (Scale: {entry.scaleValue})"))
                    {
                        ApplyScale(entry.scaleValue);
                    }
                }
                
                EditorGUI.indentLevel--;
            }
        }
        
        EditorGUILayout.EndScrollView();
    }
    
    void DrawGenerateTab()
    {
        GUILayout.Label("スケールメニュー生成", EditorStyles.boldLabel);
        
        EditorGUILayout.HelpBox("「GameObject」メニューに「ClothScaleAdjuster」サブメニューを生成します。\nUnityエディタを再起動すると変更が反映されます。", MessageType.Info);
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("メニュースクリプトを生成", GUILayout.Height(30)))
        {
            GenerateMenuScript();
        }
    }
    
    private void LoadScaleDatabase()
    {
        if (File.Exists(databasePath))
        {
            string json = File.ReadAllText(databasePath);
            scaleDatabase = JsonUtility.FromJson(json);
        }
        else
        {
            scaleDatabase = new CharacterScaleDatabase();
        }
    }
    
    private void SaveCurrentScale()
    {
        if (Selection.activeTransform == null)
        {
            EditorUtility.DisplayDialog("エラー", "オブジェクトを選択してください", "OK");
            return;
        }
        
        if (string.IsNullOrEmpty(clothingName) || string.IsNullOrEmpty(targetCharacter))
        {
            EditorUtility.DisplayDialog("エラー", "衣装名とキャラクター名を入力してください", "OK");
            return;
        }
        
        float scaleValue = Selection.activeTransform.localScale.x;
        
        ClothingScaleData newEntry = new ClothingScaleData
        {
            clothingName = clothingName,
            targetCharacter = targetCharacter,
            scaleValue = scaleValue
        };
        
        // 既存エントリの更新またはリストへの追加
        bool entryUpdated = false;
        for (int i = 0; i > groupedEntries = new Dictionary>();
        
        foreach (var entry in scaleDatabase.scaleEntries)
        {
            if (!groupedEntries.ContainsKey(entry.clothingName))
            {
                groupedEntries[entry.clothingName] = new List();
            }
            groupedEntries[entry.clothingName].Add(entry);
        }
        
        int menuOrder = 50;
        
        // 各衣装・キャラクターの組み合わせごとにメソッドを生成
        foreach (var clothingGroup in groupedEntries)
        {
            string clothingName = clothingGroup.Key;
            
            // コメント
            sb.AppendLine($"    // {clothingName}衣装");
            
            foreach (var entry in clothingGroup.Value)
            {
                string methodName = $"Adjust{CleanName(clothingName)}To{CleanName(entry.targetCharacter)}";
                
                // メソッド生成
                sb.AppendLine($"    [MenuItem(\"GameObject/ClothScaleAdjuster/{clothingName}/{entry.targetCharacter}に着せる\", false, {menuOrder})]");
                sb.AppendLine($"    public static void {methodName}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        Selection.activeTransform.localScale = Vector3.one * {entry.scaleValue}f;");
                sb.AppendLine("    }");
                sb.AppendLine("");
                
                menuOrder++;
            }
        }
        
        // バリデーションメソッド
        sb.AppendLine("    // バリデーションメソッド");
        sb.AppendLine("    [MenuItem(\"GameObject/ClothScaleAdjuster\", true)]");
        sb.AppendLine("    public static bool ValidateSelection()");
        sb.AppendLine("    {");
        sb.AppendLine("        return Selection.activeTransform != null;");
        sb.AppendLine("    }");
        
        // スクリプト終了
        sb.AppendLine("}");
        
        // ディレクトリ作成
        string directory = Path.GetDirectoryName(scriptOutputPath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        // スクリプト保存
        File.WriteAllText(scriptOutputPath, sb.ToString());
        AssetDatabase.Refresh();
        
        EditorUtility.DisplayDialog("完了", "スケールメニューを生成しました\nUnityエディタを再起動すると変更が反映されます", "OK");
    }
    
    private string CleanName(string input)
    {
        // 特殊文字や空白を削除して有効なC#識別子にする
        StringBuilder sb = new StringBuilder();
        bool nextUpper = true;
        
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(nextUpper ? char.ToUpper(c) : c);
                nextUpper = false;
            }
            else
            {
                nextUpper = true;
            }
        }
        
        return sb.ToString();
    }
}
