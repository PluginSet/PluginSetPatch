using PluginSet.Core;
using PluginSet.Core.Editor;

namespace PluginSet.TIM.Editor
{
    [BuildTools]
    public static class BuildTIMTools
    {
        [OnSyncEditorSetting]
        public static void OnSyncEditorSetting(BuildProcessorContext context)
        {
            var buildParams = context.BuildChannels.Get<BuildTIMParams>("TIM");
            if (buildParams.Enable)
            {
                Global.CheckGitLibImported("com.tencent.imsdk.unity",
                    "https://github.com/LyneXiao/TIMSDK.git#1.7.6");
                context.Symbols.Add("ENABLE_TIM");
                context.AddLinkAssembly("PluginSet.TIM");
            }
                
            var pluginConfig = context.Get<PluginSetConfig>("pluginsConfig");
            var config = pluginConfig.Get<PluginTIMConfig>("TIM");
            config.AppId = buildParams.AppId;
        }
    }
}
