#if UNITY_EDITOR

using UnityEngine;

using UnityEditor;

using System;

using System.Collections.Generic;

using System.Linq;

using System.Text.RegularExpressions;

  

public class BoneSyncPro : EditorWindow

{

    [MenuItem("Tools/BoneSync Pro")]

    static void Open() => GetWindow<BoneSyncPro>("BoneSync Pro");

  

    GameObject avatar, outfit;

    float scale = 1f;

    bool alignPosition = true, alignRotation = false, fixFootwear = true, fixHands = true, adjustBounds = true;

    Vector2 scroll;

    List<Issue> issues = new();

    Dictionary<Transform, Transform> boneMap = new();

    Transform avatarRoot, outfitRoot;

    static readonly string[] tailKeywords = { "tail", "chain", "尻尾", "しっぽ", "physicsbone_joint" };

  

    void OnGUI()

    {

        GUILayout.Label("BoneSync Pro", EditorStyles.boldLabel);

        avatar = (GameObject)EditorGUILayout.ObjectField("Avatar", avatar, typeof(GameObject), true);

        outfit = (GameObject)EditorGUILayout.ObjectField("Outfit", outfit, typeof(GameObject), true);

        scale = EditorGUILayout.Slider("Scale", scale, 0.1f, 3f);

        alignPosition = EditorGUILayout.Toggle("位置合わせ", alignPosition);

        alignRotation = EditorGUILayout.Toggle("回転合わせ", alignRotation);

        fixFootwear = EditorGUILayout.Toggle("靴補正", fixFootwear);

        fixHands = EditorGUILayout.Toggle("手指補正", fixHands);

        adjustBounds = EditorGUILayout.Toggle("Bounds自動調整", adjustBounds);

  

        if (avatar == null || outfit == null || !avatar.activeInHierarchy || !outfit.activeInHierarchy)

        {

            EditorGUILayout.HelpBox("AvatarとOutfitを選択してください。", MessageType.Info);

            return;

        }

  

        if (GUILayout.Button("1. 自動フィット＆診断", GUILayout.Height(30)))

            Run();

  

        if (issues.Count > 0)

        {

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(300));

            foreach (var issue in issues)

            {

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                GUILayout.Label($"[{issue.type}] {issue.message}", EditorStyles.wordWrappedLabel);

                if (issue.sourceBone != null) EditorGUILayout.ObjectField("Src", issue.sourceBone, typeof(Transform), true);

                if (issue.targetBone != null) EditorGUILayout.ObjectField("Dst", issue.targetBone, typeof(Transform), true);

                if (issue.fixes.Any())

                {

                    issue.selected = EditorGUILayout.ToggleLeft("修正", issue.selected);

                    GUILayout.Label($"→ {issue.description}", EditorStyles.miniLabel);

                }

                EditorGUILayout.EndVertical();

            }

            EditorGUILayout.EndScrollView();

  

            if (issues.Any(i => i.selected && i.fixes.Any()))

                if (GUILayout.Button("2. 選択した修正を適用", GUILayout.Height(28))) ApplySelectedFixes();

            if (issues.Any(i => i.fixes.Any()))

                if (GUILayout.Button("3. すべての修正を適用", GUILayout.Height(28))) ApplyAllFixes();

        }

    }

  

    void Run()

    {

        issues.Clear(); boneMap.Clear();

        outfitRoot = FindRoot(outfit?.transform); avatarRoot = FindRoot(avatar?.transform);

        if (outfitRoot == null || avatarRoot == null)

        {

            EditorUtility.DisplayDialog("エラー", "Rootが見つかりません", "OK");

            return;

        }

        Undo.RegisterFullObjectHierarchyUndo(outfit, "BoneSync Pro");

        outfit.transform.SetParent(avatar.transform, true);

        SetScale(outfit.transform, scale);

        boneMap = CreateBoneMap(outfitRoot, avatarRoot);

        CheckMappingRate();

        ApplyBoneMap(boneMap, alignPosition, alignRotation);

        if (fixFootwear) FixFootwear();

        if (fixHands) FixHands();

        if (adjustBounds) AdjustBounds(outfit);

        Diagnose();

    }

  

    void ApplySelectedFixes()

    {

        Undo.RegisterFullObjectHierarchyUndo(outfit, "BoneSync Pro");

        foreach (var issue in issues.Where(i => i.selected && !IsTailBone(i.sourceBone)))

            issue.fixes.FirstOrDefault()?.Invoke();

        Run();

    }

  

    void ApplyAllFixes()

    {

        Undo.RegisterFullObjectHierarchyUndo(outfit, "BoneSync Pro");

        foreach (var issue in issues.Where(i => i.fixes.Any() && !IsTailBone(i.sourceBone)))

            issue.fixes.First().Invoke();

        Run();

    }

  

    Transform FindRoot(Transform transform)

    {

        if (transform == null) return null;

        var smr = transform.GetComponentInChildren<SkinnedMeshRenderer>();

        if (smr?.rootBone != null) return smr.rootBone.parent ?? smr.rootBone;

        return transform.GetComponentsInChildren<Transform>().FirstOrDefault(x => x.name.Equals("Armature", StringComparison.OrdinalIgnoreCase)) ?? transform;

    }

  

    Dictionary<Transform, Transform> CreateBoneMap(Transform sourceRoot, Transform targetRoot)

    {

        var targetDict = targetRoot.GetComponentsInChildren<Transform>(true)

            .Where(b => b != targetRoot)

            .GroupBy(b => NormalizeBoneName(b.name))

            .ToDictionary(g => g.Key, g => g.First());

  

        var map = new Dictionary<Transform, Transform>();

        foreach (var sourceBone in sourceRoot.GetComponentsInChildren<Transform>(true).Where(b => b != sourceRoot))

        {

            var key = NormalizeBoneName(sourceBone.name);

            if (targetDict.TryGetValue(key, out var targetBone)) map[sourceBone] = targetBone;

        }

        return map;

    }

  

    string NormalizeBoneName(string name)

    {

        name = name.ToLowerInvariant();

        name = Regex.Replace(name, @"\d+", "");

        name = Regex.Replace(name, @"^mixamorig[:_]", "");

        name = Regex.Replace(name, @"(\.l|\.r|_l|_r)$", m => m.Value.Contains("l") ? "_l" : "_r");

        name = name.Replace("left", "_l").Replace("right", "_r");

        name = Regex.Replace(name, @"[^\w_]", "");

        return Regex.Replace(name, @"_{2,}", "_").Trim('_');

    }

  

    void SetScale(Transform transform, float scaleValue)

    {

        if (transform == null) return;

        Vector3 worldPos = transform.position; Quaternion worldRot = transform.rotation;

        transform.localScale = Vector3.one * scaleValue;

        transform.position = worldPos; transform.rotation = worldRot;

    }

  

    void ApplyBoneMap(Dictionary<Transform, Transform> map, bool alignPos, bool alignRot)

    {

        foreach (var kv in map)

        {

            if (IsTailBone(kv.Key) || kv.Key == null || kv.Value == null) continue;

            Undo.RecordObject(kv.Key, "BoneSync Pro");

            if (alignPos) kv.Key.position = kv.Value.position;

            if (alignRot) kv.Key.rotation = kv.Value.rotation;

        }

    }

  

    void AdjustBounds(GameObject root)

    {

        if (root == null) return;

        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))

        {

            Undo.RecordObject(smr, "BoneSync Pro");

            var bounds = smr.localBounds; bounds.Expand(bounds.size * 0.5f + Vector3.one * 0.1f);

            smr.localBounds = bounds;

        }

    }

  

    void CheckMappingRate()

    {

        if (outfitRoot == null) return;

        int total = outfitRoot.GetComponentsInChildren<Transform>(true).Count(t => t != outfitRoot);

        float rate = total > 0 ? (float)boneMap.Count / total : 1f;

        if (rate < 0.6f)

            issues.Add(new Issue { type = IssueType.Rate, message = $"Mapping rate low: {rate:P1}" });

    }

  

    void FixFootwear() { /* 靴補正（必要なら実装） */ }

    void FixHands() { /* 手指補正（必要なら実装） */ }

  

    void Diagnose()

    {

        foreach (var kv in boneMap)

        {

            if (IsTailBone(kv.Key) || kv.Key == null || kv.Value == null) continue;

            float distance = Vector3.Distance(kv.Key.position, kv.Value.position);

            if (distance > 0.02f)

            {

                var issue = new Issue { type = IssueType.Pos, message = $"{kv.Key.name} offset {distance * 100:F1}cm", sourceBone = kv.Key, targetBone = kv.Value };

                issue.Add("位置補正", () => { Undo.RecordObject(kv.Key, "BoneSync Pro"); kv.Key.position = kv.Value.position; });

                issues.Add(issue);

            }

            float angle = Quaternion.Angle(kv.Key.rotation, kv.Value.rotation);

            if (angle > 15f)

            {

                var issue = new Issue { type = IssueType.Rot, message = $"{kv.Key.name} rot {angle:F0}°", sourceBone = kv.Key, targetBone = kv.Value };

                issue.Add("回転補正", () => { Undo.RecordObject(kv.Key, "BoneSync Pro"); kv.Key.rotation = kv.Value.rotation; });

                issues.Add(issue);

            }

        }

    }

  

    static bool IsTailBone(Transform bone)

    {

        if (bone == null) return false;

        string name = bone.name.ToLowerInvariant();

        return tailKeywords.Any(k => name.Contains(k));

    }

  

    enum IssueType { None, Rate, Pos, Rot }

    class Issue

    {

        public IssueType type;

        public string message;

        public Transform sourceBone, targetBone;

        public List<Action> fixes = new();

        public string description;

        public bool selected;

        public void Add(string desc, Action fix) { fixes.Add(fix); description = desc; }

    }

}

#endif
