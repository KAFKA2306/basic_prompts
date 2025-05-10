# WeightSyncPro 全体設計 (機能強化案統合版)

## 1. 目的・コンセプト

- **目的**: Unityエディタ上で、アバターのボーンウェイトを衣装に高精度かつ効率的に転送し、最適化された結果を得る。
    
- **コンセプト**: **ウェイト品質の最適化**と**ユーザーの作業効率向上**に焦点を当てた、シンプルかつ強力なウェイト転送ツール。
    

## 2. モジュール構成と主要クラス案

1. **`WeightSyncProWindow.cs` (EditorWindow)**
    
    - 責務: UI描画、ユーザー入力受付、コアモジュールへの処理委譲、結果表示（ログ、簡易診断結果）。
        
        
2. **`BoneMappingCore.cs` (コアロジッククラス - BoneSyncProから分離・拡張)**
    
    - 責務: ボーンのルート特定、ボーン名の正規化、固定エイリアス処理、**ヒューマノイドリグの標準配置ルールを活用した検索アルゴリズム改善**、階層比較、ヒューマノイドボーン優先マッピング、最終的なボーンマップの生成、**入力モデルの座標系分析とUnity座標系への調整**。
        
        
3. **`WeightTransferCore.cs` (コアロジッククラス)**
    
    - 責務: 衣装のボーン配列・バインドポーズ再構築、**効率化された最近傍頂点探索（グリッドベース空間分割）**、ウェイトデータのコピーとボーンインデックス再マッピング、**ウェイトの正規化（AutoNormalizeオプション）**、**ウェイト品質最適化（Max Bones/Vertex、Max Bone Weight閾値処理）**、**簡易部分ウェイト転送**。
        
        

## 3. 詳細機能と実装方針 (機能強化案を統合)

## 3.1. UI/UX (`WeightSyncProWindow.cs`)

- **メインUI**:
    
    - アバター `GameObject` と 衣装 `GameObject` の設定フィールド。
        
    - ボーンマッピング設定 (BoneSyncProから流用):
        
        - 除去プレフィックス/サフィックス入力。
            
    - **ウェイト品質最適化オプション**:
        
        - 「Max Bones/Vertex」設定 (ドロップダウン: 1, 2, 3, 4)[11](https://discussions.unity.com/t/will-unity-support-more-than-4-weight-bone-influences-per-vertex-at-some-point/547306)[18](https://discussions.unity.com/t/what-is-the-maximum-number-of-bone-influence-per-vertex/110102)[21](https://www.reddit.com/r/Unity3D/comments/3zn24h/limits_of_bones/)。
            
        - 「Max Bone Weight しきい値」入力 (例: 0.01、この値以下のウェイトを除去)。
            
    - **簡易部分ウェイト転送オプション**:
        
        - ドロップダウンまたはトグルボタン群で事前定義エリアを選択: 「胸周り」「股関節周り」「脚全体」「腕全体」「全身（デフォルト）」など2。
            
    - **ウェイトの正規化オプション**:
        
        - 「AutoNormalize Weights」チェックボックス（デフォルトON）3[16](https://docs.blender.org/manual/en/latest/sculpt_paint/weight_paint/tool_settings/options.html)24。
            
    - 実行ボタン: 「ボーンマッピング診断＆ウェイト転送実行」。
        
    - プログレスバー。
        
    - **処理ステータス表示改善**: 現在実行中の主要ステップ名（例: 「ボーンマッピング中...」「最近傍頂点探索中...」「ウェイト最適化中...」）をプログレスバーの近くに表示。
        
- **診断結果表示エリア**:
    
    - **簡易ウェイト診断結果**:
        
        - テキストベース（例: 「警告: ボーン 'X' の影響が頂点 Y に集中しすぎている可能性があります」）。
            
        - **シンプルな色表示**: 転送後の衣装メッシュに対し、主要ボーン（例: Hips, Spine, Chest, Shoulder, UpperLeg）のウェイト分布をSceneビューで簡易的に色分け表示するオプション（赤: 影響強すぎ/偏りすぎ、緑: 適切）。実現が難しければ、主要ボーンのウェイト合計が異常に高い頂点リスト表示など。
            
    - **インテリジェントなエラーメッセージ**:
        
        - 「ボーン 'Spine' が 'Hips' に近すぎます。アバターのボーン配置を確認してください。」などの具体的なアドバイスを表示4。
            
        - エラー/警告の重要度を視覚的なアイコン（例: ❗, ⚠️）で表現。
            
    - マッピングされなかった主要衣装ボーンのリスト。
        
- ツールチップとコンソールログ。
    

## 3.2. ボーンマッピング (`BoneMappingCore.cs`)

- `BoneSyncPro.cs` のコアロジックを流用し、以下を強化:
    
    - **ボーン検索アルゴリズム改善**4:
        
        - 正規化時に、ヒューマノイドの主要ボーン（Spine, Chest, Neck, Head, Shoulder, Arm, Leg, Footなど）は、Y値をほぼ0に揃える処理（またはY値の差が大きい場合に警告）を内部的なスコアリングに加味。
            
        - Hips、UpperLeg.R、UpperLeg.Lの3点が適切な三角形を形成しているか（極端な一直線上にないかなど）を簡易チェックし、逸脱する場合は警告またはマッピングスコアを下げる。
            
    - **座標系自動検出・調整**4:
        
        - アバターのルートトランスフォームと主要ボーン（Hips, Spine）の向きやスケールから、モデルがBlender座標系（Y-up, Z-forward）かMaya座標系（Y-up, Z-backward）かなどを簡易推定。
            
        - ウェイト転送前に衣装のルートトランスフォームをUnity標準座標系（Y-up, Z-forward）に合うように一時的に調整するオプション（または自動調整）。これは主にボーンの向きがウェイトに影響する場合に重要。実装の複雑性が高い場合は、警告と手動調整の促しに留める。
            

## 3.3. ウェイト転送 (`WeightTransferCore.cs`)

- 衣装のボーン構造とバインドポーズ再構築は従来通り。
    
- **最近傍頂点探索の効率化**:
    
    - KDTreeは実装せず、アバターのバウンディングボックスを基準とした**単純なグリッドベースの空間分割**を実装。
        
        - アバターをN x M x Lのグリッドに分割。
            
        - 各グリッドセルにアバターの頂点を格納。
            
        - 衣装の頂点を探索する際、その頂点が含まれるグリッドセルと隣接セル内のアバター頂点のみを対象に最近傍探索を行う。
            
- ウェイトコピーとボーンインデックス再マッピングは従来通り。
    
- **ウェイトの正規化**:
    
    - 「AutoNormalize Weights」オプションがONの場合、転送された各頂点のウェイトの合計が1.0になるように自動調整3[16](https://docs.blender.org/manual/en/latest/sculpt_paint/weight_paint/tool_settings/options.html)24。BlenderのAuto Normalizeと同様の挙動を目指す。
        
        - 合計が1を超えた場合は各ウェイトを合計値で割る。
            
        - 合計が1未満の場合は、最も影響の大きいボーンのウェイトを増やすか、均等に分配する（要検討）。
            
- **ウェイト品質最適化**:
    
    - **Max Bones/Vertex**:
        
        - 転送後、UIで設定された最大ボーン数（例: 4）を超えるウェイトを持つ頂点に対し、影響度の低いボーンのウェイトを0にし、残りのウェイトを正規化[11](https://discussions.unity.com/t/will-unity-support-more-than-4-weight-bone-influences-per-vertex-at-some-point/547306)[13](https://docs.unity3d.com/2020.1/Documentation/ScriptReference/BoneWeight.html)[18](https://discussions.unity.com/t/what-is-the-maximum-number-of-bone-influence-per-vertex/110102)[20](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.SetBoneWeights.html)[21](https://www.reddit.com/r/Unity3D/comments/3zn24h/limits_of_bones/)[23](https://docs.unity3d.com/ja/2021.2/ScriptReference/Mesh.SetBoneWeights.html)。
            
        - Unityの標準は4ボーンだが、`Mesh.SetBoneWeights` ( `BoneWeight1` 使用時) ではそれ以上も扱えるため、ユーザー設定を優先する。
            
    - **Max Bone Weight しきい値**:
        
        - 転送後、UIで設定されたしきい値以下のウェイトを持つボーンの影響をその頂点から除去し、残りのウェイトを正規化。これにより微小なウェイトを整理。
            
- **簡易部分ウェイト転送**2:
    
    - UIで「胸周り」「股関節周り」などのエリアが選択された場合、そのエリアに強く関連するアバターのボーン群（例: 胸周りならSpine, Chest, UpperChest, Shoulderなど）を定義しておく。
        
    - ウェイト転送の際、**アバターの最近傍頂点のウェイト情報のうち、上記で定義されたボーン群へのウェイトのみを抽出し、それを基に衣装の頂点ウェイトを再構成する**。選択エリア外のボーンへのウェイトは0にするか、非常に小さくする。
        
        - 具体的には、最近傍アバター頂点の`BoneWeight`を取得後、そのボーンインデックスが定義済みボーン群に含まれるか確認。含まれるボーンのウェイトのみを保持し、それらで再度正規化して衣装頂点に適用する。
            

## 4. 処理フロー (概要)

1. **UI**: ユーザーがアバター、衣装、各種オプション（品質最適化、部分転送エリア、正規化等）を設定。
    
2. **UI**: 「ウェイト転送実行」ボタンクリック。
    
    1. **UI**: `Undo` 登録。プログレスバーと**処理ステップ名表示開始**。
        
    2. **BoneMappingCore**: (オプション) 座標系分析・調整。ボーンマップ生成（**改善された検索アルゴリズム使用**）。
        
    3. **UI**: (オプション) マッピングと座標系の診断結果をログ/テキストエリアに表示（**インテリジェントなエラーメッセージ**含む）。
        
    4. **WeightTransferCore**: 衣装のボーン構造・バインドポーズ再構築。
        
    5. **WeightTransferCore**: ウェイトデータ転送（**効率化された最近傍探索**、**簡易部分転送ロジック適用**）。
        
    6. **WeightTransferCore**: (オプション) **ウェイト正規化**。
        
    7. **WeightTransferCore**: **ウェイト品質最適化**（Max Bones/Vertex, Max Bone Weight しきい値）。
        
    8. **UI**: 衣装の `SkinnedMeshRenderer` と `sharedMesh` を更新。
        
    9. **UI**: (オプション) **簡易ウェイト診断結果**をログ/テキストエリア/簡易色表示で提示。
        
    10. **UI**: 完了メッセージとログ表示。プログレスバー終了。
        

## 5. 推奨実装優先順位の反映

提案された優先順位に基づいて、以下の順でコア機能から実装・テストを進める。

1. **ウェイト品質最適化** (Max Bones/Vertex, Max Bone Weight閾値)
    
2. **簡易部分ウェイト転送** (定義済みエリア選択と関連ボーンウェイト抽出)
    
3. **ボーン検索アルゴリズム改善** (BoneMappingCore内のヒューマノイドルール活用)
    
4. **ウェイトの正規化** (AutoNormalizeオプション)
    
5. **簡易ウェイト診断** (テキストベースの異常検出と、可能なら簡易色表示)
    

その後、実装容易性向上案（最近傍頂点探索のグリッドベース効率化、座標系自動検出・調整）やUI/UX改善案（インテリジェントエラーメッセージ、処理ステータス改善、シンプルなプレビュー）を順次組み込んでいく。

この機能強化案を統合した設計は、「シンプルさと確実性」という当初のコンセプトを維持しつつ、ユーザーが直面する具体的な問題（パフォーマンス、特定部位の調整の煩雑さ、リギングの前提知識不足）に対応し、より実用的で高品質なツールへと進化させるものです。特に`BoneSyncPro.cs` [] の堅牢なボーンマッピング技術を基盤にこれらの機能を追加することで、大きな効果が期待できます。

---

#if UNITY_EDITOR

using UnityEngine;

using UnityEditor;

using System;

using System.Collections.Generic;

using System.Linq;

using System.Text.RegularExpressions;

using System.Text;

  

// --- Core Logic Classes ---

public static class BoneMappingCore

{

    private static readonly Dictionary<string, string> BoneAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {

        {"leftforearm", "lowerarm_l"}, {"rightforearm", "lowerarm_r"}, {"leftelbow", "lowerarm_l"}, {"rightelbow", "lowerarm_r"},

        {"hand_plate_l", "hand_l"}, {"hand_plate_r", "hand_r"}

    };

    private static readonly Dictionary<string, string> FingerAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {

        {"thumb_01_l", "thumb_proximal_l"}, {"thumb_1_l", "thumb_proximal_l"}, /* ... (指エイリアス多数) ... */ {"little_03_r", "little_distal_r"}

    };

  

    public static Transform FindRootBone(Transform current, WeightSyncProWindow win) {

        if (!current) return null;

        Animator anim = current.GetComponentInParent<Animator>();

        if (anim && anim.isHuman) {

            Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);

            if (hips) {

                Transform pRoot = hips;

                while (pRoot.parent && pRoot.parent != anim.transform.parent && pRoot.parent != anim.transform) {

                    if (pRoot.parent.name.Equals("Armature", StringComparison.OrdinalIgnoreCase)) { win.Log($"Animator Armatureルート '{pRoot.parent.name}' (Hips基準)"); return pRoot.parent; }

                    pRoot = pRoot.parent;

                }

                win.Log($"Animator Hipsルート '{hips.name}'"); return hips;

            }

        }

        SkinnedMeshRenderer smr = current.GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (smr && smr.rootBone) { win.Log($"SMR rootBone '{smr.rootBone.name}'"); return smr.rootBone; }

        var armObj = current.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Equals("Armature", StringComparison.OrdinalIgnoreCase));

        if (armObj) { win.Log($"'Armature'名でルートボーン '{armObj.name}'"); return armObj; }

        if (anim && anim.isHuman) { Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips); if (hips) return hips; }

        win.LogWarn($"明確なルートボーンが見つかりません。'{current.name}' を使用します。", current); return current;

    }

  

    public static string NormalizeBoneName(string boneName, string prefix, string suffix, WeightSyncProWindow win) {

        if (string.IsNullOrEmpty(boneName)) return "";

        string name = boneName;

        if (!string.IsNullOrEmpty(prefix) && name.ToLowerInvariant().StartsWith(prefix.ToLowerInvariant())) name = name.Substring(prefix.Length);

        if (!string.IsNullOrEmpty(suffix) && name.ToLowerInvariant().EndsWith(suffix.ToLowerInvariant())) name = name.Substring(0, name.Length - suffix.Length);

        name = name.ToLowerInvariant();

        name = Regex.Replace(name, @"^(mixamorig|armature|bip[\d_]*|character[_]?object)", "", RegexOptions.IgnoreCase).TrimStart('_', '-');

        name = Regex.Replace(name, @"\.l$", "_l").Replace("left", "_l");

        name = Regex.Replace(name, @"\.r$", "_r").Replace("right", "_r");

        if (FingerAliases.TryGetValue(name, out string fa)) name = fa; else if (BoneAliases.TryGetValue(name, out string ba)) name = ba;

        name = Regex.Replace(name, @"[\s\.\-\(\)]+", "").Replace(@"_{2,}", "_").Trim('_');

        name = Regex.Replace(name, @"_adjust$|_end$", "", RegexOptions.IgnoreCase);

        if (name.EndsWith("_l_l") || name.EndsWith("_r_r")) name = name.Substring(0, name.Length - 2);

        return name;

    }

  

    public static Dictionary<Transform, Transform> CreateBoneMap(Transform outfitRoot, Transform avatarRoot, string p, string s, WeightSyncProWindow win) {

        var map = new Dictionary<Transform, Transform>();

        if (!outfitRoot || !avatarRoot) { win.LogError("ボーンマップ作成エラー: ルートボーンがnull"); return map; }

        Animator outAnim = outfitRoot.GetComponentInParent<Animator>(), avAnim = avatarRoot.GetComponentInParent<Animator>();

        if (avAnim && avAnim.isHuman) { /* (ヒューマノイドマッピングロジック - 変更なし、文字数削減のため詳細は省略) */

            win.Log("ヒューマノイドボーンマッピング実行..."); int hmc = 0;

            foreach (HumanBodyBones hbb in Enum.GetValues(typeof(HumanBodyBones))) {

                if (hbb == HumanBodyBones.LastBone) continue; Transform avBone = avAnim.GetBoneTransform(hbb); if (!avBone) continue;

                Transform outBone = (outAnim && outAnim.isHuman) ? outAnim.GetBoneTransform(hbb) : null;

                if (!outBone) {

                    string normAvName = NormalizeBoneName(avBone.name, "", "", win), normHBBName = NormalizeBoneName(hbb.ToString(), "", "", win);

                    outBone = outfitRoot.GetComponentsInChildren<Transform>(true).Where(ob => ob != outfitRoot && !map.ContainsKey(ob))

                        .Select(ob => new { T = ob, N = NormalizeBoneName(ob.name, p, s, win) })

                        .OrderByDescending(i => (i.N == normAvName)?3:(i.N==normHBBName)?2:(i.N.Contains(normAvName)||normAvName.Contains(i.N))?1:0)

                        .ThenBy(i => Vector3.Distance(i.T.position, avBone.position)).Select(i => i.T).FirstOrDefault();

                }

                if (outBone && avBone && !map.ContainsKey(outBone) && !map.Values.Contains(avBone)) { map[outBone] = avBone; hmc++; }

            } win.Log($"ヒューマノイドマップ完了: {hmc}ペア");

        } else { win.LogWarn("アバター非ヒューマノイド: ヒューマノイドマップスキップ"); }

        win.Log("名前/階層マッピング実行..."); /* (名前/階層マッピングロジック - 変更なし、文字数削減のため詳細は省略) */

        var remOut = outfitRoot.GetComponentsInChildren<Transform>(true).Where(b => b != outfitRoot && !map.ContainsKey(b)).ToList();

        var remAv = avatarRoot.GetComponentsInChildren<Transform>(true).Where(b => b != avatarRoot && !map.Values.Contains(b)).ToList();

        var avCache = remAv.Select(ab=>new{T=ab,N=NormalizeBoneName(ab.name,"","",win),P=ab.parent,Pos=ab.position}).Where(e=>!string.IsNullOrEmpty(e.N)).ToList();

        int nmc = 0;

        foreach(var outInfo in remOut.Select(ob=>new{T=ob,N=NormalizeBoneName(ob.name,p,s,win),P=ob.parent,Pos=ob.position}).Where(e=>!string.IsNullOrEmpty(e.N))){

            Transform bestMatch = null; float highScore = -1f;

            foreach(var avInfo in avCache.Where(ac=>!map.Values.Contains(ac.T))){

                float score = 0f;

                if(outInfo.N.Equals(avInfo.N, StringComparison.OrdinalIgnoreCase)) score+=100f;

                else if(outInfo.N.Contains(avInfo.N)||avInfo.N.Contains(outInfo.N)) score+=20f;

                else if(JaroWinklerDistance(outInfo.N, avInfo.N)>0.8f) score+=15f;

                if(outInfo.P && avInfo.P){ if(map.TryGetValue(outInfo.P, out Transform mop) && mop == avInfo.P) score+=80f; else { string nop=NormalizeBoneName(outInfo.P.name,p,s,win), nap=NormalizeBoneName(avInfo.P.name,"","",win); if(!string.IsNullOrEmpty(nop)&&nop.Equals(nap,StringComparison.OrdinalIgnoreCase)) score+=40f;}}

                int od=GetDepth(outInfo.T,outfitRoot), ad=GetDepth(avInfo.T,avatarRoot); if(od!=-1&&ad!=-1) score-=Mathf.Abs(od-ad)*5f;

                score -= Vector3.Distance(outInfo.Pos, avInfo.Pos)*20f;

                if(score > highScore){highScore=score; bestMatch=avInfo.T;}

            }

            if(bestMatch && !map.ContainsKey(outInfo.T) && !map.Values.Contains(bestMatch)){ map[outInfo.T]=bestMatch; nmc++; }

        } win.Log($"名前/階層マップ完了: {nmc}ペア");

        return map;

    }

    private static int GetDepth(Transform b, Transform r) { int d = 0; Transform c = b; while (c && c != r && c.parent) { d++; c = c.parent; if (d > 50) return -1; } return (c == r || (c == null && b == r)) ? d : -1; }

    public static double JaroWinklerDistance(string s1, string s2) { /* (Jaro-Winkler実装 - 変更なし、文字数削減のため省略) */ if(s1==s2)return 1.0;if(string.IsNullOrEmpty(s1)||string.IsNullOrEmpty(s2))return 0.0;int len1=s1.Length,len2=s2.Length,matchDist=Math.Max(len1,len2)/2-1,matches=0;bool[]m1=new bool[len1],m2=new bool[len2];for(int i=0;i<len1;i++){int st=Math.Max(0,i-matchDist),end=Math.Min(i+matchDist+1,len2);for(int j=st;j<end;j++){if(m2[j]||s1[i]!=s2[j])continue;m1[i]=m2[j]=true;matches++;break;}}if(matches==0)return 0.0;int t=0,k=0;for(int i=0;i<len1;i++){if(!m1[i])continue;while(!m2[k])k++;if(s1[i]!=s2[k])t++;k++;}double jaro=((double)matches/len1+(double)matches/len2+(double)(matches-t/2)/matches)/3.0;if(jaro<0.7)return jaro;int p=0;for(int i=0;i<Math.Min(len1,len2);i++){if(s1[i]==s2[i])p++;else break;}return jaro+Math.Min(p,4)*0.1*(1-jaro); }

}

  

public static class WeightTransferCore

{

    public static void ProcessSkinnedMesh(SkinnedMeshRenderer cloth, SkinnedMeshRenderer avatar, Dictionary<Transform, Transform> boneMap, WeightSyncProWindow.TransferOptions opts, WeightSyncProWindow win) {

        if (!cloth || !avatar || boneMap == null || boneMap.Count == 0) { win.LogError("ウェイト転送エラー: 情報不足"); return; }

        if (Vector3.Distance(avatar.transform.lossyScale, cloth.transform.lossyScale) > 0.1f) win.LogWarn($"アバター/衣装スケール不一致: {avatar.transform.lossyScale} vs {cloth.transform.lossyScale}", cloth.gameObject);

        Quaternion origRot = cloth.transform.localRotation; Vector3 origPos = cloth.transform.localPosition;

        try {

            if (opts.applyRotationFix && opts.rotationFixAngle != 0) {

                Vector3 axis = (opts.rotationFixAxis == WeightSyncProWindow.RotationFixAxis.X) ? Vector3.right : (opts.rotationFixAxis == WeightSyncProWindow.RotationFixAxis.Y) ? Vector3.up : Vector3.forward;

                cloth.transform.Rotate(axis, opts.rotationFixAngle, Space.Self); win.LogWarn($"実験的回転補正適用 (ローカル軸:{opts.rotationFixAxis}, 角度:{opts.rotationFixAngle}°)");

            }

            Mesh origMesh = cloth.sharedMesh; if (!origMesh) { win.LogError("衣装メッシュ未設定"); return; }

            Mesh meshCopy = UnityEngine.Object.Instantiate(origMesh); meshCopy.name = $"{origMesh.name}_WSP_{WeightSyncProWindow.VERSION}";

            win.Log("ボーン構造再構築..."); win.UpdateProgress("ボーン構造再構築", 0.1f);

            var newBones = boneMap.Values.Distinct().Where(b => b).ToList(); List<Matrix4x4> newBinds = new List<Matrix4x4>();

            if (newBones.Count == 0) { win.LogError("有効なマップ済みボーンなし"); UnityEngine.Object.DestroyImmediate(meshCopy); return; }

            foreach (var ab in newBones) newBinds.Add(ab.worldToLocalMatrix * cloth.transform.localToWorldMatrix);

            cloth.bones = newBones.ToArray(); meshCopy.bindposes = newBinds.ToArray();

            Animator avAnim = avatar.GetComponentInParent<Animator>(); Transform rootForCloth = null;

            if (avAnim && avAnim.isHuman) { Transform hips = avAnim.GetBoneTransform(HumanBodyBones.Hips); if (hips && newBones.Contains(hips)) rootForCloth = hips; }

            if (!rootForCloth) rootForCloth = newBones.FirstOrDefault(b => b.name.ToLowerInvariant().Contains("hips")) ?? newBones.FirstOrDefault();

            if (rootForCloth) { cloth.rootBone = rootForCloth; win.Log($"衣装rootBone: {rootForCloth.name}");} else win.LogError("衣装rootBone設定失敗");

            win.Log("ウェイト転送..."); win.UpdateProgress("ウェイト転送", 0.3f);

            BoneWeight[] avBW = avatar.sharedMesh.boneWeights; Vector3[] avVL = avatar.sharedMesh.vertices; Transform[] avSMRBones = avatar.bones;

            BoneWeight[] newCBW = new BoneWeight[meshCopy.vertexCount]; Vector3[] clVL = meshCopy.vertices;

            Matrix4x4 avL2W = avatar.transform.localToWorldMatrix, clL2W = cloth.transform.localToWorldMatrix;

            SimpleGridSpatialHash grid = new SimpleGridSpatialHash(avatar.bounds.min, Mathf.Max(avatar.bounds.size.x, avatar.bounds.size.y, avatar.bounds.size.z) / opts.gridCellDivisions);

            Vector3[] avVWS = new Vector3[avVL.Length]; for(int i=0;i<avVL.Length;i++){avVWS[i]=avL2W.MultiplyPoint3x4(avVL[i]); grid.AddPoint(avVWS[i],i);}

            HashSet<int> partialIdx = (opts.partialTransferArea != WeightSyncProWindow.PartialTransferArea.全身) ? GetPartialTransferBoneIndices(opts.partialTransferArea, avAnim, newBones.ToArray(), win) : null;

            for(int i=0; i<meshCopy.vertexCount; i++){

                if(i%1000==0) win.UpdateProgress($"ウェイト転送中 ({i}/{meshCopy.vertexCount})", 0.3f+0.4f*((float)i/meshCopy.vertexCount));

                int cIdx = grid.FindClosestPointIndex(clL2W.MultiplyPoint3x4(clVL[i]), avVWS, opts.searchRadiusMultiplier);

                if(cIdx != -1){

                    BoneWeight sBW=avBW[cIdx], tBW=new BoneWeight(); float totW=0;

                    int t0=0;float w0=0;int t1=0;float w1=0;int t2=0;float w2=0;int t3=0;float w3=0;

                    ProcessBoneInfluence(sBW.boneIndex0,sBW.weight0,avSMRBones,newBones.ToArray(),partialIdx,ref t0,ref w0,ref totW,win);

                    ProcessBoneInfluence(sBW.boneIndex1,sBW.weight1,avSMRBones,newBones.ToArray(),partialIdx,ref t1,ref w1,ref totW,win);

                    ProcessBoneInfluence(sBW.boneIndex2,sBW.weight2,avSMRBones,newBones.ToArray(),partialIdx,ref t2,ref w2,ref totW,win);

                    ProcessBoneInfluence(sBW.boneIndex3,sBW.weight3,avSMRBones,newBones.ToArray(),partialIdx,ref t3,ref w3,ref totW,win);

                    tBW.boneIndex0=t0;tBW.weight0=w0;tBW.boneIndex1=t1;tBW.weight1=w1;tBW.boneIndex2=t2;tBW.weight2=w2;tBW.boneIndex3=t3;tBW.weight3=w3;

                    if(opts.autoNormalizeWeights && totW > 1e-4f && Mathf.Abs(totW-1f)>1e-3f) {tBW.weight0/=totW;tBW.weight1/=totW;tBW.weight2/=totW;tBW.weight3/=totW;}

                    newCBW[i] = tBW;

                } else newCBW[i] = new BoneWeight();

            }

            meshCopy.boneWeights = newCBW;

            win.Log("ウェイト最適化..."); win.UpdateProgress("ウェイト最適化",0.8f); OptimizeBoneWeights(meshCopy,opts,win);

            win.Log("メッシュ更新..."); win.UpdateProgress("メッシュ更新",0.95f); meshCopy.RecalculateBounds(); cloth.sharedMesh=meshCopy; AddPostTransferDiagnostics(cloth,opts,win);

            win.Log("ウェイト転送完了");

        } finally { if(opts.applyRotationFix && opts.rotationFixAngle != 0) { cloth.transform.localRotation = origRot; cloth.transform.localPosition = origPos; win.Log("回転補正を元に戻しました(finally)"); }}

    }

    private static void ProcessBoneInfluence(int avBIdx, float avW, Transform[] avSMRB, Transform[] newCB, HashSet<int> pIdx, ref int tCBIdx, ref float tCW, ref float totW, WeightSyncProWindow win) { /* (実装省略 - 変更なし) */ if(avW<=1e-5f)return;if(avBIdx<0||avBIdx>=avSMRB.Length)return;Transform actAvB=avSMRB[avBIdx];if(!actAvB)return;int remapIdx=-1;for(int k=0;k<newCB.Length;k++){if(newCB[k]==actAvB){remapIdx=k;break;}}if(remapIdx!=-1){if(pIdx!=null&&!pIdx.Contains(remapIdx))return;tCBIdx=remapIdx;tCW=avW;totW+=avW;} }

    private static HashSet<int> GetPartialTransferBoneIndices(WeightSyncProWindow.PartialTransferArea area, Animator avAnim, Transform[] newCB, WeightSyncProWindow win) { /* (実装省略 - 変更なし) */ var tIdx=new HashSet<int>();if(!avAnim||!avAnim.isHuman){win.LogWarn("部分転送: 非Humanoidのため全身扱い");return null;}List<HumanBodyBones>hBones=new List<HumanBodyBones>();switch(area){case WeightSyncProWindow.PartialTransferArea.胸周り:hBones.AddRange(new[]{HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.UpperChest,HumanBodyBones.LeftShoulder,HumanBodyBones.RightShoulder,HumanBodyBones.Neck});break;/*...他エリア...*/default:return null;}foreach(HumanBodyBones hbb in hBones.Distinct()){Transform avB=avAnim.GetBoneTransform(hbb);if(avB){for(int i=0;i<newCB.Length;i++){if(newCB[i]==avB){tIdx.Add(i);foreach(Transform c in avB.GetComponentsInChildren<Transform>(true)){if(c==avB)continue;for(int j=0;j<newCB.Length;j++){if(newCB[j]==c)tIdx.Add(j);}}break;}}}}if(tIdx.Count>0)win.Log($"部分転送 '{area}': {tIdx.Count}ボーン対象");else win.LogWarn($"部分転送 '{area}': 対象ボーンなし");return tIdx.Count>0?tIdx:null;}

    private static void OptimizeBoneWeights(Mesh m, WeightSyncProWindow.TransferOptions o, WeightSyncProWindow win) { /* (実装省略 - 変更なし) */ BoneWeight[]bws=m.boneWeights;int optC=0;for(int i=0;i<bws.Length;i++){BoneWeight cbw=bws[i];List<(int idx,float w)>infs=new List<(int,float)>();if(cbw.weight0>o.minBoneWeightThreshold)infs.Add((cbw.boneIndex0,cbw.weight0));if(cbw.weight1>o.minBoneWeightThreshold)infs.Add((cbw.boneIndex1,cbw.weight1));if(cbw.weight2>o.minBoneWeightThreshold)infs.Add((cbw.boneIndex2,cbw.weight2));if(cbw.weight3>o.minBoneWeightThreshold)infs.Add((cbw.boneIndex3,cbw.weight3));infs.Sort((a,b)=>b.w.CompareTo(a.w));BoneWeight nbw=new BoneWeight();float totW=0;int bApp=0;for(int j=0;j<infs.Count&&bApp<o.maxBonesPerVertex;j++){if(infs[j].idx<0)continue;if(bApp==0){nbw.boneIndex0=infs[j].idx;nbw.weight0=infs[j].w;}else if(bApp==1){nbw.boneIndex1=infs[j].idx;nbw.weight1=infs[j].w;}else if(bApp==2){nbw.boneIndex2=infs[j].idx;nbw.weight2=infs[j].w;}else if(bApp==3){nbw.boneIndex3=infs[j].idx;nbw.weight3=infs[j].w;}totW+=infs[j].w;bApp++;}if(o.autoNormalizeWeights&&totW>1e-4f&&bApp>0){if(Mathf.Abs(totW-1f)>1e-3f||bApp<infs.Count){if(bApp>0&&nbw.weight0>0)nbw.weight0/=totW;else nbw.weight0=0;if(bApp>1&&nbw.weight1>0)nbw.weight1/=totW;else nbw.weight1=0;if(bApp>2&&nbw.weight2>0)nbw.weight2/=totW;else nbw.weight2=0;if(bApp>3&&nbw.weight3>0)nbw.weight3/=totW;else nbw.weight3=0;}}if(nbw.boneIndex0!=cbw.boneIndex0||!Mathf.Approximately(nbw.weight0,cbw.weight0)||nbw.boneIndex1!=cbw.boneIndex1||!Mathf.Approximately(nbw.weight1,cbw.weight1)||nbw.boneIndex2!=cbw.boneIndex2||!Mathf.Approximately(nbw.weight2,cbw.weight2)||nbw.boneIndex3!=cbw.boneIndex3||!Mathf.Approximately(nbw.weight3,cbw.weight3))optC++;bws[i]=nbw;}m.boneWeights=bws;if(optC>0)win.Log($"{optC}頂点ウェイト最適化");}

    private static void AddPostTransferDiagnostics(SkinnedMeshRenderer cl, WeightSyncProWindow.TransferOptions o, WeightSyncProWindow win) { /* (実装省略 - 変更なし) */ if(!cl||!cl.sharedMesh)return;Mesh m=cl.sharedMesh;BoneWeight[]bws=m.boneWeights;Transform[]bs=cl.bones;int hiWarn=0,manyWarn=0,zeroWarn=0;for(int i=0;i<bws.Length;i++){BoneWeight bw=bws[i];int bc=0;float sumW=0;if(bw.weight0>1e-4f){bc++;sumW+=bw.weight0;}if(bw.weight1>1e-4f){bc++;sumW+=bw.weight1;}if(bw.weight2>1e-4f){bc++;sumW+=bw.weight2;}if(bw.weight3>1e-4f){bc++;sumW+=bw.weight3;}if(bc==0||sumW<1e-3f){zeroWarn++;if(zeroWarn<5)win.AddDiagnosticMessage($"頂点{i}ウェイトなし",WeightSyncProWindow.DiagnosticType.Warning,null,null,i);}if(bc>o.maxBonesPerVertex){manyWarn++;if(manyWarn<5)win.AddDiagnosticMessage($"頂点{i}最大ボーン数超過({o.maxBonesPerVertex}):{bc}ボーン",WeightSyncProWindow.DiagnosticType.Warning,null,null,i);}if(bw.weight0>0.95f&&bc==1&&bs.Length>0&&bw.boneIndex0>=0&&bw.boneIndex0<bs.Length){string bn=bs[bw.boneIndex0].name.ToLowerInvariant();if(bn.Contains("spine")||bn.Contains("chest")||bn.Contains("neck")||bn.Contains("shoulder")||bn.Contains("upleg")||bn.Contains("leg")||bn.Contains("arm")||bn.Contains("elbow")||bn.Contains("knee")){hiWarn++;if(hiWarn<5)win.AddDiagnosticMessage($"頂点{i}ウェイト集中'{bs[bw.boneIndex0].name}':動き硬化の可能性",WeightSyncProWindow.DiagnosticType.Info,bs[bw.boneIndex0],null,i);}}}if(zeroWarn>0)win.LogWarn($"{zeroWarn}頂点ウェイトなし");if(hiWarn>0)win.LogWarn($"{hiWarn}箇所ウェイト集中警告");if(manyWarn>0)win.LogWarn($"{manyWarn}頂点最大ボーン数超過");}

    private class SimpleGridSpatialHash { /* (実装省略 - 変更なし) */ private Vector3 o;private float cs;private Dictionary<Vector3Int,List<int>>g=new Dictionary<Vector3Int,List<int>>();public SimpleGridSpatialHash(Vector3 wo,float s){o=wo;cs=Mathf.Max(1e-3f,s);}private Vector3Int W2G(Vector3 wp){return new Vector3Int(Mathf.FloorToInt((wp.x-o.x)/cs),Mathf.FloorToInt((wp.y-o.y)/cs),Mathf.FloorToInt((wp.z-o.z)/cs));}public void AddPoint(Vector3 wp,int idx){Vector3Int gc=W2G(wp);if(!g.ContainsKey(gc))g[gc]=new List<int>();g[gc].Add(idx);}public int FindClosestPointIndex(Vector3 qwp,Vector3[]avws,float srm){Vector3Int qgc=W2G(qwp);float msd=float.MaxValue;int ci=-1;float srs=(cs*srm)*(cs*srm);int scr=Mathf.CeilToInt(srm);for(int x=-scr;x<=scr;x++)for(int y=-scr;y<=scr;y++)for(int z=-scr;z<=scr;z++){Vector3Int cc=qgc+new Vector3Int(x,y,z);if(g.TryGetValue(cc,out List<int>pts)){foreach(int idx in pts){float sd=(avws[idx]-qwp).sqrMagnitude;if(sd<msd&&sd<=srs){msd=sd;ci=idx;}}}}if(ci==-1&&avws.Length>0){msd=float.MaxValue;for(int i=0;i<avws.Length;i++){float sd=(avws[i]-qwp).sqrMagnitude;if(sd<msd){msd=sd;ci=i;}}}return ci;} }

}

  

public class WeightSyncProWindow : EditorWindow

{

    public enum DiagnosticType { Info, Warning, Error, BoneMap }

    public class DiagnosticMessage { public string message; public DiagnosticType type; public Transform bone1, bone2; public int vertIdx=-1; public MessageType GetMessageType(){ switch(type){case DiagnosticType.Info:return MessageType.Info;case DiagnosticType.Warning:return MessageType.Warning;case DiagnosticType.Error:return MessageType.Error;default:return MessageType.None;}}}

    public enum PartialTransferArea { 全身, 胸周り, 股関節周り, 脚全体, 腕全体 }

    public enum RotationFixAxis { X, Y, Z }

    [System.Serializable] public class TransferOptions { public int maxBonesPerVertex=4; public float minBoneWeightThreshold=0.001f; public bool autoNormalizeWeights=true; public PartialTransferArea partialTransferArea=PartialTransferArea.全身; public int gridCellDivisions=10; public float searchRadiusMultiplier=2f; public bool applyRotationFix=false; public RotationFixAxis rotationFixAxis=RotationFixAxis.Y; public float rotationFixAngle=0f; }

  

    GameObject avObj, clObj; string remP="",remS=""; TransferOptions opts=new TransferOptions();

    SkinnedMeshRenderer avRen, clRen; Dictionary<Transform,Transform> cbm=new Dictionary<Transform,Transform>();

    List<DiagnosticMessage> diags=new List<DiagnosticMessage>(); Vector2 scroll; string procStep=""; bool advSet=false;

    public const string VERSION = "1.1.0"; // Simplified from previous

  

    [MenuItem("Tools/WeightSyncPro " + VERSION)] static void Init() { GetWindow<WeightSyncProWindow>("WeightSyncPro").minSize=new Vector2(500,600); }

    void OnEnable() { /* (EditorPrefs読み込み - 文字数削減のため詳細は省略) */ remP=EditorPrefs.GetString("WSP_remP","");remS=EditorPrefs.GetString("WSP_remS","");opts.maxBonesPerVertex=EditorPrefs.GetInt("WSP_maxB",4);opts.minBoneWeightThreshold=EditorPrefs.GetFloat("WSP_minW",0.001f);opts.autoNormalizeWeights=EditorPrefs.GetBool("WSP_norm",true);opts.partialTransferArea=(PartialTransferArea)EditorPrefs.GetInt("WSP_part",(int)PartialTransferArea.全身);opts.gridCellDivisions=EditorPrefs.GetInt("WSP_gridD",10);opts.searchRadiusMultiplier=EditorPrefs.GetFloat("WSP_gridR",2f);advSet=EditorPrefs.GetBool("WSP_advSet",false);opts.applyRotationFix=EditorPrefs.GetBool("WSP_rotF",false);opts.rotationFixAxis=(RotationFixAxis)EditorPrefs.GetInt("WSP_rotA",(int)RotationFixAxis.Y);opts.rotationFixAngle=EditorPrefs.GetFloat("WSP_rotAng",0f); }

    void OnDisable() { /* (EditorPrefs保存 - 文字数削減のため詳細は省略) */ EditorPrefs.SetString("WSP_remP",remP);EditorPrefs.SetString("WSP_remS",remS);EditorPrefs.SetInt("WSP_maxB",opts.maxBonesPerVertex);EditorPrefs.SetFloat("WSP_minW",opts.minBoneWeightThreshold);EditorPrefs.SetBool("WSP_norm",opts.autoNormalizeWeights);EditorPrefs.SetInt("WSP_part",(int)opts.partialTransferArea);EditorPrefs.SetInt("WSP_gridD",opts.gridCellDivisions);EditorPrefs.SetFloat("WSP_gridR",opts.searchRadiusMultiplier);EditorPrefs.SetBool("WSP_advSet",advSet);EditorPrefs.SetBool("WSP_rotF",opts.applyRotationFix);EditorPrefs.SetInt("WSP_rotA",(int)opts.rotationFixAxis);EditorPrefs.SetFloat("WSP_rotAng",opts.rotationFixAngle); }

  

    void OnGUI() {

        GUILayout.Label($"WeightSyncPro {VERSION}", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox("【重要】ウェイト転送前の準備ステップ：\n1.【FBX設定】アバターと衣装のFBXインポート設定で「Bake Axis Conversion」を確認。\n2.【配置】アバターと衣装をワールド空間で同じ位置・回転・スケール(推奨1,1,1)でTポーズ等で重ねる。\n3.【選択】下記にアバターと衣装のGameObjectを正しく設定。", MessageType.Info);

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField("1. オブジェクト設定", EditorStyles.boldLabel);

        avObj=(GameObject)EditorGUILayout.ObjectField("アバター(基準)",avObj,typeof(GameObject),true); clObj=(GameObject)EditorGUILayout.ObjectField("衣装(転送先)",clObj,typeof(GameObject),true);

        if(EditorGUI.EndChangeCheck()||(avObj&&!avRen)||(clObj&&!clRen))ValRens();

        if(GUILayout.Button("オブジェクト再検証",GUILayout.Width(150)))ValRens(); EditorGUILayout.Space();

        if(!avObj||!clObj){EditorGUILayout.HelpBox("アバターと衣装を設定",MessageType.Warning);if(EditorGUI.EndChangeCheck())SavePrefs();return;}

        if(!avRen){EditorGUILayout.HelpBox("アバターSMRなし",MessageType.Error);if(EditorGUI.EndChangeCheck())SavePrefs();return;}

        if(!clRen){EditorGUILayout.HelpBox("衣装SMRなし",MessageType.Error);if(EditorGUI.EndChangeCheck())SavePrefs();return;}

        EditorGUILayout.HelpBox($"アバター:{avRen.name}({avRen.sharedMesh?.vertexCount??0}頂点)\n衣装:{clRen.name}({clRen.sharedMesh?.vertexCount??0}頂点)",MessageType.None); EditorGUILayout.Space();

        EditorGUILayout.LabelField("2. ウェイト転送オプション",EditorStyles.boldLabel);

        opts.maxBonesPerVertex=EditorGUILayout.IntSlider("Max Bones/Vertex",opts.maxBonesPerVertex,1,8);

        opts.minBoneWeightThreshold=EditorGUILayout.Slider("Min Bone Weight",opts.minBoneWeightThreshold,0f,0.1f);

        opts.autoNormalizeWeights=EditorGUILayout.Toggle("Auto Normalize Weights",opts.autoNormalizeWeights); EditorGUILayout.Space();

  

        advSet=EditorGUILayout.Foldout(advSet,"高度な設定");

        if(advSet){ EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("ボーンマッピング:",EditorStyles.boldLabel);remP=EditorGUILayout.TextField("除去プレフィックス",remP);remS=EditorGUILayout.TextField("除去サフィックス",remS);EditorGUILayout.Space();

            EditorGUILayout.LabelField("詳細転送オプション:",EditorStyles.boldLabel);opts.partialTransferArea=(PartialTransferArea)EditorGUILayout.EnumPopup("簡易部分転送",opts.partialTransferArea);

            opts.gridCellDivisions=EditorGUILayout.IntSlider("探索グリッド分割",opts.gridCellDivisions,5,30);opts.searchRadiusMultiplier=EditorGUILayout.Slider("探索半径係数",opts.searchRadiusMultiplier,1f,5f);EditorGUILayout.Space();

            EditorGUILayout.LabelField("実験的機能(問題発生時):",EditorStyles.boldLabel);opts.applyRotationFix=EditorGUILayout.ToggleLeft("回転補正(ローカル軸)", opts.applyRotationFix);

            if(opts.applyRotationFix){opts.rotationFixAxis=(RotationFixAxis)EditorGUILayout.EnumPopup("補正軸",opts.rotationFixAxis);opts.rotationFixAngle=EditorGUILayout.FloatField("補正角度(度)",opts.rotationFixAngle);EditorGUILayout.HelpBox("処理中に一時適用、終了後解除",MessageType.Info);}

            EditorGUI.indentLevel--;

        } EditorGUILayout.Space();

  

        EditorGUILayout.LabelField("3. 実行",EditorStyles.boldLabel); GUI.backgroundColor=new Color(0.6f,1f,0.6f);

        if(GUILayout.Button("ウェイト転送実行",GUILayout.Height(40))){

            if(EditorUtility.DisplayDialog("【重要】実行前最終確認","ウェイト転送を実行しますか？\n衣装のメッシュとボーン構造が変更されます(Undo可)。\n\n【！！！再確認！！！】\n上記「準備ステップ」1～3を全て確認しましたか？\n(特にFBXインポート設定とオブジェクト配置)","実行(準備OK)","キャンセル"))ExecTrans();

        } GUI.backgroundColor=Color.white; EditorGUILayout.Space();

        EditorGUILayout.LabelField("4. 処理ステータス & 診断",EditorStyles.boldLabel);

        if(!string.IsNullOrEmpty(procStep))EditorGUILayout.HelpBox($"状態:{procStep}",MessageType.None);

        if(diags.Count>0){ scroll=EditorGUILayout.BeginScrollView(scroll,GUILayout.MinHeight(100),GUILayout.MaxHeight(200)); foreach(var m in diags)EditorGUILayout.HelpBox($"[{m.type}]{m.message}",m.GetMessageType()); EditorGUILayout.EndScrollView(); if(GUILayout.Button("診断クリア",GUILayout.Width(150))){diags.Clear();Repaint();}}

        else if(!string.IsNullOrEmpty(procStep)&&(procStep=="完了"||procStep.Contains("エラー")))EditorGUILayout.HelpBox(procStep=="完了"?"処理完了":"エラー発生",procStep=="完了"?MessageType.Info:MessageType.Error);

        if(EditorGUI.EndChangeCheck())SavePrefs();

    }

  

    void ValRens(){SkinnedMeshRenderer pa=avRen,pc=clRen;avRen=clRen=null;if(avObj){avRen=avObj.GetComponentInChildren<SkinnedMeshRenderer>(false)??avObj.GetComponent<SkinnedMeshRenderer>();if(!avRen)AddDiag("アバターSMRなし",DiagnosticType.Error,avObj.transform);else if(!avRen.sharedMesh)AddDiag("アバターメッシュなし",DiagnosticType.Error,avRen.transform);}if(clObj){clRen=clObj.GetComponentInChildren<SkinnedMeshRenderer>(false)??clObj.GetComponent<SkinnedMeshRenderer>();if(!clRen)AddDiag("衣装SMRなし",DiagnosticType.Error,clObj.transform);else if(!clRen.sharedMesh)AddDiag("衣装メッシュなし",DiagnosticType.Error,clRen.transform);}if(avRen!=pa||clRen!=pc)Repaint();}

    void ExecTrans(){procStep="初期化...";diags.Clear();Log($"---WSP {VERSION}開始---");var st=System.Diagnostics.Stopwatch.StartNew();ValRens();if(!avRen||!clRen||!avRen.sharedMesh||!clRen.sharedMesh){LogError("SMR/メッシュ無効");EditorUtility.DisplayDialog("エラー","SMR/メッシュ無効。「準備ステップ」確認","OK");procStep="エラー:無効SMR/メッシュ";st.Stop();EditorUtility.ClearProgressBar();Repaint();return;}if(avRen==clRen){LogError("同一SMR");EditorUtility.DisplayDialog("エラー","同一SMR。別オブジェクト設定","OK");procStep="エラー:同一SMR";st.Stop();EditorUtility.ClearProgressBar();Repaint();return;}Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("WSP Transfer");int grp=Undo.GetCurrentGroup();Undo.RecordObject(clRen,"WSP:SMR Update");if(clRen.sharedMesh)Undo.RecordObject(clRen.sharedMesh,"WSP:Mesh Update");

        try{procStep="ボーンマップ中...";UpdateProg(0.05f);Transform avR=BoneMappingCore.FindRootBone(avRen.transform,this),clR=BoneMappingCore.FindRootBone(clRen.transform,this);if(!avR)throw new Exception("アバタールート検出失敗");if(!clR)throw new Exception("衣装ルート検出失敗");cbm=BoneMappingCore.CreateBoneMap(clR,avR,remP,remS,this);if(cbm.Count==0){AddDiag("ボーンマップ失敗。「高度な設定」の除去P/S確認",DiagnosticType.Error);throw new Exception("ボーンマップ失敗");}AddDiag($"{cbm.Count}ペアマップ",DiagnosticType.Info);Log($"{cbm.Count}ペアマップ");

            procStep="ウェイト転送/最適化中...";UpdateProg(0.3f);WeightTransferCore.ProcessSkinnedMesh(clRen,avRen,cbm,opts,this);

            procStep="完了処理中...";UpdateProg(0.9f);if(clRen.sharedMesh)EditorUtility.SetDirty(clRen.sharedMesh);EditorUtility.SetDirty(clRen);AssetDatabase.SaveAssets();

            st.Stop();Log($"---WSP完了({st.Elapsed.TotalSeconds:F2}s)---");EditorUtility.DisplayDialog("成功",$"WSP完了({st.Elapsed.TotalSeconds:F2}s)","OK");procStep="完了";Undo.CollapseUndoOperations(grp);

        }catch(Exception ex){st.Stop();LogError($"WSPエラー:{ex.GetType().Name}:{ex.Message}\n{ex.StackTrace}");AddDiag($"致命的エラー:{ex.Message}。コンソールと「準備ステップ」確認",DiagnosticType.Error);EditorUtility.DisplayDialog("致命的エラー",$"WSPエラー。診断/コンソール確認","OK");procStep=$"致命的エラー:{ex.GetType().Name}";}

        finally{EditorUtility.ClearProgressBar();Repaint();}

    }

    void SavePrefs(){/*(EditorPrefs保存 - 詳細はOnDisable参照)*/OnDisable();}

    public void Log(string m,UnityEngine.Object c=null){Debug.Log($"[WSP]{m}",c);}

    public void LogWarn(string m,UnityEngine.Object c=null){Debug.LogWarning($"[WSP]{m}",c);}

    public void LogError(string m,UnityEngine.Object c=null){Debug.LogError($"[WSP]{m}",c);}

  

    // --- 追加されたメソッド ---

    public void UpdateProgress(string stepDescription, float progress)

    {

        this.procStep = stepDescription; // procStep は現在の処理ステップを示す文字列

        UpdateProg(progress); // 既存の UpdateProg(float p) を呼び出す

    }

  

    public void AddDiagnosticMessage(string message, DiagnosticType type, Transform bone1 = null, Transform bone2 = null, int vertexIndex = -1)

    {

        // AddDiag は診断メッセージをリストに追加し、ログ出力などを行う

        AddDiag(message, type, bone1, bone2, vertexIndex); // 既存の AddDiag を呼び出す

    }

    // --- 追加されたメソッドここまで ---

  

    public void AddDiag(string m,DiagnosticType t,Transform b1=null,Transform b2=null,int vIdx=-1){var msg=new DiagnosticMessage{message=m,type=t,bone1=b1,bone2=b2,vertIdx=vIdx};diags.Add(msg);if(t==DiagnosticType.Error)LogError(m,b1??b2);else if(t==DiagnosticType.Warning)LogWarn(m,b1??b2);Repaint();}

    public void UpdateProg(float p){EditorUtility.DisplayProgressBar("WeightSyncPro",$"{procStep}({p*100:F0}%)",p);}

}

#endif 
'''


**WeightSyncPro**は、Unityエディタ上でアバターと衣装のウェイト転送・最適化を直感的かつ安全に実現するエディタ拡張ツールです。  
ユーザーは、アバター（転送元）と衣装（転送先）のGameObjectをウィンドウ上で指定し、ワンクリックで「ボーンマッピング」から「ウェイト転送」「最適化」までを自動実行できます。


ウェイト転送に特化したい
