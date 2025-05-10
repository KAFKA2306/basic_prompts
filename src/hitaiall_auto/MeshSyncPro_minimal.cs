using UnityEngine;
using UnityEditor;
public class WeightSyncPro : EditorWindow
{
    SkinnedMeshRenderer src, dst;
    [MenuItem("Tools/WeightSyncPro")]
    static void Open() => GetWindow<WeightSyncPro>();
    void OnGUI()
    {
        src = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Source", src, typeof(SkinnedMeshRenderer), true);
        dst = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Target", dst, typeof(SkinnedMeshRenderer), true);
        if (GUILayout.Button("Transfer") && src && dst) Transfer();
    }
    void Transfer()
    {
        var sM = src.sharedMesh; var dM = dst.sharedMesh;
        var sV = sM.vertices; var dV = dM.vertices;
        var sBW = sM.boneWeights; var dBW = new BoneWeight[dV.Length];
        var sB = src.bones; var dB = dst.bones;
        System.Collections.Generic.Dictionary<int, int> map = new();
        for (int i = 0; i < sB.Length; i++) for (int j = 0; j < dB.Length; j++)
            if (sB[i].name == dB[j].name) { map[i] = j; break; }
        for (int i = 0; i < dV.Length; i++)
        {
            int min = 0; float md = float.MaxValue;
            for (int j = 0; j < sV.Length; j++)
            {
                float dj = (dst.transform.TransformPoint(dV[i]) - src.transform.TransformPoint(sV[j])).sqrMagnitude;
                if (dj < md) { md = dj; min = j; }
            }
            var bw = sBW[min];
            dBW[i].boneIndex0 = map.ContainsKey(bw.boneIndex0) ? map[bw.boneIndex0] : 0; dBW[i].weight0 = bw.weight0;
            dBW[i].boneIndex1 = map.ContainsKey(bw.boneIndex1) ? map[bw.boneIndex1] : 0; dBW[i].weight1 = bw.weight1;
            dBW[i].boneIndex2 = map.ContainsKey(bw.boneIndex2) ? map[bw.boneIndex2] : 0; dBW[i].weight2 = bw.weight2;
            dBW[i].boneIndex3 = map.ContainsKey(bw.boneIndex3) ? map[bw.boneIndex3] : 0; dBW[i].weight3 = bw.weight3;
        }
        var nm = Instantiate(dM); nm.boneWeights = dBW; dst.sharedMesh = nm;
        EditorUtility.SetDirty(dst);
    }
}
