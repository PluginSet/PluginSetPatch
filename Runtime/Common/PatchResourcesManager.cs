using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PluginSet.Core;
using UnityEngine;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;

#endif

namespace PluginSet.Patch
{
    public class PatchResourcesManager : ResourcesManager
    {
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

        private Dictionary<string, FileManifest> _bunleManifests = new Dictionary<string, FileManifest>();
        private Dictionary<string, FileManifest> _assetManifests = new Dictionary<string, FileManifest>();

#if UNITY_EDITOR
        public static readonly Dictionary<string, PathInfo[]> PathInfoses = new Dictionary<string, PathInfo[]>();
#endif

        public override string StreamingAssetsName => "StreamingAssets";

        protected override void OnCreate()
        {
        }

        private void Reset()
        {
            ResourceVersion = PluginUtil.AppVersion;
            _searchManifest.Clear();
            _bunleManifests.Clear();
            _assetManifests.Clear();
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

            _bunleManifests.Clear();
            _assetManifests.Clear();
        }

        private IEnumerator AddSearchManifestAsync(FileManifest fileManifest, bool front)
        {
            yield return fileManifest.LoadManifestAsync();

            if (front)
                _searchManifest.Insert(0, fileManifest);
            else
                _searchManifest.Add(fileManifest);

            _bunleManifests.Clear();
            _assetManifests.Clear();
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

            _bunleManifests.Clear();
            _assetManifests.Clear();
        }

#if UNITY_EDITOR
        public IEnumerator Init(FileManifest fileManifest, PatchInfo[] patchInfos, PathInfo[] streamPaths)
        {
            PathInfoses.Clear();
            base.AddSearchPath(StreamingAssetsName);
            PathInfoses.Add(StreamingAssetsName, streamPaths);
            if (patchInfos != null)
            {
                foreach (var info in patchInfos)
                {
                    PathInfoses.Add(info.Name.ToLower(), info.Paths);
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
            var asset = PatchUtil.LoadEditorAsset(path, PathInfoses, type);
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
                var asset = PatchUtil.LoadEditorAsset(path, PathInfoses, type);
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
                var asset = PatchUtil.LoadEditorAsset<T>(path, PathInfoses);
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
            var abRef = AssetBundleRef.GetLoadedAssetBundle(bundleName);
            abRef?.Release();
        }

        public override void DontReleaseBundle(string bundleName)
        {
            var abRef = AssetBundleRef.GetLoadedAssetBundle(bundleName);
            abRef?.DontRelease();
        }

        public override void ReleaseAll()
        {
            _tasks.Clear();
            AssetBundleRef.UnloadAll();
        }

        public override Object LoadAsset(string bundleName, string assetName, Type type)
        {
            var manifest = FindBundlePatchWithAsset(bundleName, assetName);
            if (manifest == null)
                return null;

            AssetBundleRef abRef = LoadBundleRef(bundleName, manifest);
            return abRef.LoadAsset(assetName, type);
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

#if UNITY_EDITOR
        public override bool IsValidAssetFile(string file)
        {
            if (!base.IsValidAssetFile(file))
                return false;

            var path = Global.GetSubPath(".", file);
            foreach (var kv in PathInfoses)
            {
                foreach (var pathInfo in kv.Value)
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
            return FindBundlePatch(name) != null;
        }

        private FileManifest FindBundlePatch(string name)
        {
            if (_bunleManifests.TryGetValue(name, out var result))
                return result;

            foreach (var manifest in _searchManifest)
            {
                if (manifest.ExistBundle(name))
                {
                    _bunleManifests.Add(name, manifest);
                    return manifest;
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

                abRef = new AssetBundleRef(realName, fileInfo);
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
            if (abRef.IsNull)
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
