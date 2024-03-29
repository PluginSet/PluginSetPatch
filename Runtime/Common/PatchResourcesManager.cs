using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PluginSet.Core;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;

#endif

namespace PluginSet.Patch
{
    public class PatchResourcesManager : ResourcesManager
    {
        internal static byte[] ReadFileContent(in FileManifest.FileInfo info)
        {
            var path = Path.Combine(PatchesSavePath, info.FileName);
            if (!File.Exists(path))
                return null;
            
            var content = File.ReadAllBytes(path);
            if (!string.IsNullOrEmpty(info.BundleHash))
                content = AssetBundleEncryption.DecryptBytes(content, info.BundleHash);
            return content;
        }
        
        private const int OnceMaxLoadAssetBundleCount = 5;

        public override string RunningVersion
        {
            get
            {
                if (_searchManifest.Count > 0)
                {
                    var manifest = _searchManifest[0];
                    var version = manifest.Version;
                    if (!string.IsNullOrEmpty(version))
                        return version;
                }

                return ResourceVersion;
            }
        }

        private List<FileManifest> _searchManifest = new List<FileManifest>();
        private TaskLimiter _tasks = new TaskLimiter(OnceMaxLoadAssetBundleCount);

        private readonly object _fileInfoLock = new object();
        private readonly Dictionary<string, FileManifest.FileInfo> _fileInfos = new Dictionary<string, FileManifest.FileInfo>();
        private readonly Dictionary<string, FileManifest> _bundleManifests = new Dictionary<string, FileManifest>();
        private readonly Dictionary<string, FileManifest> _assetManifests = new Dictionary<string, FileManifest>();

#if UNITY_EDITOR
        public static readonly Dictionary<string, PatchInfo> PatchInfos = new Dictionary<string, PatchInfo>();
#endif

        public override string StreamingAssetsName => "StreamingAssets";

        protected override void OnCreate()
        {
        }

        private void Reset()
        {
            ResourceVersion = PluginUtil.AppVersion;
            _searchManifest.Clear();
            _bundleManifests.Clear();
            _assetManifests.Clear();
            lock (_fileInfoLock)
                _fileInfos.Clear();
        }

        public override void AddSearchPath(string searchPath, bool front = false)
        {
            base.AddSearchPath(searchPath, front);
            var fileManifest = FileManifest.LoadPatchFileManifest(searchPath, PatchesSavePath);
            AddSearchManifest(fileManifest, front);
        }

        public override void RemoveSearchPath(string searchPath)
        {
            RemoveSearchManifest(searchPath);
            base.RemoveSearchPath(searchPath);
        }

        private void AddSearchManifest(FileManifest fileManifest, bool front)
        {
            fileManifest.LoadManifest();
            if (front)
                _searchManifest.Insert(0, fileManifest);
            else
                _searchManifest.Add(fileManifest);

            _bundleManifests.Clear();
            _assetManifests.Clear();
            lock (_fileInfoLock)
                _fileInfos.Clear();
        }

        private IEnumerator AddSearchManifestAsync(FileManifest fileManifest, bool front)
        {
            yield return fileManifest.LoadManifestAsync();

            if (front)
                _searchManifest.Insert(0, fileManifest);
            else
                _searchManifest.Add(fileManifest);

            _bundleManifests.Clear();
            _assetManifests.Clear();
            lock (_fileInfoLock)
                _fileInfos.Clear();
        }

        private void RemoveSearchManifest(string name)
        {
            AssetBundleRef.UnloadWithTag(name);

            var count = _searchManifest.Count;
            FileManifest manifest = null;
            int index = -1;
            for (int i = 0; i < count; i++)
            {
                var temp = _searchManifest[i];
                if (temp.Name.Equals(name))
                {
                    manifest = temp;
                    index = i;
                    break;
                }
            }

            manifest?.Unload();
            if (index >= 0)
                _searchManifest.RemoveAt(index);

            _bundleManifests.Clear();
            _assetManifests.Clear();
            lock (_fileInfoLock)
                _fileInfos.Clear();
        }

#if UNITY_EDITOR
        public IEnumerator Init(FileManifest fileManifest, PatchInfo[] patchInfos, PathInfo[] streamPaths, string[] streamingExtendPaths)
        {
            PatchInfos.Clear();
            base.AddSearchPath(StreamingAssetsName);
            PatchInfos.Add(StreamingAssetsName, new PatchInfo()
            {
                Paths =  streamPaths,
                extendFiles = streamingExtendPaths
            });
            if (patchInfos != null)
            {
                foreach (var info in patchInfos)
                {
                    PatchInfos.Add(info.Name.ToLower(), info);
                }
            }

            yield return Init(fileManifest);
        }
#endif

        public IEnumerator Init(FileManifest fileManifest)
        {
            Reset();

            if (fileManifest == null)
                yield break;

            yield return AddSearchManifestAsync(fileManifest, false);

            var version = fileManifest.Version;
            if (!string.IsNullOrEmpty(version))
            {
                ResourceVersion = version;
            }
            AssetBundleRef.ResourcesVersion = version;
        }

        public override Object Load(string path, Type type)
        {
            string bundleName = PatchUtil.GetResourcesAssetBundle(path.ToLower(), out var assetName);

            var manifest = FindBundlePatchWithAsset(bundleName, assetName);
            if (manifest != null)
            {
                AssetBundleRef abRef = LoadBundleRef(bundleName, manifest);
                return abRef.LoadAsset(assetName, type);
            }

#if UNITY_EDITOR
            var asset = PatchUtil.LoadEditorAsset(path, PatchInfos, type);
            if (asset != null)
                return asset;
#endif
            return Resources.Load(path, type);
        }

        public override AsyncOperationHandle<Object> LoadAsync(string path, Type type)
        {
            string bundleName = PatchUtil.GetResourcesAssetBundle(path.ToLower(), out var assetName);

            return LoadAssetAsync(bundleName, assetName, type, delegate(Action<Object> completed)
            {
#if UNITY_EDITOR
                var asset = PatchUtil.LoadEditorAsset(path, PatchInfos, type);
                if (asset != null)
                {
                    completed?.Invoke(asset);
                    return null;
                }
#endif
                var async = AsyncResourceLoadHandle<Object>.Create(path);
                if (completed != null)
                    async.OnGetResult += completed;
                return async;
            });
        }

        public override AsyncOperationHandle<T> LoadAsync<T>(string path)
        {
            string bundleName = PatchUtil.GetResourcesAssetBundle(path.ToLower(), out var assetName);

            return LoadAssetAsync<T>(bundleName, assetName, delegate(Action<T> completed)
            {
#if UNITY_EDITOR
                var asset = PatchUtil.LoadEditorAsset<T>(path, PatchInfos);
                if (asset != null)
                {
                    completed?.Invoke(asset);
                    return null;
                }
#endif
                var async = AsyncResourceLoadHandle<T>.Create(path);
                if (completed != null)
                    async.OnGetResult += completed;
                return async;
            });
        }

        public override AssetBundle LoadBundle(string bundleName)
        {
            var manifest = FindBundlePatch(bundleName);
            if (manifest == null) return null;

            var abRef = LoadBundleRef(bundleName, manifest);
            if (abRef == null)
                return null;

            abRef.Retain();
            return abRef.Source;
        }

        public override AsyncOperationHandle<AssetBundle> LoadBundleAsync(string bundleName)
        {
            var manifest = FindBundlePatch(bundleName);
            if (manifest == null)
                return AsyncNonResultHandle<AssetBundle>.Default;

            return AsyncMonoCoroutineHandle<AssetBundle>.Create(CoroutineHelper.Instance
                , action => LoadBundleRefAsync(bundleName, manifest, true, action));
        }

        public override void ReleaseBundle(AssetBundle bundle)
        {
            ReleaseBundle(bundle.name);
        }

        public override void DontReleaseBundle(AssetBundle bundle)
        {
            DontReleaseBundle(bundle.name);
        }

        public override void ReleaseBundle(string bundleName)
        {
            var manifest = FindBundlePatch(bundleName);
            if (manifest == null)
                return;
            var realName = manifest.GetRealBundleName(bundleName);
            var abRef = AssetBundleRef.GetLoadedAssetBundle(realName);
            abRef?.Release();
        }

        public override void DontReleaseBundle(string bundleName)
        {
            var manifest = FindBundlePatch(bundleName);
            if (manifest == null)
                return;
            var realName = manifest.GetRealBundleName(bundleName);
            var abRef = AssetBundleRef.GetLoadedAssetBundle(realName);
            abRef?.DontRelease();
        }

        public override void ReleaseAll()
        {
            _tasks.Clear();
            AssetBundleRef.UnloadAll();
        }

        public override Object LoadAsset(string bundleName, string assetName, Type type)
        {
#if UNITY_EDITOR
            if (_searchManifest.All(mani => mani.IsEmpty))
                return PatchUtil.LoadEditorBundleAsset(bundleName, assetName, PatchInfos, type);
#endif
            var manifest = FindBundlePatchWithAsset(bundleName, assetName);
            if (manifest == null)
                return null;

            AssetBundleRef abRef = LoadBundleRef(bundleName, manifest);
            return abRef.LoadAsset(assetName, type);
        }

        public override AssetBundle GetAssetBundle(Object asset)
        {
            var abRef = AssetBundleRef.FindLoadedAsset(asset);
            return abRef?.Source;
        }

        public override void RetainAsset(Object asset)
        {
            var abRef = AssetBundleRef.FindLoadedAsset(asset);
            abRef?.Retain();
        }

        public override void ReleaseAsset(Object asset)
        {
            var abRef = AssetBundleRef.FindLoadedAsset(asset);
            abRef?.Release();
        }

        public override AsyncOperationHandle<T> LoadAssetAsync<T>(string bundleName, string assetName)
        {
            return LoadAssetAsync<T>(bundleName, assetName, null);
        }

        public override AsyncOperationHandle<Object> LoadAssetAsync(string bundleName, string assetName, Type type)
        {
            return LoadAssetAsync(bundleName, assetName, type, null);
        }

        private AsyncOperationHandle<Object> LoadAssetAsync(string bundleName, string assetName, Type type, Func<Action<Object>, IEnumerator> noneCall)
        {
            return AsyncMonoCoroutineHandle<Object>.Create(CoroutineHelper.Instance
                , action => LoadBundleRefAssetAsync(bundleName, assetName, type, action, noneCall));
        }

        private AsyncOperationHandle<T> LoadAssetAsync<T>(string bundleName, string assetName, Func<Action<T>, IEnumerator> noneCall) where T : UnityEngine.Object
        {
            return AsyncMonoCoroutineHandle<T>.Create(CoroutineHelper.Instance
                , action => LoadBundleRefAssetAsync<T>(bundleName, assetName, action, noneCall));
        }

        public override Object[] LoadAllAssets(string bundleName, Type type)
        {
            var manifest = FindBundlePatch(bundleName);
            if (manifest == null)
                return null;

            AssetBundleRef abRef = LoadBundleRef(bundleName, manifest);
            return abRef.LoadAllAssets(type);
        }

        public override AsyncOperationHandle<T[]> LoadAllAssetsAsync<T>(string bundleName)
        {
            var manifest = FindBundlePatch(bundleName);
            if (manifest == null)
                return AsyncNonResultHandle<T[]>.Default;

            return AsyncMonoCoroutineHandle<T[]>.Create(CoroutineHelper.Instance,
                action => LoadBundleRefAllAssetAsync<T>(manifest, bundleName, action));
        }

        public override AsyncOperationHandle<Object[]> LoadAllAssetsAsync(string bundleName, Type type)
        {
            var manifest = FindBundlePatch(bundleName);
            if (manifest == null)
                return AsyncNonResultHandle<Object[]>.Default;

            return AsyncMonoCoroutineHandle<Object[]>.Create(CoroutineHelper.Instance,
                action => LoadBundleRefAllAssetAsync(manifest, bundleName, type, action));
        }

        public override byte[] ReadFile(string fileName)
        {
            var fileInfo = FindFileInfo(fileName);
            if (!fileInfo.HasValue)
            {
#if UNITY_EDITOR
                if (PatchUtil.FindFileInPaths(fileName, out var path, SearchPaths, PatchInfos))
                    return File.ReadAllBytes(path);
#endif
                return null;
            }

            return ReadFileContent(fileInfo.Value);
        }

#if UNITY_EDITOR
        public override bool IsValidAssetFile(string file)
        {
            if (!base.IsValidAssetFile(file))
                return false;

            var path = Global.GetSubPath(".", file);
            foreach (var kv in PatchInfos)
            {
                foreach (var pathInfo in kv.Value.Paths)
                {
                    if (path.StartsWith(pathInfo.Path))
                    {
                        return SearchPaths.Contains(kv.Key);
                    }
                }
            }

            return true;
        }
#endif
        public override bool ExistsBundle(string name)
        {
#if UNITY_EDITOR
            if (_searchManifest.All(manifest => manifest.IsEmpty))
                return PatchUtil.ExistsBundle(name, PatchInfos);
#endif
            return FindBundlePatch(name) != null;
        }

        public override bool ExistsAsset(string bundleName, string assetName)
        {
#if UNITY_EDITOR
            if (_searchManifest.All(manifest => manifest.IsEmpty))
                return !string.IsNullOrEmpty(PatchUtil.FindEditorBundleAsset(bundleName, assetName, PatchInfos));
#endif
            return FindBundlePatchWithAsset(bundleName, assetName) != null;
        }

        private FileManifest FindBundlePatch(string name)
        {
            if (_bundleManifests.TryGetValue(name, out var result))
                return result;

            foreach (var manifest in _searchManifest)
            {
                if (manifest.ExistBundle(name))
                {
                    _bundleManifests.Add(name, manifest);
                    return manifest;
                }
            }

            return null;
        }
        
        private FileManifest.FileInfo? FindFileInfo(string name)
        {
            lock (_fileInfoLock)
            {
                if (_fileInfos.TryGetValue(name, out var result))
                    return result;
            }

            foreach (var manifest in _searchManifest)
            {
                if (manifest.GetFileInfo(name, out var info))
                {
                    lock (_fileInfoLock)
                        _fileInfos.Add(name, info);
                    return info;
                }
            }

            return null;
        }

        private FileManifest FindBundlePatchWithAsset(string bundleName, string assetName)
        {
            var key = $"{bundleName}?{assetName}";
            if (_assetManifests.TryGetValue(key, out var result))
                return result;

            foreach (var manifest in _searchManifest)
            {
                if (!manifest.ExistBundle(bundleName))
                    continue;

                var abRef = GetOrCreateRef(bundleName, manifest);
                abRef.LoadSync();

                if (abRef.ContainAsset(assetName))
                {
                    _assetManifests.Add(key, manifest);
                    return manifest;
                }
            }

            return null;
        }

        private AssetBundleRef GetOrCreateRef(string name, FileManifest manifest, bool pushTask = false)
        {
            var realName = manifest.GetRealBundleName(name);
            var abRef = AssetBundleRef.GetLoadedAssetBundle(realName);
            if (abRef == null)
            {
                if (!manifest.GetFileInfo(realName, out var fileInfo))
                    throw new Exception("Cannot get file info with " + realName);

                abRef = AssetBundleRef.GetAssetBundleRef(realName, fileInfo);
                abRef.Tag = manifest.Name;
                abRef.AutoRelease();

                if (pushTask)
                    _tasks.PushTask(abRef);
            }

            return abRef;
        }

        private List<AssetBundleRef> CheckDependsLoaded(string name, FileManifest manifest)
        {
            var depends = manifest.GetAllDependencies(name);
            if (depends == null || depends.Length <= 0)
                return null;

            List<AssetBundleRef> deps = new List<AssetBundleRef>();
            foreach (var dep in depends)
            {
                var abRef = GetOrCreateRef(dep, manifest);
                abRef.LoadSync();

                deps.Add(abRef);
            }

            return deps;
        }

        private AssetBundleRef LoadBundleRef(string name, FileManifest manifest)
        {
            var depends = CheckDependsLoaded(name, manifest);

            var abRef = GetOrCreateRef(name, manifest);
            if (!abRef.HasSetDepends)
                abRef.SetDependencies(depends);

            abRef.LoadSync();

#if UNITY_EDITOR
            if (abRef.Source == null)
            {
                Debug.LogError("Failed to load AssetBundle => " + name);
            }
#endif
            return abRef;
        }

        private List<AssetBundleRef> CheckDependsStartedLoad(string name, FileManifest manifest)
        {
            var depends = manifest.GetAllDependencies(name);
            if (depends == null || depends.Length <= 0)
                return null;

            List<AssetBundleRef> deps = new List<AssetBundleRef>();
            foreach (var dep in depends)
            {
                var abRef = GetOrCreateRef(dep, manifest, true);
                deps.Add(abRef);
            }

            return deps;
        }

        private IEnumerator LoadBundleRefAsync(string name, FileManifest manifest, bool retain = false, Action<AssetBundle> complete = null)
        {
            var depends = CheckDependsStartedLoad(name, manifest);

            var abRef = GetOrCreateRef(name, manifest, true);
            if (!abRef.HasSetDepends)
                abRef.SetDependencies(depends);

            if (retain)
                abRef.Retain();

            if (depends != null)
                yield return depends.GetEnumerator();

            yield return abRef;

#if UNITY_EDITOR
            if (abRef.IsFail)
            {
//                Debug.LogError("Failed to load AssetBundle => " + name);
                throw new Exception($"Failed to load AssetBundle name:{name}");
            }
#endif
            complete?.Invoke(abRef.Source);
        }

        private IEnumerator LoadBundleRefAssetAsync<T>(string name, string assetName,
            Action<T> complete = null, Func<Action<T>,
                IEnumerator> noneCall = null) where T : Object
        {
            yield return LoadBundleRefAssetAsync(name, assetName, typeof(T),
                delegate(Object o) { complete?.Invoke(o as T); },
                delegate(Action<Object> action) { return noneCall?.Invoke(action); });
        }

        private IEnumerator LoadBundleRefAssetAsync(string name, string assetName, Type type,
            Action<Object> complete = null, Func<Action<Object>,
                IEnumerator> noneCall = null)
        {
#if UNITY_EDITOR
            if (_searchManifest.All(mani => mani.IsEmpty))
            {
                var asset = PatchUtil.LoadEditorBundleAsset(name, assetName, PatchInfos, type);
                yield return null;
                if (asset == null)
                {
                    if (noneCall != null)
                        yield return noneCall(complete);
                    else
                        complete?.Invoke(null);
                }
                else
                {
                    complete?.Invoke(asset);
                }
                yield break;
            }
#endif
            FileManifest manifest = null;

            var key = $"{name}?{assetName}";
            if (!_assetManifests.TryGetValue(key, out manifest))
            {
                foreach (var mani in _searchManifest)
                {
                    if (!mani.ExistBundle(name))
                        continue;

                    var tempRef = GetOrCreateRef(name, mani, true);
                    yield return tempRef;
                    if (tempRef.ContainAsset(assetName))
                    {
                        manifest = mani;
                        if (!_assetManifests.ContainsKey(key))
                            _assetManifests.Add(key, mani);
                        break;
                    }
                }
            }

            if (manifest == null)
            {
                if (noneCall != null)
                    yield return noneCall(complete);
                else
                    complete?.Invoke(null);

                yield break;
            }

            yield return LoadBundleRefAsync(name, manifest);
            var abRef = GetOrCreateRef(name, manifest);
            yield return abRef.LoadAssetAsync(assetName, type);
            complete?.Invoke(abRef.LoadAsset(assetName, type));
        }

        private IEnumerator LoadBundleRefAllAssetAsync<T>(FileManifest manifest, string name, Action<T[]> complete = null) where T : Object
        {
            yield return LoadBundleRefAllAssetAsync(manifest, name, typeof(T), delegate(Object[] objects) { complete?.Invoke(objects as T[]); });
        }

        private IEnumerator LoadBundleRefAllAssetAsync(FileManifest manifest, string name, Type type, Action<Object[]> complete = null)
        {
            yield return LoadBundleRefAsync(name, manifest);
            var abRef = GetOrCreateRef(name, manifest);
            yield return abRef.LoadAllAssetAsync(type);
            complete?.Invoke(abRef.LoadAllAssets(type));
        }
    }
}
