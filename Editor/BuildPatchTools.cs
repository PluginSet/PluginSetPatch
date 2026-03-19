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
    [BuildTools]
    public static class BuildPatchTools
    {
        private static readonly string[] SupportAudioTypes = new[] { ".mp3", ".wav", ".ogg", ".mp4" };

        private static bool IsAudioFile(string file)
        {
            return SupportAudioTypes.Contains(Path.GetExtension(file));
        }
        
        private static void CollectAllAudioFiles(BuildProcessorContext context, string patchName, in PathInfo[] paths)
        {
            var fileMap = new Dictionary<string, FileBundleInfo>();
            var bundleInfos = new Dictionary<string, BundleInfo>();
            var soundPaths = paths.Where(path => path.AudioPatchType != AudioPatchType.None).ToArray();
            foreach (var ext in SupportAudioTypes)
            {
                CollectFileMap(context, patchName, soundPaths, new List<string>(), ref fileMap, ref bundleInfos, new Dictionary<string, string>(), $"*{ext}");
            }
        }
        
        [OnSyncEditorSetting]
        public static void OnSyncEditorSettingFirst(BuildProcessorContext context)
        {
            if (!context.IsSupportAudioPatch())
                return;
                
            var buildParams = context.BuildChannels.Get<BuildPatchParams>();
            CollectAllAudioFiles(context, context.StreamingAssetsName, buildParams.StreamingPaths);
            foreach (var info in buildParams.Patches)
            {
                if (info.Ignore)
                    continue;
                
                CollectAllAudioFiles(context, info.Name, info.Paths);
            }
        }
        
        [CheckRebuildAssetBundles]
        public static bool CheckRebuildAssetBundles(BuildProcessorContext context)
        {
            var streamName = context.StreamingAssetsName;
            if (string.IsNullOrEmpty(streamName))
                return false;
            
            var streamPath = context.StreamingAssetsPath;
            if (!Directory.Exists(streamPath))
                return true;

            var streamFile = Path.Combine(streamPath, streamName);
            if (!File.Exists(streamFile))
                return true;
            
            var version = PluginUtil.GetVersionString(context.VersionName, int.Parse(context.Build));
            var fileManifest = FileManifest.LoadFileManifest(streamName, streamFile);
            if (!version.Equals(fileManifest.Version))
                return true;

            if (string.IsNullOrEmpty(context.ResourceVersion))
                return false;
            
            return !fileManifest.Tag.Equals(context.ResourceVersion);
        }
            
        
        [AssetBundleFilePathsCollectorAttribute(int.MinValue)]
        public static void CollectAssetBundleFilePaths(BuildProcessorContext context, string bundleName,
            List<string> result)
        {
            var buildParams = context.BuildChannels.Get<BuildPatchParams>();
            foreach (var path in buildParams.StreamingPaths.Concat(buildParams.Patches.Where(patch=>!patch.Ignore).SelectMany(patch => patch.Paths)))
            {
                if (path.UseResourceLoad)
                    continue;

                var pathName = Path.GetFileName(path.Path)?.ToLower();
                if (string.IsNullOrEmpty(path.BundleName))
                {
                    if (bundleName.ToLower().Equals(pathName))
                        result.Add(path.Path);
                } else if (bundleName.ToLower().Equals(string.Format(path.BundleName, pathName).ToLower()))
                {
                    result.Add(path.Path);
                }
            }
        }
        
        [OnBuildBundles(int.MinValue)]
        [OnBuildPatches(int.MinValue)]
        public static void CollectInvalidAssets(BuildProcessorContext context)
        {
            var buildParams = context.BuildChannels.Get<BuildPatchParams>();
            context.Set("streamingPaths", buildParams.StreamingPaths);
            // 定义依赖资源名称对应的bundle名称
            context.Set("depFileBundleName", new Dictionary<string, string>());
            
            var invalidList = context.TryGet<List<string>>("invalidAssets", new List<string>());
            var files = Global.GetRelyableFiles(Path.Combine(Application.dataPath, "Resources"));
            foreach (var file in files)
            {
                var subPath = Global.GetSubPath(".", file);
                var deps = AssetDatabase.GetDependencies(subPath, true);
                if (deps != null)
                {
                    foreach (var dep in deps)
                    {
                        var fullPath = Global.GetFullPath(dep);
                        if (!invalidList.Contains(fullPath))
                            invalidList.Add(fullPath);
                    }
                }
            }
            
            context.Set("invalidAssets", invalidList);
        }
        
        [OnBuildPatches(int.MinValue + 1)]
        public static void CalculateAllModify(BuildProcessorContext context)
        {
            var modFiles = context.Get<List<string>>("UpdatePatchModFiles");
#if CHECK_RESOURCES_WHEN_BUILD_PATCH
            foreach (var file in Global.GetAllModifyFiles(modFiles.ToArray()))
            {
                if (!modFiles.Contains(file))
                    modFiles.Add(file);
            }
#endif
            
            var invalidList = context.TryGet<List<string>>("invalidAssets", null);
            invalidList?.RemoveAll(file => modFiles.Contains(file));
        }

        [OnBuildBundles(-1000)]
        [OnBuildPatches(-1000)]
        public static void BuildPatches(BuildProcessorContext context)
        {
            // 子包打包时不再响应该方法
            if (context.IsBuildingPatches())
                return;

            // 对已修改文件名称的构建后文件作记录，防止二次修改
            var map = context.TryGet<Dictionary<string, FileManifest.FileInfo>>("buildFileInfoMap", null);
            if (map == null)
            {
                map = new Dictionary<string, FileManifest.FileInfo>();
                context.Set("buildFileInfoMap", map);
            }

            var isBuildUpdatePatch = context.IsBuildingUpdatePatches();
            var buildParams = context.BuildChannels.Get<BuildPatchParams>();
            
            context.BuildPatchesStart();
            var subPatches = new List<string>();
            var allPatches = new List<string>();

#region BuildPatches
            var patchPath = Path.Combine(Application.dataPath, "Patches");
            var fileMap = new Dictionary<string, FileBundleInfo>();
            var bundleInfos = new Dictionary<string, BundleInfo>();
            var version = PluginUtil.GetVersionString(context.VersionName, int.Parse(context.Build));
            
            var invalidList = context.TryGet<List<string>>("invalidAssets", new List<string>());
            var depFileBundleName = context.TryGet("depFileBundleName", new Dictionary<string, string>());
            var streamName = context.StreamingAssetsName;
            var streamPath = context.StreamingAssetsPath;
            if (string.IsNullOrEmpty(streamPath))
                return;
            
            if (!Directory.Exists(streamPath))
                Directory.CreateDirectory(streamPath);
            
            FileBundleInfo.BuildType = buildParams.BuildType;
            PatchInfo[] patchInfos = buildParams.Patches;
            if (patchInfos != null)
            {
                foreach (var info in patchInfos)
                {
                    if (info.Ignore)
                        continue;
                    
                    FileBundleInfo.TempBundleMap.Clear();
                    fileMap.Clear();
                    bundleInfos.Clear();
                    var buildPaths = new List<string>(info.Paths.Select(path => path.Path));
                    context.SetBuildPaths(buildPaths);
                    
                    Global.CallCustomOrderMethods<OnBuildBundlesAttribute, BuildToolsAttribute>(context);

                    var infoPaths = info.Paths.Where(path => buildPaths.Contains(path.Path));
                    CollectFileMap(context, info.Name, infoPaths.ToArray(), invalidList, ref fileMap, ref bundleInfos, depFileBundleName);
                    
                    context.Set<BundleInfo[]>("patchBundleRevertInfo", bundleInfos.Values.ToArray());

                    foreach (var bundleName in AssetDatabase.GetAllAssetBundleNames())
                    {
                        if (string.IsNullOrEmpty(bundleName))
                            continue;

                        var files = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
                        CollectFileMapWithFiles(context, info.Name, bundleName, files, string.Empty, invalidList, ref fileMap, ref bundleInfos, depFileBundleName, AudioPatchType.None);
                    }
                    
                    foreach (var kv in fileMap)
                    {
                        context.AddBuildBundle(kv.Value.BundleName, kv.Key);
                    }

                    var exportPath = Path.Combine(patchPath, info.Name);
                    Global.CheckAndDeletePath(exportPath);
                    var manifest = context.BuildAssetBundle(exportPath, info.Name);

                    var copyToStream = info.CopyToStream &&
                                       (!isBuildUpdatePatch || buildParams.EnableCopyToStreamWhenBuildUpdatePatches);
                    
                    Global.CallCustomOrderMethods<OnBuildBundlesCompletedAttribute, BuildToolsAttribute>(context, exportPath, info.Name, manifest, !copyToStream);
                    
                    if (manifest == null)
                        continue;

                    if (copyToStream)
                    {
                        subPatches.Add(info.Name);
                        Global.MoveAllFilesToPath(exportPath, streamPath);
                    }
                    else if (!string.IsNullOrEmpty(context.BuildPath))
                    {
                        var patchesPath = Path.Combine(context.BuildPath, "Patches");
                        context.SetBuildResult("patchesPath", Path.GetFullPath(patchesPath));
                        Global.MoveAllFilesToPath(exportPath, patchesPath);
                    }
                    allPatches.Add(info.Name);
                }
                
                context.Set<string[]>("internalSubPatches", subPatches.ToArray());

                #endregion

            }
            
            context.SetBuildResult("patchesName", allPatches);

            FileBundleInfo.TempBundleMap.Clear();
            fileMap.Clear();
            bundleInfos.Clear();
            
            var modFiles = context.TryGet<List<string>>("UpdatePatchModFiles", null);
            var addFiles = context.TryGet<List<string>>("UpdatePatchAddFiles", null);
            CollectResources(context, modFiles, invalidList, ref fileMap, ref bundleInfos, depFileBundleName);
            CollectResources(context, addFiles, invalidList, ref fileMap, ref bundleInfos, depFileBundleName);
            context.SetBuildResult("CollectedResources", string.Join(",", fileMap.Keys));

            foreach (var bundleName in AssetDatabase.GetAllAssetBundleNames())
            {
                if (string.IsNullOrEmpty(bundleName))
                    continue;

                var files = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
                CollectFileMapWithFiles(context, streamName, bundleName, files, string.Empty, invalidList, ref fileMap, ref bundleInfos, depFileBundleName, AudioPatchType.None);
            }

            var streamPaths = context.TryGet("streamingPaths", buildParams.StreamingPaths);
            CollectFileMap(context, streamName, streamPaths, invalidList, ref fileMap, ref bundleInfos, depFileBundleName);

            context.Set<BundleInfo[]>("patchBundleRevertInfo", bundleInfos.Values.ToArray());

            foreach (var kv in fileMap)
            {
                context.AddBuildBundle(kv.Value.BundleName, kv.Key);
            }

            context.BuildPatchesEnded();
            AssetDatabase.Refresh();
        }
        
        [OnBuildBundlesCompleted(9999999)]
        public static void OnBuildBundlesCompleted(BuildProcessorContext context, string streamingPath,
            string streamingName, AssetBundleManifest manifest, bool patchBundle = false)
        {
            var bundleInfos = context.TryGet<BundleInfo[]>("patchBundleRevertInfo", null);
            // 还原bundleInfo
            if (bundleInfos != null && bundleInfos.Length > 0)
            {
                foreach (var info in bundleInfos)
                {
                    Global.RevertFileBundleInfo(info);
                }
            }
            
            PackBundlesFileInfo(context, manifest, streamingPath, streamingName);

            if (context.IsBuildingPatches())
                return;

            var list = context.GetStreamingExtendFiles();
            if (list == null)
                return;

            // 在PackBundlesFileInfo中已经处理了所有文件
            // FileManifest.AppendFiles(streamingPath, streamingName, list.ToArray());
            list.Clear();
        }
        
        [BuildProjectCompleted(int.MaxValue)]
        public static void OnProjectBuildCompleted(BuildProcessorContext context, string exportPath)
        {
            var list = context.GetStreamingExtendFiles();
            if (list == null)
                return;

            var assetsPath = Global.GetProjectAssetsPath(context.BuildTarget, exportPath);
            if (string.IsNullOrEmpty(assetsPath))
                return;
            
            var streamingName = context.StreamingAssetsName;
            FileManifest.AppendFiles(assetsPath, streamingName, list.ToArray());
            list.Clear();
        }
        
        private static void PackBundlesFileInfo(BuildProcessorContext context, AssetBundleManifest manifest, string streamingPath, string streamingName)
        {
            var version = PluginUtil.GetVersionString(context.VersionName, int.Parse(context.Build));
            var subPatches = context.TryGet<string[]>("internalSubPatches", null);
            var map = context.TryGet("buildFileInfoMap",new Dictionary<string, FileManifest.FileInfo>());
            FileManifest.AppendFileInfo(manifest, streamingPath, streamingName, version , ref map, subPatches, context.ResourceVersion);
        }

        private static void CollectFileMap(BuildProcessorContext context, string patchName, in PathInfo[] paths,
            in List<string> ignoreFiles,
            ref Dictionary<string, FileBundleInfo> fileMap,
            ref Dictionary<string, BundleInfo> bundleInfos,
            in Dictionary<string, string> defFileBundleName,
            string searchPattern = "*.*")
        {
            if (paths == null || paths.Length <= 0)
                return;
            
            foreach (var pathInfo in paths)
            {
                var path = pathInfo.Path;
                switch (pathInfo.BuildType)
                {
                    case PathBuildType.AllInBundle:
                    {
                        var bundleName = Path.GetFileName(path)?.ToLower();
                        if (!string.IsNullOrEmpty(pathInfo.BundleName))
                            bundleName = string.Format(pathInfo.BundleName, bundleName);
                        if (!string.IsNullOrEmpty(path))
                            bundleName = CollectFileMapWithPath(context, patchName, bundleName, pathInfo.UseResourceLoad, path
                                , ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName, pathInfo.AudioPatchType, searchPattern);

                        if (pathInfo.FileList != null)
                            CollectFileMapWithFiles(context, patchName, bundleName, pathInfo.FileList, Global.GetFullPath(path), ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName, pathInfo.AudioPatchType);
                    } break;
                    case PathBuildType.TopDirectory:
                    {
                        var dir = Global.GetFullPath(path);
                        foreach (var file in Directory.GetFiles(path, searchPattern, SearchOption.TopDirectoryOnly))
                        {
                            if (!Global.IsAsset(file))
                                continue;

                            var bundleName = Path.GetFileNameWithoutExtension(file)?.ToLower();
                            if (!string.IsNullOrEmpty(pathInfo.BundleName))
                                bundleName = string.Format(pathInfo.BundleName, bundleName);
                            
                            if (string.IsNullOrEmpty(bundleName))
                                continue;
                            
                            CollectFileMapWithFile(context, patchName, bundleName, file, dir, ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName, pathInfo.AudioPatchType);
                        }
                        
                        foreach (var directory in Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly))
                        {
                            var bundleName = Path.GetFileName(directory)?.ToLower();
                            if (!string.IsNullOrEmpty(pathInfo.BundleName))
                                bundleName = string.Format(pathInfo.BundleName, bundleName);
                            
                            CollectFileMapWithPath(context, patchName, bundleName, pathInfo.UseResourceLoad, directory
                                , ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName, pathInfo.AudioPatchType, searchPattern);
                        }
                        
                    } break;
                    case PathBuildType.AllDirectories:
                    {
                        var dir = Global.GetFullPath(path);
                        foreach (var file in Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories))
                        {
                            if (!Global.IsAsset(file))
                                continue;

                            var bundleName = Path.GetDirectoryName(file)?.ToLower();
                            if (!string.IsNullOrEmpty(pathInfo.BundleName))
                                bundleName = string.Format(pathInfo.BundleName, bundleName);
                            
                            if (string.IsNullOrEmpty(bundleName))
                                continue;
                            
                            CollectFileMapWithFile(context, patchName, bundleName, file, dir, ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName, pathInfo.AudioPatchType);
                        }
                    } break;
                }
            }
        }

        private static string CollectFileMapWithPath(BuildProcessorContext context, string patchName
            , string bundleName, bool useResourceLoad
            , string path, in List<string> ignoreFiles
        , ref Dictionary<string, FileBundleInfo> fileMap
        , ref Dictionary<string, BundleInfo> bundleInfos
        , in Dictionary<string, string> defFileBundleName
        , AudioPatchType audioPatchType
        , string searchPattern = "*.*")
        {
            if (string.IsNullOrEmpty(bundleName))
                bundleName = Path.GetFileName(path);

            if (useResourceLoad)
                bundleName = PatchUtil.GetResourceAssetBundleName(bundleName);

            if (File.Exists(path))
            {
                CollectFileMapWithFiles(context, patchName, bundleName, new []{ path }, Global.GetFullPath(path), ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName, audioPatchType);
            }
            else
            {
                var files = Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories);
                CollectFileMapWithFiles(context, patchName, bundleName, files, Global.GetFullPath(path), ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName, audioPatchType);
            }
            return bundleName;
        }

        private static void CollectFileMapWithFiles(BuildProcessorContext context, string patchName, string bundleName, IEnumerable<string> files, string dir,
            in List<string> ignoreFiles, ref Dictionary<string, FileBundleInfo> fileMap
            , ref Dictionary<string, BundleInfo> bundleInfos, in Dictionary<string, string> defFileBundleName
            , AudioPatchType audioPatchType)
        {
            if (files == null)
                return;
            
            foreach (var f in files)
            {
                if (!Global.IsAsset(f))
                    continue;

                var file = Global.GetFullPath(f);
                CollectFileMapWithFile(context, patchName, bundleName, file, dir, ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName, audioPatchType);
            }
        }

        private static void CollectResources(BuildProcessorContext context, IEnumerable<string> resFiles, in List<string> ignoreFiles,
            ref Dictionary<string, FileBundleInfo> fileMap, ref Dictionary<string, BundleInfo> bundleInfos,
            in Dictionary<string, string> defFileBundleName)
        {
            if (resFiles == null)
                return;
            
            var resourcePath = Path.Combine(Application.dataPath, "Resources");
            var dir = Global.GetFullPath(resourcePath);
            foreach (var file in resFiles)
            {
                if (!Global.IsAsset(file))
                    continue;
                
                if (!file.ToLower().Contains("/assets/resources/"))
                    continue;

                var subPath = Global.GetSubPath(resourcePath, file);
                var bundleName = PatchUtil.GetResourcesAssetBundle(subPath, out _);
                CollectFileMapWithFile(context, string.Empty, bundleName, file, dir, ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName, AudioPatchType.None);
            }
        }

        private static void CollectFileMapWithFile(BuildProcessorContext context, string patchName, string bundleName, string file, string dir, in List<string> ignoreFiles,
            ref Dictionary<string, FileBundleInfo> fileMap, ref Dictionary<string, BundleInfo> bundleInfos,
            in Dictionary<string, string> defFileBundleName, AudioPatchType audioPatchType)
        {
            if (context.IsSupportAudioPatch() && IsAudioFile(file) && audioPatchType != AudioPatchType.None)
            {
                switch (audioPatchType)
                {
                    case AudioPatchType.SplitAsSound:
                    {
                        var tempPath = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file));
                        var name = Global.GetSubPath(dir, tempPath);
                        name = bundleName.StartsWith("res_") ? $"{bundleName.Substring(4)}/{name}" : $"{bundleName}/{name}";
                        context.AddSoundPatchFiles(patchName, name, Global.GetAssetPath(file), bundleName);
                    }
                        break;
                    case AudioPatchType.SplitAsMusic:
                    {
                        var tempPath = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file));
                        var name = Global.GetSubPath(dir, tempPath);
                        name = bundleName.StartsWith("res_") ? $"{bundleName.Substring(4)}/{name}" : $"{bundleName}/{name}";
                        context.AddMusicPatchFiles(patchName, name, Global.GetAssetPath(file), bundleName);
                    }
                        break;
                }

                return;
            }
            
            file = Global.GetFullPath(file);
            if (!fileMap.TryGetValue(file, out var fileBundleInfo))
            {
                fileBundleInfo = new FileBundleInfo(file, dir, bundleName);
                fileMap.Add(file, fileBundleInfo);
            }

            CollectFileCurrentBundleInfo(file, ref bundleInfos);
            fileBundleInfo.BundleName = bundleName;

            var assetsRoot = Global.GetFullPath(Application.dataPath).ToLower();
            var assetPath = Global.GetSubPath(".", file);
            foreach (var df in AssetDatabase.GetDependencies(assetPath))
            {
                if (!Global.IsAsset(df))
                    continue;

                var depFile = Global.GetFullPath(df);
                if (!depFile.ToLower().StartsWith(assetsRoot))
                    continue;
                
                if (fileMap.TryGetValue(depFile, out var info))
                {
                    info.AddReference(bundleName);
                    continue;
                }
                
                if (ignoreFiles.Contains(depFile))
                    continue;

                var tempPath = Global.GetSubPath(".", depFile);
                if (tempPath.ToLower().StartsWith("assets/resources"))
                    continue;

                string depBundleName = CollectFileCurrentBundleInfo(depFile, ref bundleInfos);
                if (defFileBundleName.TryGetValue(depFile, out var bn))
                    depBundleName = bn;

                info = new FileBundleInfo(depFile, dir, depBundleName);
                info.AddReference(bundleName);
                fileMap.Add(depFile, info);
            }
        }

        private static string CollectFileCurrentBundleInfo(string file, ref Dictionary<string, BundleInfo> bundleInfos)
        {
            if (bundleInfos.TryGetValue(file, out var info))
                return info.Name;

            try
            {
                var assetName = Global.GetSubPath(".", file);
                var bundleName = AssetDatabase.GetImplicitAssetBundleName(assetName);
                if (string.IsNullOrEmpty(bundleName))
                    return bundleName;
                
                var variantName = AssetDatabase.GetImplicitAssetBundleVariantName(assetName);

                info = new BundleInfo
                {
                    FilePath = file,
                    Name = bundleName,
                    Variant = variantName
                };
                bundleInfos.Add(file, info);
                return bundleName;
            }
            catch (Exception e)
            {
                Debug.LogWarning("CollectFileCurrentBundleInfo:: " + e);
                return string.Empty;
            }
        }

        [AndroidProjectModify]
        public static void OnAndroidProjectModify(BuildProcessorContext context, AndroidProjectManager projectManager)
        {
            var doc = projectManager.LibraryManifest;
            doc.AddUsePermission("android.permission.WRITE_EXTERNAL_STORAGE", "SDCard写入数据");
            doc.AddUsePermission("android.permission.READ_EXTERNAL_STORAGE", "SDCard读取数据");
            // var node = doc.AddUsePermission("android.permission.MOUNT_UNMOUNT_FILESYSTEMS", "SDCard中创建与删除文件权限");
            // node.SetAttribute("ignore", AndroidConst.NS_TOOLS, "ProtectedPermissions");
            
            var buildParams = context.BuildChannels.Get<BuildPatchParams>();
            if (!buildParams.DisablePatchUpdate)
            {
                doc.AddUsePermission("android.permission.INTERNET", "网络访问权限");
                doc.AddUsePermission("android.permission.ACCESS_NETWORK_STATE", "网络状态权限");
                doc.AddUsePermission("android.permission.ACCESS_WIFI_STATE", "WIFI状态权限");
            }
        }
    }
}