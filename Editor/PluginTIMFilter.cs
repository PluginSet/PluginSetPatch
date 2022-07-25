
using PluginSet.Core.Editor;
using UnityEditor;

namespace PluginSet.TIM.Editor
{
    [InitializeOnLoad]
    public static class PluginTIMFilter
    {
        static PluginTIMFilter()
        {
            var fileter = PluginFilter.IsBuildParamsEnable<BuildTIMParams>();
            PluginFilter.RegisterFilter("com.tencent.imsdk.unity/Plugins/Android/libs/arm64-v8a", fileter);
            PluginFilter.RegisterFilter("com.tencent.imsdk.unity/Plugins/Android/libs/armeabi-v7a", fileter);
            PluginFilter.RegisterFilter("com.tencent.imsdk.unity/Plugins/Android/libs/x86", fileter);
            PluginFilter.RegisterFilter("com.tencent.imsdk.unity/Plugins/Android/libs/x86_64", fileter);
            PluginFilter.RegisterFilter("com.tencent.imsdk.unity/Plugins/iOS", fileter);
            PluginFilter.RegisterFilter("com.tencent.imsdk.unity/Plugins/MacOS", fileter);
            PluginFilter.RegisterFilter("com.tencent.imsdk.unity/Plugins/Metro/lib/Win32", fileter);
            PluginFilter.RegisterFilter("com.tencent.imsdk.unity/Plugins/Metro/lib/Win64", fileter);
        }
    }
}
