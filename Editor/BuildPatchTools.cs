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
        [AssetBundleFilePathsCollectorAttribute(int.MinValue)]
        public static void CollectAssetBundleFilePaths(BuildProcessorContext context, string bundleName,
            List<string> result)
        {
            var buildParams = context.BuildChannels.Get<BuildPatchParams>("Patch");
            foreach (var path in buildParams.StreamingPaths.Concat(buildParams.Patches.SelectMany(patch => patch.Paths)))
            {
                if (path.UseResourceLoad)
                    continue;

                if (string.IsNullOrEmpty(path.BundleName))
                {
                    if (bundleName.ToLower().Equals(Path.GetFileName(path.Path)?.ToLower()))
                        result.Add(path.Path);
                } else if (path.BundleName.ToLower().Equals(bundleName.ToLower()))
                {
                    result.Add(path.Path);
                }
            }
        }
        
        [OnBuildBundles(int.MinValue)]
        [OnBuildPatches(int.MinValue)]
        public static void CollectInvalidAssets(BuildProcessorContext context)
        {
            var buildParams = context.BuildChannels.Get<BuildPatchParams>("Patch");
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
            var buildParams = context.BuildChannels.Get<BuildPatchParams>("Patch");
            
            context.BuildPatchesStart();
            var subPatches = new List<string>();

#region BuildPatches
            var patchPath = Path.Combine(Application.dataPath, "Patches");
            var fileMap = new Dictionary<string, FileBundleInfo>();
            var bundleInfos = new Dictionary<string, BundleInfo>();
            
            var invalidList = context.TryGet<List<string>>("invalidAssets", new List<string>());
            var depFileBundleName = context.TryGet("depFileBundleName", new Dictionary<string, string>());
            var streamPath = context.Get<string>("StreamingAssetsPath");
            if (!Directory.Exists(streamPath))
                Directory.CreateDirectory(streamPath);
                    
            FileBundleInfo.BuildType = buildParams.BuildType;
            PatchInfo[] patchInfos = buildParams.Patches;
            if (patchInfos != null)
            {
                foreach (var info in patchInfos)
                {
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
                    
                    Global.CallCustomOrderMethods<OnBuildBundlesCompletedAttribute, BuildToolsAttribute>(context, exportPath, info.Name, manifest, false);
                    
                    if (manifest == null)
                        continue;

                    if (info.CopyToStream && (!isBuildUpdatePatch || buildParams.EnableCopyToStreamWhenBuildUpdatePatches))
                    {
                        subPatches.Add(info.Name);
                        Global.MoveAllFilesToPath(exportPath, streamPath);
                    }
                    else if (!string.IsNullOrEmpty(context.BuildPath))
                    {
                        if (isBuildUpdatePatch)
                            Global.MoveAllFilesToPath(exportPath, context.BuildPath);
                        else
                            Global.MoveAllFilesToPath(exportPath, Path.Combine(context.BuildPath, "Patches"));
                    }
                }
                
                context.Set<string[]>("internalSubPatches", subPatches.ToArray());
    #endregion
                
            }

            FileBundleInfo.TempBundleMap.Clear();
            fileMap.Clear();
            bundleInfos.Clear();
            
            var modFiles = context.TryGet<List<string>>("UpdatePatchModFiles", null);
            var addFiles = context.TryGet<List<string>>("UpdatePatchAddFiles", null);
            CollectResources(modFiles, invalidList, ref fileMap, ref bundleInfos, depFileBundleName);
            CollectResources(addFiles, invalidList, ref fileMap, ref bundleInfos, depFileBundleName);

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
        
        [OnBuildBundlesCompleted]
        public static void OnBuildBundlesCompleted(BuildProcessorContext context, string streamingPath,
            string streamingName, AssetBundleManifest manifest, bool isCreate = false)
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

            var version = PluginUtil.GetVersionString(context.VersionName, int.Parse(context.Build));
            var subPatches = context.TryGet<string[]>("internalSubPatches", null);
            var map = context.TryGet("buildFileInfoMap",new Dictionary<string, FileManifest.FileInfo>());

            if (manifest == null)
            {
                //如果没有重新构建bundle,但是也需要刷新StreamingAssets文件
                bool isBuild = context.TryGet("IsBuild",true);
                if (!isBuild)
                {
                    FileManifest.AppendFileInfo(streamingPath, streamingName, version, ref map, subPatches);
                }
                return;
            }
                
            FileManifest.AppendFileInfo(manifest, streamingPath, streamingName, version, ref map, subPatches);
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
                        var bundleName = pathInfo.BundleName;
                        if (!string.IsNullOrEmpty(path))
                            bundleName = CollectFileMapWithPath(pathInfo.BundleName, pathInfo.UseResourceLoad, path
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
            var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
            CollectFileMapWithFiles(bundleName, files, ignoreFiles, ref fileMap, ref bundleInfos, defFileBundleName);
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
    }
}