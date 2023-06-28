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
        
        private static Dictionary<string, AssetBundleRef> s_loadedRefs = new Dictionary<string, AssetBundleRef>();

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
            if (s_loadedRefs.TryGetValue(name, out var result))
                return result;
            
            return null;
        }

        public static void UnloadWithTag(string tag)
        {
            var keys = s_loadedRefs.Where(kv => kv.Value.Tag.Equals(tag)).Select(kv => kv.Key).ToArray();
            foreach (var key in keys)
            {
                if (s_loadedRefs.TryGetValue(key, out var abRef))
                {
                    abRef.Unload();
                    s_loadedRefs.Remove(key);
                }
            }
        }

        public static void UnloadAll()
        {
            s_loadedRefs.Clear();
            AssetBundle.UnloadAllAssetBundles(true);
        }

        private static void AddLoadedRef(AssetBundleRef abRef)
        {
            s_loadedRefs[abRef._name] = abRef;
        }

        private static void RemoveLoadedRef(AssetBundleRef abRef)
        {
            var name = abRef._name;
            if (s_loadedRefs.ContainsKey(name))
                s_loadedRefs.Remove(name);
        }

#if DEBUG
        internal static int AssetCount = 0;
#endif

        private AssetBundle _source = null;
        private bool _sourceIsNull = true;
        private List<AssetBundleRef> _depends = null;
        
        private int _reference = 0;
        private bool _isAutoRelease = false;
        private bool _dontRelease = false;
        private bool _isReleased = false;
        private bool _isLoading = false;
        private bool _hasSetDepends = false;

        private UnityWebRequest _contentRequest;
        private AssetBundleCreateRequest _createRequest;
        
        private string _name;
        private FileManifest.FileInfo _fileInfo;
        private bool _isWaiting;

        private string[] _sourceAssetNames = null;
        private Dictionary<string, string> _assetPaths = new Dictionary<string, string>();
        
        private event Action<AssetBundle> _loadCompleted;

        public override bool keepWaiting => _isWaiting;
        
        public string Tag { get; set; }
        
        public AssetBundle Source => _source;
        public bool IsNull => _sourceIsNull;
        
        public override string ToString()
        {
            return $"{(!_sourceIsNull ? _source.ToString() : "Null")}: reference({_reference})";
        }

        public bool HasSetDepends => _hasSetDepends;
        public bool IsUnused  => _reference <= 0;

        public bool IsLoaded => !_isWaiting;

        public bool IsSceneBundle => !_sourceIsNull && _source.isStreamedSceneAssetBundle;
        
        public AssetBundleRef(string name, FileManifest.FileInfo fileInfo)
        {
            _name = name;
            _fileInfo = fileInfo;
            _isWaiting = true;
            _reference = 1;
            AddLoadedRef(this);
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
            return _source.LoadAsset(name, type);
#else
            var path = FindAssetPath(name);
            if (string.IsNullOrEmpty(path))
                return null;
            
            return _source.LoadAsset(path, type);
#endif
        }

        public T LoadAsset<T>(string name) where T : Object
        {
            return LoadAsset(name, typeof(T)) as T;
        }

        public T[] LoadAllAssets<T>() where T : Object
        {
            var assets = _source.LoadAllAssets<T>();
            return assets;
        }
        
        public UnityEngine.Object[] LoadAllAssets(Type type)
        {
            var assets = _source.LoadAllAssets(type);
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
        }
        

        public IEnumerator LoadAllAssetAsync(Type type)
        {
            if (_isWaiting)
                yield return this;
            
            var request = _source.LoadAllAssetsAsync(type);
            yield return request;
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
        }

        public IEnumerator LoadAssetAsync<T>(string name) where T : Object
        {
            yield return LoadAssetAsync(name, typeof(T));
        }
        
        public IEnumerator LoadAsync(Action<AssetBundle> callback)
        {
            _loadCompleted += callback;
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
            
            content = AssetBundleEncryption.DecryptBytes(content, _fileInfo.BundleHash);

            var createRequest = _createRequest = AssetBundle.LoadFromMemoryAsync(content);
            yield return createRequest;
            var result = createRequest.assetBundle;
            OnLoadedAssetBundle(result != null ? result : _source);
        }
        
        public void Unload()
        {
            RemoveLoadedRef(this);
            _contentRequest?.Abort();
            _loadCompleted = null;
            SetDependencies(null);
            _hasSetDepends = false;
            OnLoadedAssetBundle(null);
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

        protected AssetBundle OnLoadedAssetBundle(AssetBundle assetBundle)
        {
            _createRequest = null;
            _contentRequest = null;
            _isLoading = false;
            _isWaiting = false;

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
                
                _source = assetBundle;
                _sourceIsNull = !_source;
                _assetPaths.Clear();
                _sourceAssetNames = _sourceIsNull ? null : _source.GetAllAssetNames();
#if DEBUG
                AssetCount += _sourceAssetNames?.Length ?? 0;
#endif
            }
            
            _loadCompleted?.Invoke(_source);
            _loadCompleted = null;
            return assetBundle;
        }
    }

}
