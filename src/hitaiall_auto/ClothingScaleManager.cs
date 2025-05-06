using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic; // ← Dictionaryを使うために必要
using System.Text; // ← StringBuilderを使うために必要

// スケールデータを保存するためのクラス構造
[System.Serializable] // ← JsonUtilityで扱うために必要
public class ClothingScaleData
{
    public string clothingName;
    public string targetCharacter;
    public float scaleValue;
}

// スケールデータベース全体を保存するためのクラス構造
[System.Serializable] // ← JsonUtilityで扱うために必要
public class CharacterScaleDatabase
{
    // ★修正ポイント1: Listの中身の型を指定する
    public List<ClothingScaleData> scaleEntries = new List<ClothingScaleData>();
}


// メインの管理ツールウィンドウ
public class ClothingScaleManager : EditorWindow // ← EditorWindowを継承する
{
    // タブ切り替え用の変数
    private enum Tab { Extract, Apply, Generate }
    private Tab currentTab = Tab.Extract;

    // スケール抽出タブ用の変数
    private string clothingName = "";
    private string targetCharacter = "";

    // スケールデータ全体を保持する変数
    private CharacterScaleDatabase scaleDatabase = null;
    // JSONファイルの保存場所
    private string databasePath = "Assets/ScaleData/scale_database.json";
    // 自動生成するメニュースクリプトの保存場所
    private string scriptOutputPath = "Assets/Editor/ClothScaleAdjuster.cs";

    // スケール適用タブ用のスクロール位置
    private Vector2 scrollPosition;
    // スケール適用タブ用の折りたたみ状態を保持する変数
    // ★修正ポイント2: Dictionaryの中身の型を指定する
    private Dictionary<string, bool> foldouts = new Dictionary<string, bool>();

    // "Tools/Clothing Scale Manager" メニューからウィンドウを開くためのメソッド
    [MenuItem("Tools/Clothing Scale Manager")]
    public static void ShowWindow()
    {
        // ★修正ポイント3: GetWindowにウィンドウの型を指定する
        GetWindow<ClothingScaleManager>("衣装スケール管理");
    }

    // ウィンドウが開かれた時や、スクリプトが再読み込みされた時に呼ばれるメソッド
    void OnEnable()
    {
        LoadScaleDatabase(); // 最初にデータベースを読み込む
    }

    // ウィンドウの中身を描画するメソッド (毎フレーム呼ばれる)
    void OnGUI()
    {
        GUILayout.Space(10); // 上に少しスペースを空ける
        // タブ切り替えボタンを表示
        EditorGUILayout.BeginHorizontal(); // 横並び開始
        if (GUILayout.Toggle(currentTab == Tab.Extract, "スケール抽出", EditorStyles.toolbarButton))
            currentTab = Tab.Extract;
        if (GUILayout.Toggle(currentTab == Tab.Apply, "スケール適用", EditorStyles.toolbarButton))
            currentTab = Tab.Apply;
        if (GUILayout.Toggle(currentTab == Tab.Generate, "メニュー生成", EditorStyles.toolbarButton))
            currentTab = Tab.Generate;
        EditorGUILayout.EndHorizontal(); // 横並び終了
        GUILayout.Space(10); // タブの下に少しスペースを空ける

        // 現在選択されているタブに応じて、表示する内容を切り替える
        switch (currentTab)
        {
            case Tab.Extract:
                DrawExtractTab(); // スケール抽出タブを描画
                break;
            case Tab.Apply:
                DrawApplyTab(); // スケール適用タブを描画
                break;
            case Tab.Generate:
                DrawGenerateTab(); // メニュー生成タブを描画
                break;
        }
    }

    // 「スケール抽出」タブの中身を描画するメソッド
    void DrawExtractTab()
    {
        GUILayout.Label("キャラクター衣装スケール抽出", EditorStyles.boldLabel); // 太字ラベル

        clothingName = EditorGUILayout.TextField("衣装名", clothingName); // 衣装名入力欄
        targetCharacter = EditorGUILayout.TextField("着せるキャラクター", targetCharacter); // キャラクター名入力欄

        GUILayout.Space(10);
        EditorGUILayout.HelpBox("1. スケールを抽出したい衣装オブジェクトを選択\n2. 衣装名とキャラクター名を入力\n3. 「スケール抽出」ボタンをクリック", MessageType.Info); // 使い方のヘルプ表示
        GUILayout.Space(10);

        // ボタンが押された時の処理
        if (GUILayout.Button("現在の選択オブジェクトのスケールを抽出", GUILayout.Height(30)))
        {
            SaveCurrentScale(); // スケール保存処理を呼ぶ
        }

        GUILayout.Space(20); // ボタン間にスペース

        // ボタンが押された時の処理
        if (GUILayout.Button("スケールデータベースを保存 (手動)", GUILayout.Height(30)))
        {
            SaveScaleDatabase(); // データベース保存処理を呼ぶ
        }
    }

    // 「スケール適用」タブの中身を描画するメソッド
    void DrawApplyTab()
    {
        GUILayout.Label("キャラクター衣装スケール適用", EditorStyles.boldLabel);

        // データベースが空の場合の表示
        if (scaleDatabase == null || scaleDatabase.scaleEntries.Count == 0)
        {
            EditorGUILayout.HelpBox("スケールデータが存在しません。「スケール抽出」タブでデータを追加してください。", MessageType.Info);
            if (GUILayout.Button("データベースの再読込"))
            {
                LoadScaleDatabase(); // 再読み込みボタン
            }
            return; // ここで処理を終了
        }

        EditorGUILayout.HelpBox("1. スケールを適用したい衣装オブジェクトを選択\n2. 目的のキャラクター「に着せる」ボタンをクリック", MessageType.Info);
        GUILayout.Space(10);

        // 衣装ごとにデータをグループ化するための準備
        // ★修正ポイント4: Dictionaryの中身の型を指定する
        Dictionary<string, List<ClothingScaleData>> groupedEntries = new Dictionary<string, List<ClothingScaleData>>();

        // データベースの全エントリをループしてグループ化
        foreach (var entry in scaleDatabase.scaleEntries)
        {
            if (!groupedEntries.ContainsKey(entry.clothingName))
            {
                // ★修正ポイント5: Listの中身の型を指定する
                groupedEntries[entry.clothingName] = new List<ClothingScaleData>(); // 新しい衣装名のリストを作成
            }
            groupedEntries[entry.clothingName].Add(entry); // リストにエントリを追加
        }

        // スクロール可能な領域を開始
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        // グループ化された衣装ごとにループしてUIを表示
        foreach (var clothingGroup in groupedEntries)
        {
            string clothingName = clothingGroup.Key; // 衣装名を取得

            // 折りたたみ状態を管理 (なければfalseで初期化)
            if (!foldouts.ContainsKey(clothingName))
            {
                foldouts[clothingName] = false;
            }

            // 折りたたみ(Foldout)UIを表示
            foldouts[clothingName] = EditorGUILayout.Foldout(foldouts[clothingName], clothingName, true);

            // もし折りたたみが開かれていたら、中身を表示
            if (foldouts[clothingName])
            {
                EditorGUI.indentLevel++; // インデントを一段深くする

                // その衣装に含まれるキャラクターごとのボタンをループ表示
                foreach (var entry in clothingGroup.Value)
                {
                    // ボタンが押された時の処理
                    if (GUILayout.Button($"{entry.targetCharacter}に着せる (Scale: {entry.scaleValue})"))
                    {
                        ApplyScale(entry.scaleValue); // スケール適用処理を呼ぶ
                    }
                }

                EditorGUI.indentLevel--; // インデントを元に戻す
            }
        }

        EditorGUILayout.EndScrollView(); // スクロール領域を終了
    }

    // 「メニュー生成」タブの中身を描画するメソッド
    void DrawGenerateTab()
    {
        GUILayout.Label("スケールメニュー生成", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("「GameObject」メニューに「ClothScaleAdjuster」サブメニューを生成します。\n新しいスケールを追加・変更した場合は、再度このボタンを押してUnityエディタを再起動してください。", MessageType.Info);
        GUILayout.Space(10);

        // ボタンが押された時の処理
        if (GUILayout.Button("メニュースクリプトを生成", GUILayout.Height(30)))
        {
            GenerateMenuScript(); // メニュー生成処理を呼ぶ
        }
    }

    // JSONファイルからスケールデータベースを読み込むメソッド
    private void LoadScaleDatabase()
    {
        if (File.Exists(databasePath)) // ファイルが存在するか確認
        {
            string json = File.ReadAllText(databasePath); // ファイルの内容を読み込む
            // ★修正ポイント6: JsonUtility.FromJsonに読み込む型を指定する
            scaleDatabase = JsonUtility.FromJson<CharacterScaleDatabase>(json); // JSONをオブジェクトに変換
        }

        // もしファイルが存在しない、または読み込みに失敗した場合
        if (scaleDatabase == null)
        {
            // ★修正ポイント7: new で作る型を指定する
            scaleDatabase = new CharacterScaleDatabase(); // 新しい空のデータベースを作成
        }
    }

    // 現在選択されているオブジェクトのスケールをデータベースに保存するメソッド
    private void SaveCurrentScale()
    {
        // オブジェクトが選択されているか確認
        if (Selection.activeTransform == null)
        {
            EditorUtility.DisplayDialog("エラー", "オブジェクトを選択してください", "OK");
            return;
        }
        // 衣装名とキャラクター名が入力されているか確認
        if (string.IsNullOrEmpty(clothingName) || string.IsNullOrEmpty(targetCharacter))
        {
            EditorUtility.DisplayDialog("エラー", "衣装名とキャラクター名を入力してください", "OK");
            return;
        }

        // 選択オブジェクトのX軸スケールを取得 (XYZ同じ前提)
        float scaleValue = Selection.activeTransform.localScale.x;

        // 新しいデータエントリを作成
        ClothingScaleData newEntry = new ClothingScaleData
        {
            clothingName = clothingName,
            targetCharacter = targetCharacter,
            scaleValue = scaleValue
        };

        // データベースがまだなければ作成
        if (scaleDatabase == null)
        {
            scaleDatabase = new CharacterScaleDatabase();
        }
        // scaleEntriesリストがまだなければ作成
        if (scaleDatabase.scaleEntries == null)
        {
             scaleDatabase.scaleEntries = new List<ClothingScaleData>();
        }


        // 同じ衣装名・キャラクター名の組み合わせが既に存在するかチェック
        bool entryUpdated = false;
        for (int i = 0; i < scaleDatabase.scaleEntries.Count; i++)
        {
            if (scaleDatabase.scaleEntries[i].clothingName == clothingName &&
                scaleDatabase.scaleEntries[i].targetCharacter == targetCharacter)
            {
                scaleDatabase.scaleEntries[i] = newEntry; // 既存のエントリを上書き
                entryUpdated = true;
                break;
            }
        }

        // もし既存のエントリがなければ、新しいエントリを追加
        if (!entryUpdated)
        {
            scaleDatabase.scaleEntries.Add(newEntry);
        }

        // ★重要: データを追加・更新したらすぐにファイルに保存する
        SaveScaleDatabase();

        EditorUtility.DisplayDialog("完了",
            $"スケール値 {scaleValue} を保存しました：\n衣装：{clothingName}\nキャラクター：{targetCharacter}", "OK");

        // 抽出後に入力欄をクリアする (任意)
        // clothingName = "";
        // targetCharacter = "";
    }

    // スケールデータベースをJSONファイルに保存するメソッド
    private void SaveScaleDatabase()
    {
        // データベースがなければ何もしない
        if (scaleDatabase == null) return;

        // 保存先ディレクトリが存在するか確認、なければ作成
        string directoryPath = Path.GetDirectoryName(databasePath);
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        // オブジェクトをJSON文字列に変換 (trueで整形して保存)
        string json = JsonUtility.ToJson(scaleDatabase, true);
        // ファイルに書き込み
        File.WriteAllText(databasePath, json);
        // Unityエディタにファイルの変更を通知
        AssetDatabase.Refresh();

        // EditorUtility.DisplayDialog("完了", "スケールデータベースを保存しました", "OK"); // SaveCurrentScale内で呼ぶのでコメントアウト
    }

    // 選択されているオブジェクトにスケールを適用するメソッド
    private void ApplyScale(float scaleValue)
    {
        // オブジェクトが選択されているか確認
        if (Selection.activeTransform == null)
        {
            EditorUtility.DisplayDialog("エラー", "スケールを適用するオブジェクトを選択してください", "OK");
            return;
        }

        // Undo(元に戻す)機能を有効にするための記録
        Undo.RecordObject(Selection.activeTransform, "Apply Scale");
        // スケールを適用 (XYZすべてに同じ値を設定)
        Selection.activeTransform.localScale = Vector3.one * scaleValue;
        // 変更をエディタに反映させる (任意だが推奨)
        EditorUtility.SetDirty(Selection.activeTransform);
    }

    // GameObjectメニューにスケール適用メニューを自動生成するメソッド
    private void GenerateMenuScript()
    {
        // データベースが空なら何もしない
        if (scaleDatabase == null || scaleDatabase.scaleEntries.Count == 0)
        {
            EditorUtility.DisplayDialog("エラー", "スケールデータが存在しません。「スケール抽出」タブでデータを追加してください。", "OK");
            return;
        }

        // スクリプトコードを生成するためのStringBuilder
        StringBuilder sb = new StringBuilder();

        // スクリプトの最初のお決まりの部分
        sb.AppendLine("using UnityEngine;");
        sb.AppendLine("using UnityEditor;");
        sb.AppendLine("");
        sb.AppendLine("// このスクリプトは ClothingScaleManager によって自動生成されました。直接編集しないでください。");
        sb.AppendLine("public class ClothScaleAdjuster");
        sb.AppendLine("{");

        // 衣装ごとにグループ化するための準備
        // ★修正ポイント8: Dictionaryの中身の型を指定する
        Dictionary<string, List<ClothingScaleData>> groupedEntries = new Dictionary<string, List<ClothingScaleData>>();

        // データベースの全エントリをループしてグループ化
        foreach (var entry in scaleDatabase.scaleEntries)
        {
            if (!groupedEntries.ContainsKey(entry.clothingName))
            {
                // ★修正ポイント9: Listの中身の型を指定する
                groupedEntries[entry.clothingName] = new List<ClothingScaleData>();
            }
            groupedEntries[entry.clothingName].Add(entry);
        }

        int menuOrder = 50; // メニューの表示順序の初期値

        // グループ化された衣装ごとにループしてメソッドを生成
        foreach (var clothingGroup in groupedEntries)
        {
            string clothingName = clothingGroup.Key;

            // 分かりやすいようにコメントを追加
            sb.AppendLine($"    // --- {clothingName} ---");

            // その衣装に含まれるキャラクターごとのメソッドを生成
            foreach (var entry in clothingGroup.Value)
            {
                // メソッド名を生成 (例: AdjustTestDressToTestAvatar)
                string methodName = $"Adjust{CleanName(clothingName)}To{CleanName(entry.targetCharacter)}";

                // MenuItem属性を追加 (これがメニュー項目になる)
                // エスケープ文字(\")が必要なことに注意
                sb.AppendLine($"    [MenuItem(\"GameObject/ClothScaleAdjuster/{CleanNameForMenu(clothingName)}/{CleanNameForMenu(entry.targetCharacter)}に着せる\", false, {menuOrder})]");
                // メソッド定義を開始
                sb.AppendLine($"    public static void {methodName}()");
                sb.AppendLine("    {");
                // スケールを適用するコード
                sb.AppendLine($"        if (Selection.activeTransform != null)"); // 選択オブジェクトがあるか確認
                sb.AppendLine($"        {{");
                sb.AppendLine($"            Undo.RecordObject(Selection.activeTransform, \"Apply Scale via Menu\");");
                sb.AppendLine($"            Selection.activeTransform.localScale = Vector3.one * {entry.scaleValue}f;");
                sb.AppendLine($"            EditorUtility.SetDirty(Selection.activeTransform);");
                sb.AppendLine($"        }}");
                sb.AppendLine("    }");
                sb.AppendLine(""); // メソッド間に空行を入れる

                menuOrder++; // 次のメニュー項目の順序を増やす
            }
        }

        // メニュー全体を有効/無効にするためのバリデーションメソッド
        sb.AppendLine("    // --- Validation ---");
        sb.AppendLine("    [MenuItem(\"GameObject/ClothScaleAdjuster\", true)] // メニュー階層のルートに対するバリデーション");
        sb.AppendLine("    public static bool ValidateSelection()");
        sb.AppendLine("    {");
        sb.AppendLine("        // オブジェクトが選択されている場合のみメニューを有効にする");
        sb.AppendLine("        return Selection.activeTransform != null;");
        sb.AppendLine("    }");

        // スクリプトの終了
        sb.AppendLine("}");

        // 生成したスクリプトコードをファイルに書き込む
        try
        {
            // 保存先ディレクトリが存在するか確認、なければ作成
            string directory = Path.GetDirectoryName(scriptOutputPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            // ファイルに書き込み
            File.WriteAllText(scriptOutputPath, sb.ToString());
            // Unityエディタにファイルの変更を通知
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("完了", "スケールメニューを生成しました。\nUnityエディタを再起動するか、スクリプトの再コンパイル後にメニューが更新されます。", "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"メニュースクリプトの生成に失敗しました: {e.Message}");
            EditorUtility.DisplayDialog("エラー", $"メニュースクリプトの生成に失敗しました。\n詳細はConsoleを確認してください。", "OK");
        }
    }

    // C#のメソッド名として使えるように、名前から不要な文字を削除・変換するヘルパーメソッド
    private string CleanName(string input)
    {
        StringBuilder sb = new StringBuilder();
        bool nextUpper = true; // 次の文字を大文字にするかどうかのフラグ

        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c)) // 英数字の場合
            {
                sb.Append(nextUpper ? char.ToUpper(c) : c); // フラグに応じて大文字/小文字で追加
                nextUpper = false; // 次の文字は小文字にする
            }
            else // 英数字以外（記号や空白など）の場合
            {
                nextUpper = true; // 次の英数字は大文字にする（単語の区切りとみなす）
            }
        }
        // もし名前が空になったらデフォルト名を返す
        return sb.Length > 0 ? sb.ToString() : "DefaultName";
    }

     // Unityのメニューパスとして使えるように、スラッシュ('/')を全角スラッシュなどに置換するヘルパーメソッド
    private string CleanNameForMenu(string input)
    {
        // メニューパスで '/' は階層区切りとして解釈されるため、別の文字に置き換える
        return input.Replace("/", "／"); // 例として全角スラッシュに置換
    }
}
