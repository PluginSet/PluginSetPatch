using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PluginSet.Core;
using UnityEngine;
using UnityEngine.Networking;
using Logger = PluginSet.Core.Logger;
using Object = UnityEngine.Object;

namespace PluginSet.Patch
{
    internal class AssetBundleRef: CustomYieldInstruction, IAsyncOperationTask
    {
        private static readonly Logger Logger = LoggerManager.GetLogger("AssetBundleRef");
        
        private static readonly Dictionary<string, AssetBundleRef> LoadedRefs = new Dictionary<string, AssetBundleRef>();

        private static string FindString(IEnumerable<string> elements, Func<string, bool> match)
        {
            foreach (var str in elements)
            {
                if (match(str))
                    return str;
            }

            return string.Empty;
        }

        public static AssetBundleRef GetLoadedAssetBundle(string name)
        {
            if (LoadedRefs.TryGetValue(name, out var result))
            {
                return result._isReleased ? null : result;
            }
            
            return null;
        }

        public static AssetBundleRef GetAssetBundleRef(string name, FileManifest.FileInfo fileInfo)
        {
            if (LoadedRefs.TryGetValue(name, out var result))
            {
                if (result._isReleased)
                    result.Use(fileInfo);
                return result;
            }

            result = new AssetBundleRef(name);
            result.Use(fileInfo);
            LoadedRefs.Add(name, result);
            return result;
        }

        public static AssetBundleRef FindLoadedAsset(Object asset)
        {
            foreach (var loader in LoadedRefs.Values)
            {
                if (loader._isReleased)
                    continue;
                
                if (loader._loadedAssets.Contains(asset))
                    return loader;
            }

            return null;
        }
        

        public static void UnloadWithTag(string tag)
        {
            var keys = LoadedRefs.Where(kv => kv.Value.Tag.Equals(tag)).Select(kv => kv.Key).ToArray();
            foreach (var key in keys)
            {
                if (LoadedRefs.TryGetValue(key, out var abRef))
                {
                    abRef.Unload();
                }
            }
        }

        public static void UnloadAll()
        {
            foreach (var loadedRef in LoadedRefs)
            {
                loadedRef.Value.Unload();
            }
            AssetBundle.UnloadAllAssetBundles(true);
        }
        
#if DEBUG
        internal static int AssetCount = 0;
#endif

        private AssetBundle _source = null;
        private bool _sourceIsNull = true;
        private List<AssetBundleRef> _depends = null;
        private readonly HashSet<Object> _loadedAssets = new HashSet<Object>();
        
        private int _reference = 0;
        private bool _isAutoRelease = false;
        private bool _dontRelease = false;
        private bool _isReleased = false;
        private bool _isLoading = false;
        private bool _hasSetDepends = false;

        private UnityWebRequest _contentRequest;
        private AssetBundleCreateRequest _createRequest;
        
        private readonly string _name;
        private FileManifest.FileInfo _fileInfo;
        private bool _isWaiting;

        private string[] _sourceAssetNames = null;
        private readonly Dictionary<string, string> _assetPaths = new Dictionary<string, string>();
        
        private event Action<AssetBundle> LoadCompleted;

        public override bool keepWaiting => _isWaiting;
        
        public string Tag { get; set; }
        
        public AssetBundle Source => _source;
        public bool IsFail => _sourceIsNull && !_isReleased;
        
        public override string ToString()
        {
            return $"{(!_sourceIsNull ? _source.ToString() : "Null")}: reference({_reference})";
        }

        public bool HasSetDepends => _hasSetDepends;
        public bool IsUnused  => _reference <= 0;

        public bool IsLoaded => !_isWaiting;

        public bool IsSceneBundle => !_sourceIsNull && _source.isStreamedSceneAssetBundle;
        
        private AssetBundleRef(string name)
        {
            _name = name;
        }

        public AssetBundleRef Retain()
        {
            _reference++;
            return this;
        }

        public void Release()
        {
            _reference--;
            if (_isReleased)
            {
                return;
            }
            
            if (_isAutoRelease && _reference <= 0)
            {
                Unload();
            }
        }

        public void AutoRelease()
        {
            if (_isAutoRelease)
                return;
            
            if (_dontRelease)
            {
                return;
            }
            
            _isAutoRelease = true;
            _reference--;
        }

        public void DontRelease()
        {
            if (_dontRelease)
                return;

            _dontRelease = true;
            if (_isAutoRelease)
            {
                _isAutoRelease = false;
                _reference++;
            }
        }
        

        public void SetDependencies(List<AssetBundleRef> deps)
        {
            _hasSetDepends = true;
            if (deps != null)
            {
                foreach (var dep in deps)
                {
                    dep.Retain();
                }
            }
            
            if (_depends != null)
            {
                foreach (var dep in _depends)
                {
                    dep.Release();
                }
            }
            _depends = deps;
        }

        public AssetBundle LoadSync()
        {
            if (IsLoaded)
                return _source;

            AssetBundle result = null;
            if (_createRequest != null)
            {
                // 引擎会在异步加载时强制转换为同步加载返回AssetBundle
                result = _createRequest.assetBundle;
                if (result)
                {
                    return OnLoadedAssetBundle(result);
                }
            }
            
            var request = _contentRequest;
            var file = Path.Combine(PatchResourcesManager.PatchesSavePath, _fileInfo.FileName);
            if (File.Exists(file))
            {
                var content = File.ReadAllBytes(file);
                content = AssetBundleEncryption.DecryptBytes(content, _fileInfo.BundleHash);
                
                result = AssetBundle.LoadFromMemory(content);
            }
            
            result = OnLoadedAssetBundle(result);
            request?.Abort();
            return result;
        }
        
        public UnityEngine.Object LoadAsset(string name, Type type)
        {
            if (_source == null)
                return null;

#if ENABLE_LOAD_ASSET_WITH_NAME
            var asset = _source.LoadAsset(name, type);
#else
            var path = FindAssetPath(name);
            if (string.IsNullOrEmpty(path))
                return null;
            
            var asset = _source.LoadAsset(path, type);
#endif
            _loadedAssets.Add(asset);
            return asset;
        }

        public T LoadAsset<T>(string name) where T : Object
        {
            return LoadAsset(name, typeof(T)) as T;
        }

        public T[] LoadAllAssets<T>() where T : Object
        {
            var assets = _source.LoadAllAssets<T>();
            _loadedAssets.UnionWith(assets);
            return assets;
        }
        
        public UnityEngine.Object[] LoadAllAssets(Type type)
        {
            var assets = _source.LoadAllAssets(type);
            _loadedAssets.UnionWith(assets);
            return assets;
        }
        

        public AsyncOperationHandle Start()
        {
            if (IsLoaded || _isReleased)
                return null;

            return AsyncMonoCoroutineHandle<AssetBundle>.Create(CoroutineHelper.Instance, LoadAsync);
        }

        public IEnumerator LoadAllAssetAsync<T>() where T : Object
        {
            if (_isWaiting)
                yield return this;
            
            var request = _source.LoadAllAssetsAsync<T>();
            yield return request;
            if (request.allAssets != null)
            {
                _loadedAssets.UnionWith(request.allAssets);
            }
        }
        

        public IEnumerator LoadAllAssetAsync(Type type)
        {
            if (_isWaiting)
                yield return this;
            
            var request = _source.LoadAllAssetsAsync(type);
            yield return request;
            if (request.allAssets != null)
            {
                _loadedAssets.UnionWith(request.allAssets);
            }
        }
        
        public IEnumerator LoadAssetAsync(string name, Type type)
        {
            if (_isWaiting)
                yield return this;

#if ENABLE_LOAD_ASSET_WITH_NAME
            var request = _source.LoadAssetAsync(name, type);
#else
            var path = FindAssetPath(name);
            if (string.IsNullOrEmpty(path))
                yield break;
            
            var request = _source.LoadAssetAsync(path, type);
#endif
            yield return request;
            if (request.asset == null)
            {
                throw new Exception($"Cannot load asset {name} in AssetBundle {_name}");
            }
            _loadedAssets.Add(request.asset);
        }

        public IEnumerator LoadAssetAsync<T>(string name) where T : Object
        {
            yield return LoadAssetAsync(name, typeof(T));
        }
        
        public IEnumerator LoadAsync(Action<AssetBundle> callback)
        {
            LoadCompleted += callback;
            if (_isLoading)
                yield break;
            
            _isLoading = true;
            
#if UNITY_WEBGL
            var file = Path.Combine(ResourcesManager.PatchesSavePath, _fileInfo.FileName);
            byte[] content = null;
            if (File.Exists(file))
            {
                content = File.ReadAllBytes(file);
            }
            else
            {
                if (!IsLoaded)
                    Debug.LogWarning($"Cannot load bundle file :{_fileInfo.FileName}, error: file {file} not exist");
                OnLoadedAssetBundle(_source);
                yield break;
            }
#else
            var url = ResourcesManager.PatchesSavePathUrlPrefix + _fileInfo.FileName;
            var contentRequest = new UnityWebRequest(url, "GET", new DownloadHandlerBuffer(), null);
            _contentRequest = contentRequest;
            yield return contentRequest.SendWebRequest();
            
            if (!contentRequest.isDone || !_isLoading)
            {
                OnLoadedAssetBundle(_source);
                yield break;
            }

            if (contentRequest.result != UnityWebRequest.Result.Success)
            {
                if (!IsLoaded)
                    Debug.LogWarning($"Cannot load bundle file :{_fileInfo.FileName}, error: {contentRequest.error}, url:{url}");
                OnLoadedAssetBundle(_source);
                yield break;
            }

            var content = contentRequest.downloadHandler.data;
            _contentRequest = null;
#endif
            if (IsLoaded || _isReleased)
                yield break;
            
            content = AssetBundleEncryption.DecryptBytes(content, _fileInfo.BundleHash);
            var createRequest = _createRequest = AssetBundle.LoadFromMemoryAsync(content);
            yield return createRequest;
            var result = createRequest.assetBundle;
            OnLoadedAssetBundle(result != null ? result : _source);
        }

        private void Use(FileManifest.FileInfo fileInfo)
        {
            _isWaiting = true;
            _fileInfo = fileInfo;
            _isReleased = false;
            _reference = _isAutoRelease ? 0 : 1;
        }
        
        private void Unload()
        {
            // _contentRequest?.Abort();
            // _contentRequest = null;
            LoadCompleted = null;
            SetDependencies(null);
            _hasSetDepends = false;
            OnLoadedAssetBundle(null, false);
            _isReleased = true;
        }

        public bool ContainAsset(string assetName)
        {
            return !string.IsNullOrEmpty(FindAssetPath(assetName));
        }

        protected string FindAssetPath(string assetName)
        {
            if (_isWaiting)
                return null;

            if (_assetPaths.TryGetValue(assetName, out var result))
                return result;

            if (_sourceAssetNames == null)
                return null;

            string findPath = null;
            if (assetName.StartsWith("assets/"))
            {
                findPath = FindString(_sourceAssetNames, val => val.StartsWith(assetName));
            }
            else
            {
                var tempName = assetName.ToLower().Replace(".", "[.]");
                string p = $"^assets/([\\w\\s]+[/])*{tempName}([.]\\w+)?$";
                findPath = FindString(_sourceAssetNames, val => Regex.IsMatch(val, p));
            }
            
            _assetPaths.Add(assetName, findPath);
            return findPath;
        }

        private AssetBundle OnLoadedAssetBundle(AssetBundle assetBundle, bool completedLoading = true)
        {
            _isWaiting = false;
            if (completedLoading)
            {
                _contentRequest = null;
                _isLoading = false;
                _createRequest = null;
            }

            if (_isReleased)
            {
                if (assetBundle)
                    assetBundle.Unload(true);
                return null;
            }
            
            if (_source != assetBundle)
            {
#if DEBUG
                AssetCount -= _sourceAssetNames?.Length ?? 0;
#endif
                if (_source)
                    _source.Unload(true);
                
                _loadedAssets.Clear();
                _source = assetBundle;
                _sourceIsNull = !_source;
                _assetPaths.Clear();
                _sourceAssetNames = _sourceIsNull ? null : _source.GetAllAssetNames();
#if DEBUG
                AssetCount += _sourceAssetNames?.Length ?? 0;
#endif
            }
            
            LoadCompleted?.Invoke(_source);
            LoadCompleted = null;
            return assetBundle;
        }
    }

}
