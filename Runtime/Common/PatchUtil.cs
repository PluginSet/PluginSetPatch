using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PluginSet.Core;
using UnityEngine;

namespace PluginSet.Patch
{
    public enum CheckResult
    {
        Nothing,
        DownloadPatches,
        DownloadApp,
        NeedCheck,
        Retry,
    }
    
    public static class PatchUtil
    {
        public static CheckResult CheckResourceVersion(string current, string target)
        {
            if (string.IsNullOrEmpty(target))
                return CheckResult.Nothing;

            if (string.IsNullOrEmpty(current))
                return CheckResult.DownloadPatches;
            
            string targetVersion;
            string targetCode;

            if (!PluginUtil.SplitVersionString(target, out targetVersion, out targetCode))
                return CheckResult.Nothing;

            string currentVersion;
            string currentCode;

            if (!PluginUtil.SplitVersionString(current, out currentVersion, out currentCode))
                return CheckResult.DownloadPatches;

            if (targetVersion.Equals(currentVersion))
            {
                if (targetCode.Equals(currentCode))
                    return CheckResult.Nothing;

                var targetCodeNum = int.Parse(targetCode);
                var currentCodeNum = int.Parse(currentCode);
                if (currentCodeNum < targetCodeNum)
                    return CheckResult.DownloadPatches;
            }
            else
            {
                var targetVersionNum = PluginUtil.ParseVersionNumber(targetVersion);
                var currentVersionNum = PluginUtil.ParseVersionNumber(currentVersion);
                if (currentVersionNum < targetVersionNum)
                    return CheckResult.DownloadApp;
            }
            
            return CheckResult.Nothing;
        }

        public static bool CheckFileInfo(string filePath, ref FileManifest.FileInfo info)
        {
            var content = File.ReadAllBytes(filePath);
            if (info.Size >= 0 && content.Length != info.Size)
            {
                Debug.LogWarning($"CheckFileInfo size fail {filePath}: info={info.Size} size={ content.Length}");
                return false;
            }

            if (!string.IsNullOrEmpty(info.Md5) && !info.Md5.Equals(PluginUtil.GetMd5(content)))
            {
                Debug.LogWarning($"CheckFileInfo md5 fail {filePath}: info={info.Md5} md5={PluginUtil.GetMd5(content)}");
                return false;
            }

            return true;
        }
        
        public static string GetResourcesAssetBundle(string resourcePath, out string assetName)
        {
#if ENABLE_LOAD_ASSET_WITH_NAME
            assetName = Path.GetFileName(resourcePath);
#else
            assetName = resourcePath;
#endif
            int pos = resourcePath.IndexOf('/');
            if (pos < 0)
            {
                return "res_resources";
            }
            
            return GetResourceAssetBundleName(resourcePath.Substring(0, pos));
        }

        public static string GetResourceAssetBundleName(string pathName)
        {
            return "res_" + pathName.ToLower();
        }

        /// <summary>
        /// 分包检查更新通用逻辑（检查本地文件与目标URL文件的版本差异，判断是否需要更新）
        /// </summary>
        /// <param name="patchName">分包名称</param>
        /// <param name="urlPrefix">下载地址目录</param>
        /// <param name="streamingUrl">下载Streaming文件名</param>
        /// <param name="targetVersion">目标版本号</param>
        /// <param name="callback">检测回调</param>
        /// <returns></returns>
        public static IEnumerator CheckDownloadPatch(string patchName, string urlPrefix, string streamingUrl, string targetVersion = null, Action<PatchesDownloader> callback = null)
        {
            if (string.IsNullOrEmpty(urlPrefix))
            {
                callback?.Invoke(null);
                yield break;
            }

            var savePath = PatchResourcesManager.PatchesSavePath;
            var patchDownloader = new PatchesDownloader(urlPrefix, streamingUrl, patchName, savePath);
            var needDownload = true;
            FileManifest oldFileManifest = FileManifest.LoadPatchFileManifest(patchName, savePath);
            if (!string.IsNullOrEmpty(oldFileManifest.Version))
            {
                if (string.IsNullOrEmpty(targetVersion))
                    needDownload = false;
                else
                    needDownload = CheckResourceVersion(oldFileManifest.Version, targetVersion) != CheckResult.Nothing;
            }
            
            if (!needDownload && !string.IsNullOrEmpty(targetVersion))
            {
                yield return patchDownloader.PrepareDownload(oldFileManifest);
                if (patchDownloader.TotalTaskCount <= 0)
                {
                    patchDownloader.Stop();
                    callback?.Invoke(null);
                    yield break;
                }
                else if (callback != null)
                {
                    callback.Invoke(patchDownloader);
                    yield break;
                }
                else
                {
                    patchDownloader.Stop();
                    yield break;
                }
            }

            var request = FileManifest.RequestFileManifest(patchName, urlPrefix + streamingUrl);
            yield return request;
            
            FileManifest newFileManifest = request.result;
            if (newFileManifest == null || newFileManifest.Manifest == null)
            {
                patchDownloader.Stop();
                callback?.Invoke(null);
                yield break;
            }

            if (!needDownload)
            {
                needDownload = CheckResourceVersion(oldFileManifest.Version, newFileManifest.Version) !=
                               CheckResult.Nothing;
            }

            if (!needDownload)
            {
                yield return patchDownloader.PrepareDownload(oldFileManifest);
            }
            else
            {
                yield return patchDownloader.PrepareDownload(newFileManifest);
            }

            if (patchDownloader.TotalTaskCount > 0)
            {
                if (callback != null)
                {
                    callback.Invoke(patchDownloader);
                    yield break;
                }
            }
            else
            {
                if (needDownload)
                    newFileManifest.SaveTo(Path.Combine(savePath, patchName));
                callback?.Invoke(null);
            }
            
            patchDownloader.Stop();
        }
        
#if UNITY_EDITOR
        private static readonly Dictionary<Type, string[]> fileTypes = new Dictionary<Type, string[]>
        {
            {typeof(GameObject), new []{"prefab"}},
#if UNITY_MODULE_AUDIO
            {typeof(AudioClip), new []{"mp3", "ogg", "wav"}},
#endif
#if UNITY_MODULE_ANIMATION
            {typeof(AnimationClip), new []{"anim"}},
            {typeof(RuntimeAnimatorController), new []{"controller"}},
#endif
            {typeof(TextAsset), new []{"txt", "bytes", "json"}},
            {typeof(Material), new []{"mat"}},
            {typeof(Texture), new []{"jpg", "png", "bmp", "tif", "gif"}},
            {typeof(Mesh), new []{"asset"}},
            {typeof(ScriptableObject), new []{"asset"}},
        };

        private static readonly List<string> allFileTypes = (from kv in fileTypes
            from tn in kv.Value
            select tn).ToList();

        public static IEnumerable<string> FindTypeFileExtensions(Type type)
        {
            if (fileTypes.TryGetValue(type, out var result))
                return result;

            foreach (var kv in fileTypes)
            {
                if (type.IsSubclassOf(kv.Key))
                    return kv.Value;
            }

            return null;
        }

        public static UnityEngine.Object LoadEditorAsset(string path, Dictionary<string, PatchInfo> patchInfos, Type type)
        {
            var extensions = FindTypeFileExtensions(type);
            if (extensions == null)
                extensions = allFileTypes;

            var manager = ResourcesManager.Instance;
            foreach (var ext in extensions)
            {
                var tempPath = $"{path}.{ext}";
                
                foreach (var searchPath in manager.SearchPaths)
                {
                    if (patchInfos.TryGetValue(searchPath, out var patch))
                    {
                        foreach (var pathInfo in patch.Paths)
                        {
                            if (!pathInfo.UseResourceLoad)
                                continue;

                            string fileName = null;
                            if (!string.IsNullOrEmpty(pathInfo.Path))
                            {
                                if (pathInfo.BuildType == PathBuildType.AllInBundle)
                                    fileName = FindMatchFileName(Path.Combine(pathInfo.Path, ".."), tempPath);
                                else
                                    fileName = FindMatchFileName(pathInfo.Path, tempPath);
                            }

                            if (fileName == null && pathInfo.FileList != null)
                            {
                                foreach (var file in pathInfo.FileList)
                                {
                                    if (file.EndsWith(tempPath))
                                    {
                                        fileName = Global.GetFullPath(file);
                                        if (!File.Exists(fileName))
                                            fileName = null;
                                        else
                                            break;
                                    }
                                }
                            }
                            
                            if (fileName != null)
                                return UnityEditor.AssetDatabase.LoadAssetAtPath(Global.GetSubPath(".", fileName), type);
                        }
                    }
                }
            }

            return null;
        }

        public static T LoadEditorAsset<T>(string path, Dictionary<string, PatchInfo> patchInfos) where T: UnityEngine.Object
        {
            var result = LoadEditorAsset(path, patchInfos, typeof(T));
            if (result == null)
                return null;

            return result as T;
        }

        public static bool ExistsBundle(string bundleName, Dictionary<string, PatchInfo> patchInfos)
        {
            var manager = ResourcesManager.Instance;
            foreach (var searchPath in manager.SearchPaths)
            {
                if (!patchInfos.TryGetValue(searchPath, out var patch))
                    continue;

                foreach (var pathInfo in patch.Paths)
                {
                    if (pathInfo.UseResourceLoad)
                        continue;
                    
                    if (pathInfo.BuildType == PathBuildType.AllDirectories)
                    {
                        foreach (var file in Directory.GetFiles(pathInfo.Path, "*", SearchOption.AllDirectories))
                        {
                            if (!Global.IsAsset(file))
                                continue;
                            
                            var pathBundleName = Path.GetFileNameWithoutExtension(file)?.ToLower();
                            if (!string.IsNullOrEmpty(pathInfo.BundleName))
                                pathBundleName = string.Format(pathInfo.BundleName, pathBundleName);

                            if (bundleName.Equals(pathBundleName))
                                return true;
                        }
                    }
                    else if (pathInfo.BuildType == PathBuildType.TopDirectory)
                    {
                        foreach (var directory in Directory.GetDirectories(pathInfo.Path, "*", SearchOption.TopDirectoryOnly))
                        {
                            var pathBundleName = Path.GetFileName(directory)?.ToLower();
                            if (!string.IsNullOrEmpty(pathInfo.BundleName))
                                pathBundleName = string.Format(pathInfo.BundleName, pathBundleName);

                            if (bundleName.Equals(pathBundleName))
                                return true;
                        }

                        foreach (var file in Directory.GetFiles(pathInfo.Path, "*", SearchOption.TopDirectoryOnly))
                        {
                            if (!Global.IsAsset(file))
                                continue;
                            
                            var pathBundleName = Path.GetFileNameWithoutExtension(file)?.ToLower();
                            if (!string.IsNullOrEmpty(pathInfo.BundleName))
                                pathBundleName = string.Format(pathInfo.BundleName, pathBundleName);

                            if (bundleName.Equals(pathBundleName))
                                return true;
                        }
                    }
                    else if (pathInfo.BuildType == PathBuildType.AllInBundle)
                    {
                    
                        var pathBundleName = Path.GetFileNameWithoutExtension(pathInfo.Path)?.ToLower();
                        if (!string.IsNullOrEmpty(pathInfo.BundleName))
                            pathBundleName = string.Format(pathInfo.BundleName, pathBundleName);

                        if (bundleName.Equals(pathBundleName))
                            return true;
                    }
                    else
                    {
                        throw new Exception("Unknow BuildPathType = " + pathInfo.BuildType);
                    }
                }
            }

            return false;
        }


        private static string FindMatchFileName(string path, string pattern,
            SearchOption option = SearchOption.TopDirectoryOnly)
        {
            if (!Directory.Exists(path))
                return null;

            pattern = pattern ?? "*";
            pattern = pattern.Replace("\\", "/");
            var paths = pattern.Split('/');
            if (paths.Length > 1)
            {
                var newPaths = new string[paths.Length];
                newPaths[0] = path;
                Array.Copy(paths, 0, newPaths, 1, paths.Length - 1);
                return FindMatchFileName(Path.Combine(newPaths), paths[paths.Length - 1], option);
            }
            
            var files = Directory.GetFiles(path, pattern, option);
            if (files.Length <= 0)
                return null;

            return files[0];
        }

        public static string FindEditorBundleAsset(string bundleName, string assetName,
            Dictionary<string, PatchInfo> patchInfos, string ext = "*")
        {
            var manager = ResourcesManager.Instance;
            var tempPath = $"{assetName}.{ext}";
            
            foreach (var searchPath in manager.SearchPaths)
            {
                if (!patchInfos.TryGetValue(searchPath, out var patch))
                    continue;

                foreach (var pathInfo in patch.Paths)
                {
                    if (pathInfo.UseResourceLoad)
                        continue;

                    string fileName = string.Empty;
                    if (pathInfo.BuildType == PathBuildType.AllDirectories)
                    {
                        foreach (var file in Directory.GetFiles(pathInfo.Path, $"*.{ext}", SearchOption.AllDirectories))
                        {
                            if (!Global.IsAsset(file))
                                continue;
                            
                            var pathBundleName = Path.GetFileNameWithoutExtension(file)?.ToLower();
                            if (!string.IsNullOrEmpty(pathInfo.BundleName))
                                pathBundleName = string.Format(pathInfo.BundleName, pathBundleName);
                                
                            if (!bundleName.Equals(pathBundleName))
                                continue;

                            return Global.GetSubPath(".", Global.GetFullPath(file));
                        }
                    }
                    else if (pathInfo.BuildType == PathBuildType.TopDirectory)
                    {
                        foreach (var directory in Directory.GetDirectories(pathInfo.Path, "*", SearchOption.TopDirectoryOnly))
                        {
                            var pathBundleName = Path.GetFileName(directory)?.ToLower();
                            if (!string.IsNullOrEmpty(pathInfo.BundleName))
                                pathBundleName = string.Format(pathInfo.BundleName, pathBundleName);
                                
                            if (!bundleName.Equals(pathBundleName))
                                continue;

                            fileName = FindMatchFileName(directory, tempPath);
                            if (!string.IsNullOrEmpty(fileName))
                                return Global.GetSubPath(".", Global.GetFullPath(fileName));
                        }

                        if (string.IsNullOrEmpty(fileName))
                        {
                            foreach (var file in Directory.GetFiles(pathInfo.Path, $"*.{ext}", SearchOption.TopDirectoryOnly))
                            {
                                if (!Global.IsAsset(file))
                                    continue;
                            
                                var pathBundleName = Path.GetFileNameWithoutExtension(file)?.ToLower();
                                if (!string.IsNullOrEmpty(pathInfo.BundleName))
                                    pathBundleName = string.Format(pathInfo.BundleName, pathBundleName);
                                    
                                if (!bundleName.Equals(pathBundleName))
                                    continue;

                                return Global.GetSubPath(".", Global.GetFullPath(file));
                            }
                        }
                    }
                    else if (pathInfo.BuildType == PathBuildType.AllInBundle)
                    {
                        var pathBundleName = Path.GetFileNameWithoutExtension(pathInfo.Path)?.ToLower();
                        if (!string.IsNullOrEmpty(pathInfo.BundleName))
                            pathBundleName = string.Format(pathInfo.BundleName, pathBundleName);
                        
                        if (!bundleName.Equals(pathBundleName))
                            continue;

                        fileName = FindMatchFileName(pathInfo.Path, $"*.{ext}");
                        if (!string.IsNullOrEmpty(fileName))
                            return Global.GetSubPath(".", Global.GetFullPath(fileName));

                        if (pathInfo.FileList != null)
                        {
                            foreach (var file in pathInfo.FileList)
                            {
                                if (file.EndsWith(tempPath))
                                {
                                    fileName = Global.GetFullPath(file);
                                    if (File.Exists(fileName))
                                        return Global.GetSubPath(".", fileName);
                                }
                            }
                        }
                    }
                    else
                    {
                        throw new Exception("Unknow BuildPathType = " + pathInfo.BuildType);
                    }
                }
            }

            return null;
        }
        
        public static UnityEngine.Object LoadEditorBundleAsset(string bundleName, string assetName, Dictionary<string, PatchInfo> patchInfos, Type type)
        {
            var extensionTypes = FindTypeFileExtensions(type);
            if (extensionTypes == null)
                return null;
            
            
            foreach (var ext in extensionTypes)
            {
                var fileName = FindEditorBundleAsset(bundleName, assetName, patchInfos, ext);
                if (!string.IsNullOrEmpty(fileName))
                    return UnityEditor.AssetDatabase.LoadAssetAtPath(fileName, type);
            }

            return null;
        }

        public static T LoadEditorBundleAsset<T>(string bundleName, string assetName, Dictionary<string, PatchInfo> patchInfos) where T: UnityEngine.Object
        {
            return LoadEditorBundleAsset(bundleName, assetName, patchInfos, typeof(T)) as T;
        }

        public static bool FindFileInPaths(string name, out string path, IEnumerable<string> searchPaths, Dictionary<string, PatchInfo> patchInfos)
        {
            path = null;
            foreach (var searchPath in searchPaths)
            {
                if (!patchInfos.TryGetValue(searchPath, out var patch))
                    continue;
                
                foreach (var p in patch.extendFiles)
                {
                    var file = Path.Combine(p, name);
                    if (File.Exists(file))
                    {
                        path = file;
                        return true;
                    }
                }
            }

            return false;
        }
#endif
    }
}