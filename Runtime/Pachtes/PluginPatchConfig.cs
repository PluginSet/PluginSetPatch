using PluginSet.Core;
using UnityEngine;

namespace PluginSet.Patch
{
    public class PluginPatchConfig: ScriptableObject
    {
        public bool ContinueIfUpdateFail;
        
#if UNITY_EDITOR
        public PathInfo[] StreamPaths;
        public PatchInfo[] Patches;
#endif
    }
}