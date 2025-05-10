using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace WeightSyncPro
{
    public enum TransferMode
    {
        Surface = 0,
    }

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

    public class WeightSyncProWindow : EditorWindow
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

        [MenuItem("Window/WeightSyncPro")]
        public static void ShowWindow()
        {
            GetWindow<WeightSyncProWindow>("WeightSyncPro");
        }

        void OnGUI()
        {
            GUILayout.Label("Weight Sync Pro", EditorStyles.boldLabel);
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
            searchRadius = EditorGUILayout.Slider("Search Radius", searchRadius, 0.001f, 0.5f);
            maxBoneInfluence = EditorGUILayout.IntSlider("Max Bone Influences", maxBoneInfluence, 1, 8);

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

                    WeightTransferCore.TransferWeights(sourceAvatar, targetAvatar, sourceRenderer, targetRenderer, options);
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

    public static class WeightTransferCore
    {
        public static void TransferWeights(GameObject sourceRoot, GameObject targetRoot, 
                                           SkinnedMeshRenderer specificSourceRenderer, SkinnedMeshRenderer specificTargetRenderer,
                                           WeightSyncProWindow.TransferOptions options)
        {
            DiagnosticInfo diagnosticInfo = new DiagnosticInfo();

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
                var sourceHumanBones = WeightSyncProWindow.GetHumanoidBones(sourceRoot);
                var targetHumanBones = WeightSyncProWindow.GetHumanoidBones(targetRoot);

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

            diagnosticInfo.LogDiagnostics();
        }

        private static void CheckTransformScaleRecursive(Transform t, string objectName, DiagnosticInfo diagnostics)
        {
            if (t == null) return;
            
            if (t.localScale != Vector3.one)
            {
                diagnostics.AddWarning($"Scale of '{t.name}' (part of {objectName}) is {t.localScale}, not (1,1,1). This can affect mesh size and deformation during weight transfer, especially bind pose calculation.");
            }
        }

        private static void ProcessRendererPair(SkinnedMeshRenderer sourceRenderer, SkinnedMeshRenderer targetRenderer,
                                         Dictionary<string, Transform> sourceBoneMap, Dictionary<string, Transform> targetBoneMap,
                                         WeightSyncProWindow.TransferOptions commonOptions,
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

            Mesh newMesh = Object.Instantiate(targetMesh);
            newMesh.name = targetMesh.name + "_Weighted";

            Dictionary<int, int> sourceBoneToNewBoneIndexMap = new Dictionary<int, int>();
            for (int i = 0; i < sourceRenderer.bones.Length; i++)
            {
                Transform sourceBone = sourceRenderer.bones[i];
                if (sourceBone == null) continue;

                Transform correspondingTargetBone = FindTargetBone(sourceBone, sourceBoneMap, targetBoneMap, targetRootTransform, sourceRootTransform);
                if (correspondingTargetBone != null)
                {
                    int newIndex = System.Array.IndexOf(newBones, correspondingTargetBone);
                    if (newIndex != -1)
                    {
                        sourceBoneToNewBoneIndexMap[i] = newIndex;
                    }
                }
            }
            
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
                
                NormalizeBoneWeight(ref remappedBw, commonOptions.maxBoneInfluence);
                reIndexedBoneWeights[i] = remappedBw;
            }
            newMesh.boneWeights = reIndexedBoneWeights;

            newMesh.bindposes = GenerateBindPoses(newBones, targetRenderer.transform, diagnosticInfo);

            if (commonOptions.keepMaterials)
            {
                targetRenderer.sharedMaterials = targetRenderer.sharedMaterials;
            }
            targetRenderer.sharedMesh = newMesh;
            targetRenderer.bones = newBones;
            targetRenderer.ResetBounds();
            if (newBones.Length > 0 && newBones[0] != null)
            {
                 Transform newRootBone = targetRenderer.rootBone;
                 if (newBones.Contains(targetRootTransform)) newRootBone = targetRootTransform;
                 else if (newBones.Length > 0)
                 {
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
                    else if (newBones.Length > 0 && newBones[0] != null) newRootBone = newBones[0];
                 }
                 targetRenderer.rootBone = newRootBone;
            }

            EditorUtility.SetDirty(targetRenderer);
            EditorUtility.SetDirty(newMesh);
            Debug.Log($"Weights transferred to {targetRenderer.name} using {newMesh.name}. Target mesh now has {newBones.Length} bones.");
        }

        private static BoneWeight CalculateSurfaceWeights(Vector3 targetVertexWorldPos, SkinnedMeshRenderer sourceRenderer, Vector3[] sourceVertices, BoneWeight[] sourceBoneWeights, float searchRadius, int maxInfluences, ref int fallbackCount)
        {
            List<KeyValuePair<float, BoneWeight>> potentialWeights = new List<KeyValuePair<float, BoneWeight>>();
            float searchRadiusSq = searchRadius * searchRadius;

            Transform sourceTransform = sourceRenderer.transform;

            for (int j = 0; j < sourceVertices.Length; j++)
            {
                Vector3 sourceVertexWorldPos = sourceTransform.TransformPoint(sourceVertices[j]);
                float distSq = (targetVertexWorldPos - sourceVertexWorldPos).sqrMagnitude;

                if (distSq < searchRadiusSq && distSq > 0f)
                {
                    float weight = 1.0f / distSq;
                    potentialWeights.Add(new KeyValuePair<float, BoneWeight>(weight, sourceBoneWeights[j]));
                }
                else if (distSq < searchRadiusSq && searchRadius == 0f && distSq == 0f)
                {
                     potentialWeights.Add(new KeyValuePair<float, BoneWeight>(float.MaxValue, sourceBoneWeights[j]));
                }
            }

            if (potentialWeights.Count == 0)
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
                    BoneWeight rawBw = sourceBoneWeights[closestVertIndex];
                    NormalizeBoneWeight(ref rawBw, maxInfluences);
                    return rawBw;
                }
                return new BoneWeight();
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
                totalWeightSum += contributingWeight;
            }
            
            if (totalWeightSum > 0)
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
            NormalizeBoneWeight(ref finalBw, maxInfluences); 

            return finalBw;
        }
        
        private static void AddInfluence(Dictionary<int, float> summedWeights, int boneIndex, float weight)
        {
            if (weight <= 0.00001f) return;
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
                if (influences.Count > 0 && influences[0].Key >= 0) {
                    bw.boneIndex0 = influences[0].Key;
                    bw.weight0 = 1.0f;
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
            List<Transform> newTargetBonesList = new List<Transform>();
            
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
                var targetAnimator = targetRoot.GetComponentInChildren<Animator>();
                SkinnedMeshRenderer existingTargetRenderer = targetRoot.GetComponentInChildren<SkinnedMeshRenderer>();
                if (existingTargetRenderer != null && existingTargetRenderer.rootBone != null)
                {
                    newTargetBonesList.Add(existingTargetRenderer.rootBone);
                    if(!newTargetBonesList.Contains(existingTargetRenderer.rootBone)) newTargetBonesList.Add(existingTargetRenderer.rootBone);
                    diagnosticInfo.AddWarning($"Fallback: Added root bone '{existingTargetRenderer.rootBone.name}' of an existing SkinnedMeshRenderer on target.");

                } else if (fullTargetBoneMap.Any()) {
                     newTargetBonesList.Add(fullTargetBoneMap.Values.First());
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

            if (!string.IsNullOrEmpty(sourceBonePath) && targetBonePathMap.TryGetValue(sourceBonePath, out Transform foundTargetBoneByPath))
            {
                return foundTargetBoneByPath;
            }
            
            foreach (var targetBoneEntry in targetBonePathMap)
            {
                if (targetBoneEntry.Value.name == sourceBone.name)
                {
                    return targetBoneEntry.Value;
                }
            }
            return null;
        }

        private static Matrix4x4[] GenerateBindPoses(Transform[] bones, Transform meshTransform, DiagnosticInfo diagnosticInfo)
        {
            Matrix4x4[] bindPoses = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                {
                    bindPoses[i] = bones[i].worldToLocalMatrix * meshTransform.localToWorldMatrix;
                }
                else
                {
                    bindPoses[i] = Matrix4x4.identity;
                    diagnosticInfo.nullBonesInBindPoseCount++;
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
            while (current != null && current != root.parent)
            {
                if (current == root)
                {
                    path = current.name + "/" + path;
                    return path; 
                }
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        private static Dictionary<string, Transform> GetAllBones(GameObject avatarRoot)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (Transform bone in avatarRoot.GetComponentsInChildren<Transform>(true)) 
            {
                string path = GetBonePath(bone, avatarRoot.transform);
                if (!string.IsNullOrEmpty(path) && !bones.ContainsKey(path))
                {
                    bones[path] = bone;
                }
            }
            return bones;
        }

        private static void BackupTarget(GameObject target)
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Backup", target.name + "_Backup.prefab", "prefab", "Please enter a file name to save the backup to.");
            if (string.IsNullOrEmpty(path)) return;

            GameObject prefabInstanceToSave;
            if (PrefabUtility.IsPartOfPrefabAsset(target))
            {
                 prefabInstanceToSave = Object.Instantiate(target);
            } else {
                 prefabInstanceToSave = Object.Instantiate(target);
                 prefabInstanceToSave.name = target.name;
            }
            
            PrefabUtility.SaveAsPrefabAsset(prefabInstanceToSave, path, out bool success);
            
            if (PrefabUtility.IsPartOfPrefabAsset(target) || prefabInstanceToSave != target)
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
