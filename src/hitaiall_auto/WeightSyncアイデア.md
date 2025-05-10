## WeightSyncPro

**1. 序論**

**1.1. プロジェクト概要**
WeightSyncPro（以下、本ツール）は、Unityエディタ拡張機能であり、異なる3Dアバターモデル間で`SkinnedMeshRenderer`のボーンウェイト情報を転送するツールである。主な目的は、アバターの衣装やアクセサリーの着せ替え作業におけるリギング調整の手間を大幅に削減し、ユーザーの作業効率と成果物の品質を向上させることである。

**1.2. 設計目標**
*   **高精度なウェイト転送**: 異なるボーン構造を持つアバター間でも、自然な変形を維持できるウェイト転送を実現する。
*   **ユーザーフレンドリーな操作性**: 3Dモデリングやリギングの専門知識が少ないユーザーでも直感的に操作でき、期待する結果を得られるUI/UXを提供する。
*   **堅牢性と安定性**: 様々なアバター構成やエッジケースに対応し、エラー発生時にも適切なフィードバックと対処法を提示する。
*   **拡張性**: 将来的な機能追加や改善が容易なモジュール構造とする。

**1.3. 対象読者**
本ツールの開発を担当するプログラマーおよびQAエンジニア。

**1.4. 参考文献**
*   Unity Scripting APIドキュメント (特に`SkinnedMeshRenderer`, `Mesh`, `Transform`, `Animator`, `EditorWindow`, `EditorGUILayout`関連)

**2. システムアーキテクチャ**

**2.1. 概要**
本ツールはUnityエディタ上で動作する。主要コンポーネントは以下の通り。
*   **UIモジュール (`WeightSyncProWindow`)**: ユーザーインターフェースの描画とユーザー入力の受付。
*   **コアロジックモジュール (`WeightTransferCore`)**: ウェイト転送処理の本体。
*   **データ管理モジュール (`TransferOptions`, `DiagnosticInfo`)**: 処理オプションや診断情報を管理。
*   **ユーティリティモジュール**: ボーン操作、メッシュ操作、ファイル操作などの補助機能。

**2.2. 配置**
全てのスクリプトはUnityプロジェクトの`Assets/Editor`フォルダ、またはそのサブフォルダ内に配置する[6]。これにより、エディタ拡張として正しく機能し、ランタイムビルドに含まれないようにする。

**3. UIモジュール (`WeightSyncProWindow` クラス)**

**3.1. ウィンドウ定義**
*   クラス名: `WeightSyncProWindow`
*   継承: `UnityEditor.EditorWindow`[6]
*   メニューパス: "Window/WeightSyncPro
*   ウィンドウタイトル: "WeightSyncPro"

**3.2. 表示要素とレイアウト**
`OnGUI()`メソッド内に`EditorGUILayout`および`GUILayout`を使用して実装[6]。画像[2]のUIを参考にしつつ、以下のセクションで構成する。

**3.2.1. アバター設定セクション**
*   **ソースアバター (Source Avatar)**:
    *   UIコントロール: `EditorGUILayout.ObjectField` (タイプ: `GameObject`)
    *   説明: ウェイト情報の提供元となるアバターのルートGameObject。
*   **ターゲットアバター (Target Avatar)**:
    *   UIコントロール: `EditorGUILayout.ObjectField` (タイプ: `GameObject`)
    *   説明: ウェイト情報を適用する対象の衣装やアクセサリーを含むアバターのルートGameObject。
*   **ソースレンダラー (Source Renderer)**:
    *   UIコントロール: `EditorGUILayout.ObjectField` (タイプ: `SkinnedMeshRenderer`)
    *   説明: ソースアバター内のウェイト情報を持つ`SkinnedMeshRenderer`。
*   **ターゲットレンダラー (Target Renderer)**:
    *   UIコントロール: `EditorGUILayout.ObjectField` (タイプ: `SkinnedMeshRenderer`)
    *   説明: ターゲットアバター内のウェイトを適用する対象の`SkinnedMeshRenderer`。

**3.2.2. 基本オプションセクション**
*   **ヒューマノイドボーン自動検出 (Auto Detect Humanoid Bones)**:
    *   UIコントロール: `EditorGUILayout.Toggle`
    *   デフォルト値: `true`
    *   説明: Unityのヒューマノイドリグ設定 (`Animator.GetBoneTransform(HumanBodyBones)`) を利用してボーンを自動マッピングするかどうか。
*   **バックアップ作成 (Create Backup)**:
    *   UIコントロール: `EditorGUILayout.Toggle`
    *   デフォルト値: `true`
    *   説明: 処理前にターゲットアバターのプレハブバックアップを作成するかどうか。
*   **ターゲットマテリアル維持 (Keep Target Materials)**:
    *   UIコントロール: `EditorGUILayout.Toggle`
    *   デフォルト値: `true`
    *   説明: 処理後もターゲットレンダラーの既存マテリアルを維持するかどうか。

**3.2.3. ウェイト転送パラメータセクション (Surface Mode固定)**
*   **検索半径 (Search Radius)**:
    *   UIコントロール: `EditorGUILayout.Slider`
    *   範囲: 0.001f ～ 0.5f (推奨。状況に応じて調整)
    *   デフォルト値: 0.05f
    *   説明: ターゲットメッシュの各頂点から、ウェイト情報をサンプリングするソースメッシュ上の近傍頂点を検索する半径。
*   **最大ボーン影響数 (Max Bone Influences)**:
    *   UIコントロール: `EditorGUILayout.IntSlider`
    *   範囲: 1 ～ 8 (UnityのSkinnedMeshRendererは通常4ボーン、設定により最大32だが、UIでは実用的な範囲に)
    *   デフォルト値: 4
    *   説明: 1つの頂点に影響を与える最大ボーン数。

**3.2.4. アクションボタンセクション**
*   **ウェイト転送 (Transfer Weights)**:
    *   UIコントロール: `GUILayout.Button`
    *   アクション: `WeightTransferCore.TransferWeights()`メソッドを実行。

**3.2.5. 診断情報表示エリア (新規追加または強化)**
*   UIコントロール: `EditorGUILayout.HelpBox` やカスタム描画
*   表示内容: `DiagnosticInfo`クラスから取得した情報（スケール警告、ボーンマッピング状況、フォールバック使用回数、エラーメッセージ、推奨アクションなど）をリアルタイムまたは処理後に表示。
*   レベル別表示: 情報、警告、エラーをアイコンや色で区別。

**3.2.6. ボーンマッピング編集UI (新規追加 - 詳細別途)**
*   表示エリア: メインウィンドウ内、またはサブウィンドウとして開く。
*   機能: ソースボーンとターゲットボーンの対応関係をリスト表示し、ユーザーが手動で編集・確認できる機能。
*   UIコントロール: `UnityEditorInternal.ReorderableList` やカスタムリスト描画、`EditorGUILayout.Popup`などを検討。

**3.3. 入力検証 (`ValidateInputs` メソッド)**
*   ソースアバター、ターゲットアバター、ソースレンダラー、ターゲットレンダラーが全てアサインされているかチェック。
*   各レンダラーに`sharedMesh`が存在するかチェック。
*   未設定の場合は`EditorUtility.DisplayDialog`でエラーメッセージを表示。

**4. コアロジックモジュール (`WeightTransferCore` クラス)**

**4.1. メイン処理フロー (`TransferWeights` メソッド)**
入力: `GameObject sourceRoot`, `GameObject targetRoot`, `SkinnedMeshRenderer sourceRenderer`, `SkinnedMeshRenderer targetRenderer`, `TransferOptions options`, `DiagnosticInfo diagnostics` (参照渡し)

1.  **スケール自動補正 (新規実装)**:
    1.  `sourceRoot.transform.localScale` と `targetRoot.transform.localScale` をローカル変数に保存。
    2.  `sourceRoot.transform.localScale = Vector3.one;`
    3.  `targetRoot.transform.localScale = Vector3.one;`
    4.  `diagnostics`にスケール変更が行われた旨を記録。
2.  **バックアップ作成 (オプション)**:
    *   `options.createBackup`がtrueの場合、`BackupTarget(targetRoot, originalTargetScale)`を実行。`originalTargetScale`は補正前のスケール。
3.  **ボーン情報収集**:
    1.  `sourceBoneMap = GetAllBones(sourceRoot);`
    2.  `targetBoneMap = GetAllBones(targetRoot);`
    3.  `options.autoDetectHumanoid`がtrueの場合:
        *   `WeightSyncProWindow.GetHumanoidBones(sourceRoot)` および `targetRoot` でヒューマノイドボーンを取得。
        *   成功すればそれらを `sourceBoneMap`, `targetBoneMap` として優先使用。
        *   失敗時 (診断ログ[3]の`Could not auto-detect Humanoid bones...`に該当) は`diagnostics`に警告を記録し、`GetAllBones`の結果を使用。
4.  **レンダラーペア処理呼び出し**:
    *   `ProcessRendererPair(sourceRenderer, targetRenderer, sourceBoneMap, targetBoneMap, options, sourceRoot.transform, targetRoot.transform, diagnostics)` を実行。
5.  **スケール復元 (try-finallyブロック内で保証)**:
    1.  `sourceRoot.transform.localScale = originalSourceScale;`
    2.  `targetRoot.transform.localScale = originalTargetScale;`
    3.  `diagnostics`にスケール復元が行われた旨を記録。
6.  **診断結果表示トリガー**: UIモジュールに通知 (または`diagnostics.LogDiagnostics()`をここで呼ぶ)。

**4.2. レンダラーペア処理 (`ProcessRendererPair` メソッド)**
入力: `SkinnedMeshRenderer sourceRend`, `SkinnedMeshRenderer targetRend`, `Dictionary srcBoneMap`, `Dictionary tgtBoneMap`, `TransferOptions opts`, `Transform srcRoot`, `Transform tgtRoot`, `DiagnosticInfo diags`

1.  **メッシュデータ取得**:
    *   `srcMesh = sourceRend.sharedMesh;`
    *   `tgtMesh = targetRend.sharedMesh;`
    *   nullチェックとエラー記録 (`diags`)。
    *   `srcVertices = srcMesh.vertices;`
    *   `tgtVertices = tgtMesh.vertices;`
    *   `srcBoneWeights = srcMesh.boneWeights;`
    *   `srcBones = sourceRend.bones;` (nullまたは空の場合はエラー記録)
2.  **ボーンマッピング実行**:
    *   `newBones = MapBones(srcBones, srcBoneMap, tgtBoneMap, tgtRoot, diags, srcRoot);`
3.  **ウェイト計算ループ**:
    *   `newBoneWeights = new BoneWeight[tgtVertices.Length];`
    *   `fallbackCount = 0;`
    *   各`targetVertices[i]`に対して:
        1.  `tgtVertexWorldPos = targetRend.transform.TransformPoint(tgtVertices[i]);`
        2.  `bw = CalculateSurfaceWeights(tgtVertexWorldPos, sourceRend, srcVertices, srcBoneWeights, opts.searchRadius, opts.maxBoneInfluence, ref fallbackCount, diags);` (diagsを渡して詳細な問題を記録可能に)
        3.  `newBoneWeights[i] = bw;`
        4.  `NormalizeBoneWeight(ref newBoneWeights[i], opts.maxBoneInfluence, diags);` (正規化処理もdiagsに情報を記録)
    *   `diags.fallbackUsedInSurfaceModeCount += fallbackCount;`
4.  **ターゲットメッシュ更新**:
    1.  `newMesh = Object.Instantiate(tgtMesh);`
    2.  `newMesh.name = tgtMesh.name + "_Weighted";`
    3.  **ウェイトインデックスの再マッピング (重要・修正)**:
        *   `CalculateSurfaceWeights`が返す`BoneWeight`の`boneIndexN`は、`srcBones`配列のインデックスを指す。これを`newBones`配列のインデックスに変換する必要がある。
        *   `sourceBoneToNewBoneIndexMap = CreateSourceToNewBoneIndexMap(srcBones, newBones, diags);`
        *   `reIndexedBoneWeights = ReIndexBoneWeights(newBoneWeights, sourceBoneToNewBoneIndexMap, opts.maxBoneInfluence, diags);`
        *   `newMesh.boneWeights = reIndexedBoneWeights;`
    4.  `newMesh.bindposes = GenerateBindPoses(newBones, targetRend.transform, diags);`
    5.  `opts.keepMaterials`がtrueなら `targetRend.sharedMaterials = targetRend.sharedMaterials;` (実質的には何もしないが、明示的に設定を維持する意図)
    6.  `targetRend.sharedMesh = newMesh;`
    7.  `targetRend.bones = newBones;`
    8.  `targetRend.rootBone = DetermineNewRootBone(newBones, tgtRoot, diags);` (より堅牢なルートボーン決定ロジック)
    9.  `targetRend.ResetBounds();`
5.  **ダーティフラグ設定**:
    *   `EditorUtility.SetDirty(targetRend);`
    *   `EditorUtility.SetDirty(newMesh);`
6.  完了ログ: `Debug.Log(...)`

**4.3. ボーンマッピング (`MapBones` メソッド)**
入力: `Transform[] srcRendererBones`, `Dictionary fullSrcBoneMap`, `Dictionary fullTgtBoneMap`, `Transform tgtRoot`, `DiagnosticInfo diags`, `Transform srcRoot`

1.  `newTargetBonesList = new List();`
2.  各`srcBone` in `srcRendererBones`について:
    1.  `targetMappedBone = FindTargetBone(srcBone, fullSrcBoneMap, fullTgtBoneMap, tgtRoot, srcRoot, diags);`
    2.  `targetMappedBone`がnullでなく、`newTargetBonesList`に未追加なら追加。
    3.  nullの場合、`diags.nullBonesInMappedBonesCount++`し、`diags.AddWarning(...)`でマッピング失敗ボーン名を記録 (診断ログ[3]の`Source bone ... could not be mapped`に対応)。
3.  `newTargetBonesList`が空の場合のフォールバック処理を強化（例: `tgtRoot`のAnimatorの必須ボーンを優先的に追加）。
4.  `return newTargetBonesList.ToArray();`

**4.4. ボーン検索 (`FindTargetBone` メソッド)**
入力: `Transform srcBone`, `Dictionary srcBonePathMap`, `Dictionary tgtBonePathMap`, `Transform tgtRoot`, `Transform srcRoot`, `DiagnosticInfo diags`
1.  `srcBonePath = GetBonePath(srcBone, srcRoot);`
2.  まず、`tgtBonePathMap`で`srcBonePath`をキーに検索。あればそれを返す。
3.  次に、`srcBone.name`をキーに`tgtBonePathMap`を検索（キーがパスなので、値の`name`プロパティとの比較）。
    *   **改善案**: 曖昧検索ロジック（大文字小文字無視、接頭辞・接尾辞の一致、一般的な命名規則のバリエーション対応 L/R, Left/Rightなど）を導入。
4.  見つからなければnullを返し、`diags`に記録。

**4.5. ボーンパス取得 (`GetBonePath` メソッド)**
入力: `Transform bone`, `Transform root`
*   `bone`から`root`までの相対パス文字列を生成 (例: "Hips/Spine/Chest")。
*   `root`自身の場合は`bone.name`を返す。
*   `bone`が`root`の子孫でない場合は、適切な警告/エラー処理。

**4.6. ウェイト計算 (`CalculateSurfaceWeights` メソッド)**
入力: `Vector3 tgtVertexWorldPos`, `SkinnedMeshRenderer srcRend`, `Vector3[] srcVerts`, `BoneWeight[] srcBWs`, `float searchRad`, `int maxInf`, `ref int fallbackCnt`, `DiagnosticInfo diags`
1.  `potentialWeights = new List>();`
2.  各`srcVerts[j]`について:
    1.  `srcVertexWorldPos = srcRend.transform.TransformPoint(srcVerts[j]);`
    2.  `distSq = (tgtVertexWorldPos - srcVertexWorldPos).sqrMagnitude;`
    3.  `distSq  summedWeights`)を計算。
5.  `summedWeights`を影響度順にソートし、`maxInf`個まで選択。
6.  選択された影響から`BoneWeight`構造体を生成して返す (正規化は呼び出し元)。

**4.7. バインドポーズ生成 (`GenerateBindPoses` メソッド)**
入力: `Transform[] bones`, `Transform meshTransform`, `DiagnosticInfo diags`
1.  `bindPoses = new Matrix4x4[bones.Length];`
2.  各`bones[i]`について:
    1.  `bones[i]`がnullなら `bindPoses[i] = Matrix4x4.identity;` し、`diags.nullBonesInBindPoseCount++;` (診断ログ[3]の`Null bone found at index ...`に対応)。
    2.  nullでなければ `bindPoses[i] = bones[i].worldToLocalMatrix * meshTransform.localToWorldMatrix;`

**4.8. その他ヘルパーメソッド**
*   `GetAllBones(GameObject avatar)`: アバター内の全Transformをパス文字列をキーとして辞書に格納。
*   `NormalizeBoneWeight(ref BoneWeight bw, int maxInfluences, DiagnosticInfo diags)`: ウェイト合計が1になるように正規化し、影響ボーン数を`maxInfluences`に制限。
*   `CreateSourceToNewBoneIndexMap(...)`, `ReIndexBoneWeights(...)`, `DetermineNewRootBone(...)` (新規作成)

**5. データ管理モジュール**

**5.1. `TransferOptions` クラス**
`WeightSyncProWindow.TransferOptions`[1]をベースに、UIモジュールとコアロジックモジュール間で設定値を渡すためのデータ構造。
*   `bool autoDetectHumanoid`
*   `bool createBackup`
*   `bool keepMaterials`
*   `float searchRadius`
*   `int maxBoneInfluence`

**5.2. `DiagnosticInfo` クラス**
処理中の警告、エラー、統計情報を記録するためのデータ構造。
*   `List warnings`
*   `List errors`
*   `int fallbackUsedInSurfaceModeCount`
*   `int nullBonesInBindPoseCount`
*   `int nullBonesInMappedBonesCount`
*   メソッド: `AddWarning(string)`, `AddError(string)`, `LogDiagnostics()` (コンソールへの出力)
