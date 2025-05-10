using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace HhotateA.WeightSyncPro
{
    public enum TransferMode
    {
        Surface = 0,
    }

    /// <summary>
    /// ウェイト転送処理の診断情報を保持するクラス
    /// </summary>
    public class DiagnosticInfo
    {
        public List<string> warnings = new List<string>();
        public int fallbackUsedInSurfaceModeCount = 0;
        public int nullBonesInBindPoseCount = 0;
        public int nullBonesInMappedBonesCount = 0;

        public void AddWarning(string message)
        {
            warnings.Add(message);
        }

        public void LogDiagnostics()
        {
            if (warnings.Any() || fallbackUsedInSurfaceModeCount > 0 || nullBonesInBindPoseCount > 0 || nullBonesInMappedBonesCount > 0)
            {
                Debug.LogWarning("--- Weight Transfer Diagnostics ---");
                foreach (var warning in warnings)
                {
                    Debug.LogWarning(warning);
                }
                if (nullBonesInMappedBonesCount > 0)
                {
                     Debug.LogWarning($"{nullBonesInMappedBonesCount} null bones encountered during bone mapping. This means some source bones could not be matched to target bones, potentially affecting weights.");
                }
                if (fallbackUsedInSurfaceModeCount > 0)
                {
                    Debug.LogWarning($"Surface weight calculation used fallback (nearest vertex) {fallbackUsedInSurfaceModeCount} times for target vertices. If this number is high, consider adjusting Search Radius or check mesh proximity/overlap.");
                }
                if (nullBonesInBindPoseCount > 0)
                {
                    Debug.LogWarning($"{nullBonesInBindPoseCount} null bones were encountered when generating bind poses for the target mesh. This can lead to incorrect mesh deformation or shrinking. Ensure all necessary bones are correctly mapped and present in the target armature.");
                }
                Debug.LogWarning("--- End of Diagnostics ---");
            }
            else
            {
                Debug.Log("Weight Transfer Diagnostics: No immediate issues found based on current checks.");
            }
        }
    }

    public class WeightSyncProWindow_v2 : EditorWindow
    {
        public GameObject sourceAvatar;
        public GameObject targetAvatar;
        public SkinnedMeshRenderer sourceRenderer;
        public SkinnedMeshRenderer targetRenderer;

        public bool autoDetectHumanoid = true;
        public bool createBackup = true;
        public bool keepMaterials = true;

        public float searchRadius = 0.05f;
        public int maxBoneInfluence = 4;

        [MenuItem("Window/HhotateA/WeightSyncPro_Simplified_v2")]
        public static void ShowWindow()
        {
            GetWindow<WeightSyncProWindow_v2>("WeightSyncPro Simplified");
        }

        void OnGUI()
        {
            GUILayout.Label("Weight Sync Pro (Simplified with Diagnostics)", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            sourceAvatar = EditorGUILayout.ObjectField("Source Avatar", sourceAvatar, typeof(GameObject), true) as GameObject;
            targetAvatar = EditorGUILayout.ObjectField("Target Avatar", targetAvatar, typeof(GameObject), true) as GameObject;

            EditorGUILayout.Space();

            sourceRenderer = EditorGUILayout.ObjectField("Source Renderer", sourceRenderer, typeof(SkinnedMeshRenderer), true) as SkinnedMeshRenderer;
            targetRenderer = EditorGUILayout.ObjectField("Target Renderer", targetRenderer, typeof(SkinnedMeshRenderer), true) as SkinnedMeshRenderer;

            EditorGUILayout.Space();

            autoDetectHumanoid = EditorGUILayout.Toggle("Auto Detect Humanoid Bones", autoDetectHumanoid);
            createBackup = EditorGUILayout.Toggle("Create Backup", createBackup);
            keepMaterials = EditorGUILayout.Toggle("Keep Target Materials", keepMaterials);

            EditorGUILayout.Space();
            GUILayout.Label("Transfer Parameters (Surface Mode)", EditorStyles.boldLabel);
            searchRadius = EditorGUILayout.Slider("Search Radius", searchRadius, 0.001f, 0.5f); // Range adjusted slightly
            maxBoneInfluence = EditorGUILayout.IntSlider("Max Bone Influences", maxBoneInfluence, 1, 8); // Unity supports up to 32 with specific settings, but 8 is a practical limit for most UI [3][6]

            EditorGUILayout.Space();

            if (GUILayout.Button("Transfer Weights"))
            {
                if (ValidateInputs())
                {
                    var options = new TransferOptions
                    {
                        pMode = TransferMode.Surface,
                        autoDetectHumanoid = this.autoDetectHumanoid,
                        createBackup = this.createBackup,
                        keepMaterials = this.keepMaterials,
                        searchRadius = this.searchRadius,
                        maxBoneInfluence = this.maxBoneInfluence,
                    };

                    WeightTransferCore_v2.TransferWeights(sourceAvatar, targetAvatar, sourceRenderer, targetRenderer, options);
                }
            }
        }

        bool ValidateInputs()
        {
            if (sourceAvatar == null) { EditorUtility.DisplayDialog("Error", "Source Avatar is not set.", "OK"); return false; }
            if (targetAvatar == null) { EditorUtility.DisplayDialog("Error", "Target Avatar is not set.", "OK"); return false; }
            if (sourceRenderer == null) { EditorUtility.DisplayDialog("Error", "Source Renderer is not set.", "OK"); return false; }
            if (targetRenderer == null) { EditorUtility.DisplayDialog("Error", "Target Renderer is not set.", "OK"); return false; }
            if (sourceRenderer.sharedMesh == null) { EditorUtility.DisplayDialog("Error", "Source Renderer has no mesh.", "OK"); return false; }
            if (targetRenderer.sharedMesh == null) { EditorUtility.DisplayDialog("Error", "Target Renderer has no mesh.", "OK"); return false; }
            return true;
        }

        public static Dictionary<HumanBodyBones, Transform> GetHumanoidBones(GameObject avatar)
        {
            var animator = avatar.GetComponent<Animator>();
            if (animator == null || !animator.isHuman) return new Dictionary<HumanBodyBones, Transform>();

            var bones = new Dictionary<HumanBodyBones, Transform>();
            foreach (HumanBodyBones boneType in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (boneType == HumanBodyBones.LastBone) continue;
                Transform boneTransform = animator.GetBoneTransform(boneType);
                if (boneTransform != null)
                {
                    bones[boneType] = boneTransform;
                }
            }
            return bones;
        }

        public class TransferOptions
        {
            public TransferMode pMode;
            public bool autoDetectHumanoid;
            public bool createBackup;
            public bool keepMaterials;
            public float searchRadius;
            public int maxBoneInfluence;
        }
    }

    public static class WeightTransferCore_v2
    {
        public static void TransferWeights(GameObject sourceRoot, GameObject targetRoot,
                                           SkinnedMeshRenderer specificSourceRenderer, SkinnedMeshRenderer specificTargetRenderer,
                                           WeightSyncProWindow_v2.TransferOptions options)
        {
            DiagnosticInfo diagnosticInfo = new DiagnosticInfo();

            // Perform diagnostic checks
            CheckTransformScaleRecursive(sourceRoot.transform, "Source Avatar Root", diagnosticInfo);
            CheckTransformScaleRecursive(targetRoot.transform, "Target Avatar Root", diagnosticInfo);
            if (specificSourceRenderer != null) CheckTransformScaleRecursive(specificSourceRenderer.transform, "Source Renderer Object", diagnosticInfo);
            if (specificTargetRenderer != null) CheckTransformScaleRecursive(specificTargetRenderer.transform, "Target Renderer Object", diagnosticInfo);


            if (options.createBackup)
            {
                BackupTarget(targetRoot);
            }

            Dictionary<string, Transform> sourceBoneMap;
            Dictionary<string, Transform> targetBoneMap;

            if (options.autoDetectHumanoid)
            {
                var sourceHumanBones = WeightSyncProWindow_v2.GetHumanoidBones(sourceRoot);
                var targetHumanBones = WeightSyncProWindow_v2.GetHumanoidBones(targetRoot);

                sourceBoneMap = sourceHumanBones.Values.Where(b => b != null).ToDictionary(b => GetBonePath(b, sourceRoot.transform), b => b);
                targetBoneMap = targetHumanBones.Values.Where(b => b != null).ToDictionary(b => GetBonePath(b, targetRoot.transform), b => b);

                if (sourceBoneMap.Count == 0 || targetBoneMap.Count == 0)
                {
                    diagnosticInfo.AddWarning("Could not auto-detect Humanoid bones effectively for one or both avatars (bone map empty). Falling back to name matching for all bones.");
                    sourceBoneMap = GetAllBones(sourceRoot);
                    targetBoneMap = GetAllBones(targetRoot);
                }
            }
            else
            {
                sourceBoneMap = GetAllBones(sourceRoot);
                targetBoneMap = GetAllBones(targetRoot);
            }

            if (specificSourceRenderer == null || specificTargetRenderer == null)
            {
                Debug.LogError("Source or Target Renderer is not specified correctly for processing.");
                return;
            }

            ProcessRendererPair(specificSourceRenderer, specificTargetRenderer,
                                sourceBoneMap, targetBoneMap,
                                options,
                                sourceRoot.transform, targetRoot.transform,
                                diagnosticInfo);

            // Log all collected diagnostic information
            diagnosticInfo.LogDiagnostics();
        }

        private static void CheckTransformScaleRecursive(Transform t, string objectName, DiagnosticInfo diagnostics)
        {
            if (t == null) return;
            
            // Check current transform's scale
            if (t.localScale != Vector3.one)
            {
                diagnostics.AddWarning($"Scale of '{t.name}' (part of {objectName}) is {t.localScale}, not (1,1,1). This can affect mesh size and deformation during weight transfer, especially bind pose calculation.");
            }

            // Recursively check parents up to the root of the prefab/scene instance
            // We assume that if sourceRoot/targetRoot itself is scaled, it's a base for operations.
            // This check is more for ensuring sub-objects within the avatar hierarchy are not unexpectedly scaled.
            // No need to go "above" sourceRoot or targetRoot for this particular check.
        }


        private static void ProcessRendererPair(SkinnedMeshRenderer sourceRenderer, SkinnedMeshRenderer targetRenderer,
                                         Dictionary<string, Transform> sourceBoneMap, Dictionary<string, Transform> targetBoneMap,
                                         WeightSyncProWindow_v2.TransferOptions commonOptions,
                                         Transform sourceRootTransform, Transform targetRootTransform,
                                         DiagnosticInfo diagnosticInfo)
        {
            Mesh sourceMesh = sourceRenderer.sharedMesh;
            Mesh targetMesh = targetRenderer.sharedMesh;

            if (sourceMesh == null || targetMesh == null)
            {
                Debug.LogError("Mesh not found on one of the renderers. Source: " + (sourceMesh == null) + ", Target: " + (targetMesh == null));
                return;
            }

            Vector3[] sourceVertices = sourceMesh.vertices;
            Vector3[] targetVertices = targetMesh.vertices;
            BoneWeight[] sourceBoneWeights = sourceMesh.boneWeights;
            Transform[] sourceBones = sourceRenderer.bones;

            if (sourceBones == null || sourceBones.Length == 0)
            {
                diagnosticInfo.AddWarning($"Source Renderer '{sourceRenderer.name}' has no bones assigned. Cannot transfer weights.");
                return;
            }


            BoneWeight[] newBoneWeights = new BoneWeight[targetVertices.Length];
            Transform[] newBones = MapBones(sourceBones, sourceBoneMap, targetBoneMap, targetRootTransform, diagnosticInfo, sourceRootTransform);

            int localFallbackCount = 0;
            for (int i = 0; i < targetVertices.Length; i++)
            {
                Vector3 targetVertexWorldPos = targetRenderer.transform.TransformPoint(targetVertices[i]);
                BoneWeight bw = CalculateSurfaceWeights(targetVertexWorldPos, sourceRenderer, sourceVertices, sourceBoneWeights, commonOptions.searchRadius, commonOptions.maxBoneInfluence, ref localFallbackCount);
                newBoneWeights[i] = bw;
                NormalizeBoneWeight(ref newBoneWeights[i], commonOptions.maxBoneInfluence);
            }
            diagnosticInfo.fallbackUsedInSurfaceModeCount += localFallbackCount;


            Mesh newMesh = Object.Instantiate(targetMesh); // Create a new mesh instance to modify [3]
            newMesh.name = targetMesh.name + "_Weighted";

            newMesh.boneWeights = newBoneWeights;
            // Important: The indices in BoneWeight refer to the 'newBones' array.
            // We need to re-index bone weights if MapBones changed the bone order/count significantly
            // relative to sourceBones, or if CalculateSurfaceWeights directly returns indices from sourceBones.
            // The current CalculateSurfaceWeights returns bone indices relative to sourceRenderer.bones.
            // These need to be mapped to indices in newBones.

            BoneWeight[] reIndexedBoneWeights = new BoneWeight[newBoneWeights.Length];
            Dictionary<int, int> sourceToTargetBoneIndexMap = new Dictionary<int, int>();
            for(int i=0; i < newBones.Length; i++)
            {
                // Find this target bone (newBones[i]) in the original sourceBones list (if possible)
                // This is complex because newBones are from target armature.
                // The indices in newBoneWeights are *currently* referring to sourceRenderer.bones.
                // We need them to refer to targetRenderer.bones (which will be newBones).
            }

            // Create a map from source bone's original index to its new index in `newBones`
            // This is crucial if `CalculateSurfaceWeights` returns weights with indices from `sourceRenderer.bones`
            Dictionary<int, int> sourceBoneToNewBoneIndexMap = new Dictionary<int, int>();
            for (int i = 0; i < sourceRenderer.bones.Length; i++)
            {
                Transform sourceBone = sourceRenderer.bones[i];
                if (sourceBone == null) continue;

                // Find this source bone in the newBones array (which contains target armature bones)
                Transform correspondingTargetBone = FindTargetBone(sourceBone, sourceBoneMap, targetBoneMap, targetRootTransform, sourceRootTransform);
                if (correspondingTargetBone != null)
                {
                    int newIndex = System.Array.IndexOf(newBones, correspondingTargetBone);
                    if (newIndex != -1)
                    {
                        sourceBoneToNewBoneIndexMap[i] = newIndex;
                    }
                    // else: This source bone's corresponding target bone is not in the final newBones list.
                    // This can happen if a bone influences the source mesh but has no valid mapping or is not used by target.
                }
            }
            
            // Re-index the bone weights to use indices from the `newBones` array
            for (int i = 0; i < newBoneWeights.Length; i++)
            {
                BoneWeight originalBw = newBoneWeights[i];
                BoneWeight remappedBw = new BoneWeight();
                float totalW = 0;

                List<KeyValuePair<int, float>> influences = new List<KeyValuePair<int, float>>();

                if (originalBw.weight0 > 0 && sourceBoneToNewBoneIndexMap.TryGetValue(originalBw.boneIndex0, out int newIdx0)) { influences.Add(new KeyValuePair<int, float>(newIdx0, originalBw.weight0)); totalW += originalBw.weight0; }
                if (originalBw.weight1 > 0 && sourceBoneToNewBoneIndexMap.TryGetValue(originalBw.boneIndex1, out int newIdx1)) { influences.Add(new KeyValuePair<int, float>(newIdx1, originalBw.weight1)); totalW += originalBw.weight1; }
                if (originalBw.weight2 > 0 && sourceBoneToNewBoneIndexMap.TryGetValue(originalBw.boneIndex2, out int newIdx2)) { influences.Add(new KeyValuePair<int, float>(newIdx2, originalBw.weight2)); totalW += originalBw.weight2; }
                if (originalBw.weight3 > 0 && sourceBoneToNewBoneIndexMap.TryGetValue(originalBw.boneIndex3, out int newIdx3)) { influences.Add(new KeyValuePair<int, float>(newIdx3, originalBw.weight3)); totalW += originalBw.weight3; }
                
                influences = influences.OrderByDescending(kvp => kvp.Value).Take(commonOptions.maxBoneInfluence).ToList();

                if (influences.Count > 0) remappedBw.boneIndex0 = influences[0].Key; remappedBw.weight0 = (influences.Count > 0) ? influences[0].Value : 0;
                if (influences.Count > 1) remappedBw.boneIndex1 = influences[1].Key; remappedBw.weight1 = (influences.Count > 1) ? influences[1].Value : 0;
                if (influences.Count > 2) remappedBw.boneIndex2 = influences[2].Key; remappedBw.weight2 = (influences.Count > 2) ? influences[2].Value : 0;
                if (influences.Count > 3) remappedBw.boneIndex3 = influences[3].Key; remappedBw.weight3 = (influences.Count > 3) ? influences[3].Value : 0;
                
                NormalizeBoneWeight(ref remappedBw, commonOptions.maxBoneInfluence); // Normalize after re-indexing and potential reduction of influences
                reIndexedBoneWeights[i] = remappedBw;
            }
            newMesh.boneWeights = reIndexedBoneWeights;


            newMesh.bindposes = GenerateBindPoses(newBones, targetRenderer.transform, diagnosticInfo);

            if (commonOptions.keepMaterials)
            {
                targetRenderer.sharedMaterials = targetRenderer.sharedMaterials; // Re-assign to ensure they are kept
            }
            targetRenderer.sharedMesh = newMesh; // Set the new mesh [3]
            targetRenderer.bones = newBones;
            targetRenderer.ResetBounds(); // Recalculate bounding box [3]
            if (newBones.Length > 0 && newBones[0] != null) // Set root bone if possible
            {
                 // Try to find a sensible root bone. Often the first bone or a specific humanoid bone.
                 // For simplicity, if the newBones array is populated, the first bone can be a candidate,
                 // or better, if targetRootTransform itself is part of newBones, or one of its children.
                 // A common approach is to use the anscestor of all bones in newBones.
                 // Here, we could try to find the original root bone of targetRenderer if it exists,
                 // or the common ancestor of newBones. For now, let's try to find targetRootTransform itself or its child among newBones.
                 Transform newRootBone = targetRenderer.rootBone; // Keep existing if sensible
                 if (newBones.Contains(targetRootTransform)) newRootBone = targetRootTransform;
                 else if (newBones.Length > 0)
                 {
                    // A simple heuristic: find the highest bone in the hierarchy from newBones that is a child of targetRootTransform
                    Transform highestBone = null;
                    int highestDepth = int.MaxValue;
                    foreach(var bone in newBones)
                    {
                        if(bone == null || !bone.IsChildOf(targetRootTransform)) continue;
                        int depth = 0;
                        Transform current = bone;
                        while(current != targetRootTransform && current.parent != null)
                        {
                            current = current.parent;
                            depth++;
                        }
                        if(current == targetRootTransform && depth < highestDepth)
                        {
                            highestDepth = depth;
                            highestBone = bone;
                        }
                    }
                    if (highestBone != null) newRootBone = highestBone;
                    else if (newBones.Length > 0 && newBones[0] != null) newRootBone = newBones[0]; // Fallback to first bone
                 }
                 targetRenderer.rootBone = newRootBone; // [3]
            }


            EditorUtility.SetDirty(targetRenderer);
            EditorUtility.SetDirty(newMesh);
            Debug.Log($"Weights transferred to {targetRenderer.name} using {newMesh.name}. Target mesh now has {newBones.Length} bones.");
        }

        private static BoneWeight CalculateSurfaceWeights(Vector3 targetVertexWorldPos, SkinnedMeshRenderer sourceRenderer, Vector3[] sourceVertices, BoneWeight[] sourceBoneWeights, float searchRadius, int maxInfluences, ref int fallbackCount)
        {
            List<KeyValuePair<float, BoneWeight>> potentialWeights = new List<KeyValuePair<float, BoneWeight>>();
            float searchRadiusSq = searchRadius * searchRadius;

            Transform sourceTransform = sourceRenderer.transform; // Cache transform

            for (int j = 0; j < sourceVertices.Length; j++)
            {
                Vector3 sourceVertexWorldPos = sourceTransform.TransformPoint(sourceVertices[j]);
                float distSq = (targetVertexWorldPos - sourceVertexWorldPos).sqrMagnitude;

                if (distSq < searchRadiusSq && distSq > 0f) // Ensure distSq > 0 to avoid division by zero if radius is tiny
                {
                    // Inverse distance weighting for smoother results
                    float weight = 1.0f / distSq; // Closer points get higher weight
                    potentialWeights.Add(new KeyValuePair<float, BoneWeight>(weight, sourceBoneWeights[j]));
                }
                else if (distSq < searchRadiusSq && searchRadius == 0f && distSq == 0f) // Exact same position special case
                {
                     potentialWeights.Add(new KeyValuePair<float, BoneWeight>(float.MaxValue, sourceBoneWeights[j])); // Max weight
                }
            }

            if (potentialWeights.Count == 0) // Fallback: if no vertices in radius, find the absolute closest one
            {
                fallbackCount++;
                int closestVertIndex = -1;
                float minDistSq = float.MaxValue;
                for (int j = 0; j < sourceVertices.Length; j++)
                {
                    Vector3 sourceVertexWorldPos = sourceTransform.TransformPoint(sourceVertices[j]);
                    float distSq = (targetVertexWorldPos - sourceVertexWorldPos).sqrMagnitude;
                    if (distSq < minDistSq)
                    {
                        minDistSq = distSq;
                        closestVertIndex = j;
                    }
                }
                if (closestVertIndex != -1)
                {
                    // Return the closest vertex's weights directly, but ensure it's capped by maxInfluences
                    BoneWeight rawBw = sourceBoneWeights[closestVertIndex];
                    NormalizeBoneWeight(ref rawBw, maxInfluences); // Apply maxInfluences cap
                    return rawBw;
                }
                return new BoneWeight(); // Should not happen if sourceVertices has entries
            }

            Dictionary<int, float> summedWeights = new Dictionary<int, float>();
            float totalWeightSum = 0f;

            foreach (var pw in potentialWeights)
            {
                float contributingWeight = pw.Key;
                BoneWeight currentBw = pw.Value;
                AddInfluence(summedWeights, currentBw.boneIndex0, currentBw.weight0 * contributingWeight);
                AddInfluence(summedWeights, currentBw.boneIndex1, currentBw.weight1 * contributingWeight);
                AddInfluence(summedWeights, currentBw.boneIndex2, currentBw.weight2 * contributingWeight);
                AddInfluence(summedWeights, currentBw.boneIndex3, currentBw.weight3 * contributingWeight);
                totalWeightSum += contributingWeight; // Sum of inverse distances, or linear falloffs
            }
            
            // Normalize the summed weights
            if (totalWeightSum > 0) // Avoid division by zero
            {
                var keys = summedWeights.Keys.ToList();
                foreach (var key in keys)
                {
                    summedWeights[key] /= totalWeightSum;
                }
            }
            
            var sortedInfluences = summedWeights.OrderByDescending(pair => pair.Value).Take(maxInfluences).ToList();
            
            BoneWeight finalBw = new BoneWeight();
            AssignWeightsFromList(ref finalBw, sortedInfluences);
            // Normalization is implicitly handled by AssignWeightsFromList correctly summing to 1 if source was normalized
            // However, an explicit NormalizeBoneWeight call ensures it sums to 1 and respects maxInfluences.
            NormalizeBoneWeight(ref finalBw, maxInfluences); 

            return finalBw;
        }
        
        private static void AddInfluence(Dictionary<int, float> summedWeights, int boneIndex, float weight)
        {
            if (weight <= 0.00001f) return; // Ignore negligible weights
            if (summedWeights.ContainsKey(boneIndex))
            {
                summedWeights[boneIndex] += weight;
            }
            else
            {
                summedWeights[boneIndex] = weight;
            }
        }

        private static void AssignWeightsFromList(ref BoneWeight bw, List<KeyValuePair<int, float>> sortedInfluences)
        {
            bw.boneIndex0 = 0; bw.weight0 = 0;
            bw.boneIndex1 = 0; bw.weight1 = 0;
            bw.boneIndex2 = 0; bw.weight2 = 0;
            bw.boneIndex3 = 0; bw.weight3 = 0;

            if (sortedInfluences.Count > 0) { bw.boneIndex0 = sortedInfluences[0].Key; bw.weight0 = sortedInfluences[0].Value; }
            if (sortedInfluences.Count > 1) { bw.boneIndex1 = sortedInfluences[1].Key; bw.weight1 = sortedInfluences[1].Value; }
            if (sortedInfluences.Count > 2) { bw.boneIndex2 = sortedInfluences[2].Key; bw.weight2 = sortedInfluences[2].Value; }
            if (sortedInfluences.Count > 3) { bw.boneIndex3 = sortedInfluences[3].Key; bw.weight3 = sortedInfluences[3].Value; }
        }

        private static void NormalizeBoneWeight(ref BoneWeight bw, int maxInfluences)
        {
            List<KeyValuePair<int, float>> influences = new List<KeyValuePair<int, float>>();
            if (bw.weight0 > 0) influences.Add(new KeyValuePair<int, float>(bw.boneIndex0, bw.weight0));
            if (bw.weight1 > 0) influences.Add(new KeyValuePair<int, float>(bw.boneIndex1, bw.weight1));
            if (bw.weight2 > 0) influences.Add(new KeyValuePair<int, float>(bw.boneIndex2, bw.weight2));
            if (bw.weight3 > 0) influences.Add(new KeyValuePair<int, float>(bw.boneIndex3, bw.weight3));

            influences = influences.OrderByDescending(kvp => kvp.Value).Take(maxInfluences).ToList();

            float totalWeight = influences.Sum(kvp => kvp.Value);

            bw.weight0 = 0; bw.boneIndex0 = 0;
            bw.weight1 = 0; bw.boneIndex1 = 0;
            bw.weight2 = 0; bw.boneIndex2 = 0;
            bw.weight3 = 0; bw.boneIndex3 = 0;

            if (totalWeight == 0) {
                if (influences.Count > 0 && influences[0].Key >= 0) { // Ensure bone index is valid
                    bw.boneIndex0 = influences[0].Key;
                    bw.weight0 = 1.0f;
                } else if (influences.Count > 0) { // A bone was there but maybe index was bad, assign to bone 0 as last resort
                    // This case should ideally not be hit if mapping is correct.
                    // diagnosticInfo.AddWarning("Normalization resulted in zero total weight with influences present, but invalid bone index 0 assigned.");
                }
                return;
            }
            
            if (influences.Count > 0) { bw.boneIndex0 = influences[0].Key; bw.weight0 = influences[0].Value / totalWeight; }
            if (influences.Count > 1) { bw.boneIndex1 = influences[1].Key; bw.weight1 = influences[1].Value / totalWeight; }
            if (influences.Count > 2) { bw.boneIndex2 = influences[2].Key; bw.weight2 = influences[2].Value / totalWeight; }
            if (influences.Count > 3) { bw.boneIndex3 = influences[3].Key; bw.weight3 = influences[3].Value / totalWeight; }
        }

        private static Transform[] MapBones(Transform[] sourceRendererBones, Dictionary<string, Transform> fullSourceBoneMap, Dictionary<string, Transform> fullTargetBoneMap, Transform targetRoot, DiagnosticInfo diagnosticInfo, Transform sourceRoot)
        {
            // This list will store the bones from the TARGET armature that the TARGET mesh will use.
            // The indices of BoneWeights will refer to this array.
            List<Transform> newTargetBonesList = new List<Transform>();
            
            // Iterate through all bones that influence the SOURCE mesh.
            // For each source bone, find its corresponding bone in the TARGET armature.
            foreach (Transform sourceBone in sourceRendererBones)
            {
                if (sourceBone == null) continue; 

                Transform targetMappedBone = FindTargetBone(sourceBone, fullSourceBoneMap, fullTargetBoneMap, targetRoot, sourceRoot);
                
                if (targetMappedBone != null)
                {
                    if (!newTargetBonesList.Contains(targetMappedBone))
                    {
                        newTargetBonesList.Add(targetMappedBone);
                    }
                }
                else
                {
                    diagnosticInfo.AddWarning($"Source bone '{GetBonePath(sourceBone, sourceRoot)}' could not be mapped to any bone in the target armature. Weights associated with this bone might be lost or improperly distributed for the target mesh.");
                    diagnosticInfo.nullBonesInMappedBonesCount++;
                }
            }

            if (newTargetBonesList.Count == 0)
            {
                diagnosticInfo.AddWarning("MapBones: No bones were successfully mapped from source to target. The target mesh will likely not deform. Attempting to use target's root bone or first available bone as a fallback.");
                // Fallback: if target has an animator and a root bone for its SkinnedMeshRenderer, try using that.
                var targetAnimator = targetRoot.GetComponentInChildren<Animator>();
                SkinnedMeshRenderer existingTargetRenderer = targetRoot.GetComponentInChildren<SkinnedMeshRenderer>();
                if (existingTargetRenderer != null && existingTargetRenderer.rootBone != null)
                {
                    newTargetBonesList.Add(existingTargetRenderer.rootBone);
                    if(!newTargetBonesList.Contains(existingTargetRenderer.rootBone)) newTargetBonesList.Add(existingTargetRenderer.rootBone);
                    diagnosticInfo.AddWarning($"Fallback: Added root bone '{existingTargetRenderer.rootBone.name}' of an existing SkinnedMeshRenderer on target.");

                } else if (fullTargetBoneMap.Any()) {
                     newTargetBonesList.Add(fullTargetBoneMap.Values.First()); // Add first bone from target map
                     diagnosticInfo.AddWarning($"Fallback: Added first available bone '{fullTargetBoneMap.Values.First().name}' from target's bone map.");
                } else {
                     diagnosticInfo.AddWarning("Fallback failed: No suitable bone found on target to act as a default.");
                }
            }
            return newTargetBonesList.ToArray();
        }

        private static Transform FindTargetBone(Transform sourceBone, Dictionary<string, Transform> sourceBonePathMap, Dictionary<string, Transform> targetBonePathMap, Transform targetRoot, Transform sourceRoot)
        {
            string sourceBonePath = GetBonePath(sourceBone, sourceRoot); 

            // Try finding by the same hierarchical path
            if (!string.IsNullOrEmpty(sourceBonePath) && targetBonePathMap.TryGetValue(sourceBonePath, out Transform foundTargetBoneByPath))
            {
                return foundTargetBoneByPath;
            }
            
            // Fallback 1: Try finding by name only, anywhere in the target's bone map (values are Transforms)
            // This is less robust if names are not unique across hierarchy.
            foreach (var targetBoneEntry in targetBonePathMap) // targetBonePathMap values are Transforms
            {
                if (targetBoneEntry.Value.name == sourceBone.name)
                {
                    // Debug.LogWarning($"Bone '{sourceBonePath}' (name: {sourceBone.name}) not found by path in target, but a bone with the same name was found at path '{targetBoneEntry.Key}'. Using this.");
                    return targetBoneEntry.Value;
                }
            }
            return null; // Not found
        }

        private static Matrix4x4[] GenerateBindPoses(Transform[] bones, Transform meshTransform, DiagnosticInfo diagnosticInfo)
        {
            Matrix4x4[] bindPoses = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                {
                    // The bind pose is the inverse of the bone's world matrix, multiplied by the mesh's world matrix.
                    // This transforms vertices from mesh space to bone space (relative to the bone's pose at bind time).
                    bindPoses[i] = bones[i].worldToLocalMatrix * meshTransform.localToWorldMatrix;
                }
                else
                {
                    bindPoses[i] = Matrix4x4.identity; // Default for null bones
                    diagnosticInfo.nullBonesInBindPoseCount++;
                    // This warning is now part of the summary diagnostic log.
                    // Debug.LogWarning($"GenerateBindPoses: Null bone found at index {i}. Using identity matrix. This can cause mesh deformation issues (e.g. shrinking, vertices at origin).");
                }
            }
            return bindPoses;
        }

        private static string GetBonePath(Transform bone, Transform root)
        {
            if (bone == null || root == null) return "";
            if (bone == root) return bone.name;

            string path = bone.name;
            Transform current = bone.parent;
            while (current != null && current != root.parent) // Stop if we go above the avatar root's parent
            {
                if (current == root) // Reached the root
                {
                    path = current.name + "/" + path;
                    return path; 
                }
                path = current.name + "/" + path;
                current = current.parent;
            }
             // If loop finishes and current is null, it means 'root' was not an ancestor or was not handled correctly.
             // Or if current is root.parent, it means the bone is not under 'root'.
             // This function assumes 'bone' is a descendant of 'root' or 'root' itself.
            return path; // May return a path not starting with root if bone is not child of root.
        }

        private static Dictionary<string, Transform> GetAllBones(GameObject avatarRoot)
        {
            var bones = new Dictionary<string, Transform>();
            // Include inactive game objects as bones might be disabled.
            foreach (Transform bone in avatarRoot.GetComponentsInChildren<Transform>(true)) 
            {
                string path = GetBonePath(bone, avatarRoot.transform);
                if (!string.IsNullOrEmpty(path) && !bones.ContainsKey(path))
                {
                    bones[path] = bone;
                }
                // Fallback for name-only mapping if paths are problematic, but path should be primary.
                // This could overwrite if names are not unique, so be cautious.
                // if (!bones.ContainsKey(bone.name)) { bones[bone.name] = bone; }
            }
            return bones;
        }

        private static void BackupTarget(GameObject target)
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Backup", target.name + "_Backup.prefab", "prefab", "Please enter a file name to save the backup to.");
            if (string.IsNullOrEmpty(path)) return;

            GameObject prefabInstanceToSave;
            // Check if target is an asset or a scene instance
            if (PrefabUtility.IsPartOfPrefabAsset(target))
            {
                 prefabInstanceToSave = Object.Instantiate(target); // Instantiate if it's an asset to avoid modifying original directly via this path
            } else {
                 prefabInstanceToSave = target; // If it's a scene object, we can try to save it directly or instantiate
                 // For safety with scene objects, instantiating is often better to avoid issues with existing prefab connections.
                 // However, if it's *not* a prefab instance, instantiating and then saving creates a *new* prefab.
                 // If it *is* a prefab instance in the scene, SaveAsPrefabAsset will create a new prefab asset from its current state.
                 // Let's consistently instantiate to create a clean backup.
                 prefabInstanceToSave = Object.Instantiate(target);
                 prefabInstanceToSave.name = target.name; // Remove "(Clone)"
            }
            
            PrefabUtility.SaveAsPrefabAsset(prefabInstanceToSave, path, out bool success);
            
            if (PrefabUtility.IsPartOfPrefabAsset(target) || prefabInstanceToSave != target) // Only destroy if we instantiated it
            {
                 Object.DestroyImmediate(prefabInstanceToSave);
            }


            if (success)
            {
                Debug.Log("Backup saved to: " + path);
            }
            else
            {
                Debug.LogError("Failed to save backup.");
            }
        }
    }
}
