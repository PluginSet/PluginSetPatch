using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PluginSet.Core;
using PluginSet.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace PluginSet.Patch.Editor
{
    public enum PatchDependsBuildType
    {
        BuildWithDirectory,
        BuildWithSingleFile,
        BuildWithReference,
    }
    
    [BuildTools]
    [BuildChannelsParams("Patch", -3000, "热更相关配置")]
    public class BuildPatchParams: ScriptableObject
    {
        [Tooltip("勾选后不开启PluginPatchUpdate")]
        public bool DisablePatchUpdate;
        
        [Tooltip("更新取消或下载换败后是否能继续游戏")]
        public bool ContinueIfUpdateFail;

        [Tooltip("允许能过资源名称加载资源，勾选后，需确保同一个bundle中不同文件夹下确保不能有相同名称相同类型的资源")]
        public bool EnableLoadAssetWithName;

        [Tooltip("允许在打热更包时将子包子资源拷贝到Stream目录")]
        public bool EnableCopyToStreamWhenBuildUpdatePatches;

        [Tooltip("构建热更资源时检查Resources中的资源")]
        public bool CheckResourcesWhenBuildPatch;

            [Tooltip("主包资源数据")]
//        [FoldersDrag("Path", "StreamingPaths")]
#if UNITY_2020_2_OR_NEWER
        [NonReorderable]
#endif
        public PathInfo[] StreamingPaths;

        [Tooltip("子包资源数据")]
#if UNITY_2020_2_OR_NEWER
        [NonReorderable]
#endif
        public PatchInfo[] Patches;

        [Tooltip("依赖资源构建方式：\n1. BuildWithDirectory 分文件目录构建\n2. 单个文件构建\n3. 按照引用构建")]
        public PatchDependsBuildType BuildType = PatchDependsBuildType.BuildWithDirectory;
        
        [OnSyncEditorSetting]
        public static void SyncExportSetting(BuildProcessorContext context)
        {
            context.AddLinkAssembly("PluginSet.Patch");
            
            var buildParams = context.BuildChannels.Get<BuildPatchParams>();
            if (buildParams.DisablePatchUpdate)
                context.Symbols.Add("DISABLE_PATCH_UPDATE");
            if (buildParams.EnableLoadAssetWithName)
                context.Symbols.Add("ENABLE_LOAD_ASSET_WITH_NAME");
            if (buildParams.CheckResourcesWhenBuildPatch)
                context.Symbols.Add("CHECK_RESOURCES_WHEN_BUILD_PATCH");
            
            var pluginConfig = context.Get<PluginSetConfig>("pluginsConfig");
            var config = pluginConfig.AddConfig<PluginPatchConfig>("Patch");
            config.ContinueIfUpdateFail = buildParams.ContinueIfUpdateFail;
            config.StreamPaths = buildParams.StreamingPaths;
            config.Patches = buildParams.Patches;
        }


        private void OnEnable()
        {
            if (EditorApplication.isUpdating)
                return;
            
            FixOldVersionValue();
        }

        private void FixOldVersionValue()
        {
            bool dirty = FixPathInfo(StreamingPaths);
            if (FixPatchInfo(Patches))
                dirty = true;

            if (dirty)
            {
                EditorUtility.SetDirty(this);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private bool FixPathInfo(PathInfo[] pathInfos)
        {
            if (pathInfos == null)
                return false;
            
            bool dirty = false;
            for (int i = 0; i < pathInfos.Length; i++)
            {
                var info = pathInfos[i];
                if (info.BuildByFile)
                {
                    dirty = true;
                    info.BuildByFile = false;
                    if (info.BuildType == PathBuildType.AllInBundle)
                        info.BuildType = PathBuildType.AllDirectories;

                    pathInfos[i] = info;
                }
            }

            return dirty;
        }

        private bool FixPatchInfo(PatchInfo[] patchInfos)
        {
            if (patchInfos == null)
                return false;
            
            bool dirty = false;
            for (int i = 0; i < patchInfos.Length; i++)
            {
                var info = patchInfos[i];
                if (info.Ignore)
                    continue;
                
                var pathInfos = info.Paths;
                if (FixPathInfo(pathInfos))
                {
                    dirty = true;
                    info.Paths = pathInfos;
                    patchInfos[i] = info;
                }
            }

            return dirty;
        }
    }
}