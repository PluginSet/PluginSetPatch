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
                    CollectFileMap(infoPaths.ToArray(), invalidList, ref fileMap, ref bundleInfos, depFileBundleName);
                    
                    context.Set<BundleInfo[]>("patchBundleRevertInfo", bundleInfos.Values.ToArray());

                    foreach (var bundleName in AssetDatabase.GetAllAssetBundleNames())
                    {
                        if (string.IsNullOrEmpty(bundleName))
                            continue;

                        var files = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
                        CollectFileMapWithFiles(bundleName, files, invalidList, ref fileMap, ref bundleInfos, depFileBundleName);
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
            CollectResources(modFiles, invalidList, ref fileMap, ref bundleInfos, depFileBundleName);
            CollectResources(addFiles, invalidList, ref fileMap, ref bundleInfos, depFileBundleName);
            context.SetBuildResult("CollectedResources", string.Join(",", fileMap.Keys));

            foreach (var bundleName in AssetDatabase.GetAllAssetBundleNames())
            {
                if (string.IsNullOrEmpty(bundleName))
                    continue;

                var files = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
                CollectFileMapWithFiles(bundleName, files, invalidList, ref fileMap, ref bundleInfos, depFileBundleName);
            }

            var streamPaths = context.TryGet("streamingPaths", buildParams.StreamingPaths);
            CollectFileMap(streamPaths, invalidList, ref fileMap, ref bundleInfos, depFileBundleName);

            context.Set<BundleInfo[]>("patchBundleRevertInfo", bundleInfos.Values.ToArray());

            foreach (var kv in fileMap)
            {
                context.AddBuildBundle(kv.Value.BundleName, kv.Key);
            }

            context.BuildPatchesEnded();
            AssetDatabase.Refresh();
        }
        
        [OnBuildBundlesCompleted(int.MaxValue)]
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

        private static void CollectFileMap(in PathInfo[] paths,
            in List<string> ignoreFiles,
            ref Dictionary<string, FileBundleInfo> fileMap,
            ref Dictionary<string, BundleInfo> bundleInfos,
            in Dictionary<string, string> defFileBundleName
            )
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
                            bundleName = CollectFileMapWithPath(bundleName, pathInfo.UseResourceLoad, path
                                , ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName);

                        if (pathInfo.FileList != null)
                            CollectFileMapWithFiles(bundleName, pathInfo.FileList, ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName);
                    } break;
                    case PathBuildType.TopDirectory:
                    {
                        foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly))
                        {
                            if (!Global.IsAsset(file))
                                continue;

                            var bundleName = Path.GetFileNameWithoutExtension(file)?.ToLower();
                            if (!string.IsNullOrEmpty(pathInfo.BundleName))
                                bundleName = string.Format(pathInfo.BundleName, bundleName);
                            
                            if (string.IsNullOrEmpty(bundleName))
                                continue;
                            
                            CollectFileMapWithFile(bundleName, file, ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName);
                        }
                        
                        foreach (var directory in Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly))
                        {
                            var bundleName = Path.GetFileName(directory)?.ToLower();
                            if (!string.IsNullOrEmpty(pathInfo.BundleName))
                                bundleName = string.Format(pathInfo.BundleName, bundleName);
                            
                            CollectFileMapWithPath(bundleName, pathInfo.UseResourceLoad, directory
                                , ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName);
                        }
                        
                    } break;
                    case PathBuildType.AllDirectories:
                    {
                        foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
                        {
                            if (!Global.IsAsset(file))
                                continue;

                            var bundleName = Path.GetFileNameWithoutExtension(file)?.ToLower();
                            if (!string.IsNullOrEmpty(pathInfo.BundleName))
                                bundleName = string.Format(pathInfo.BundleName, bundleName);
                            
                            if (string.IsNullOrEmpty(bundleName))
                                continue;
                            
                            CollectFileMapWithFile(bundleName, file, ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName);
                        }
                    } break;
                }
            }
        }

        private static string CollectFileMapWithPath(string bundleName, bool useResourceLoad
            , string path, in List<string> ignoreFiles
        , ref Dictionary<string, FileBundleInfo> fileMap
        , ref Dictionary<string, BundleInfo> bundleInfos
        , in Dictionary<string, string> defFileBundleName
        )
        {
            if (string.IsNullOrEmpty(bundleName))
                bundleName = Path.GetFileName(path);

            if (useResourceLoad)
                bundleName = PatchUtil.GetResourceAssetBundleName(bundleName);

            if (File.Exists(path))
            {
                CollectFileMapWithFiles(bundleName, new []{ path }, ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName);
            }
            else
            {
                var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                CollectFileMapWithFiles(bundleName, files, ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName);
            }
            return bundleName;
        }

        private static void CollectFileMapWithFiles(string bundleName, IEnumerable<string> files,
            in List<string> ignoreFiles, ref Dictionary<string, FileBundleInfo> fileMap
            , ref Dictionary<string, BundleInfo> bundleInfos, in Dictionary<string, string> defFileBundleName)
        {
            if (files == null)
                return;
            
            foreach (var f in files)
            {
                if (!Global.IsAsset(f))
                    continue;

                var file = Global.GetFullPath(f);
                CollectFileMapWithFile(bundleName, file, ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName);
            }
        }

        private static void CollectResources(IEnumerable<string> resFiles, in List<string> ignoreFiles,
            ref Dictionary<string, FileBundleInfo> fileMap, ref Dictionary<string, BundleInfo> bundleInfos,
            in Dictionary<string, string> defFileBundleName)
        {
            if (resFiles == null)
                return;
            
            var resourcePath = Path.Combine(Application.dataPath, "Resources");
            foreach (var file in resFiles)
            {
                if (!Global.IsAsset(file))
                    continue;
                
                if (!file.ToLower().Contains("/assets/resources/"))
                    continue;

                var subPath = Global.GetSubPath(resourcePath, file);
                var bundleName = PatchUtil.GetResourcesAssetBundle(subPath, out _);
                CollectFileMapWithFile(bundleName, file, ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName);
            }
        }

        private static void CollectFileMapWithFile(string bundleName, string file, in List<string> ignoreFiles,
            ref Dictionary<string, FileBundleInfo> fileMap, ref Dictionary<string, BundleInfo> bundleInfos,
            in Dictionary<string, string> defFileBundleName)
        {
            file = Global.GetFullPath(file);
            if (!fileMap.TryGetValue(file, out var fileBundleInfo))
            {
                fileBundleInfo = new FileBundleInfo(file, bundleName);
                fileMap.Add(file, fileBundleInfo);
            }

            CollectFileCurrentBundleInfo(file, ref bundleInfos);
            fileBundleInfo.BundleName = bundleName;

            var assetPath = Global.GetSubPath(".", file);
            foreach (var df in AssetDatabase.GetDependencies(assetPath))
            {
                if (!Global.IsAsset(df))
                    continue;

                var depFile = Global.GetFullPath(df);
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

                info = new FileBundleInfo(depFile, depBundleName);
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