using UnityEditor;
using PluginSet.Core;
using UnityEngine;

namespace PluginSet.Patch.Editor
{
    [InitializeOnLoad]
    public static class EditorResourceManagerInit
    {
        static EditorResourceManagerInit()
        {
            ResourcesManager.NewInstance<PatchResourcesManager>();
        }
    }
}