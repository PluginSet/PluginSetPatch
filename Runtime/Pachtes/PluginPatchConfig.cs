using UnityEngine;

namespace PluginSet.Patch
{
    public class PluginPatchConfig: ScriptableObject
    {
        public bool ContinueIfUpdateFail;
        
#if UNITY_EDITOR
        public PathInfo[] StreamPaths;
        public string[] StreamingExtendPaths;
        public PatchInfo[] Patches;
#endif
    }
}