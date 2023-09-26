using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using PluginSet.Core;
using UnityEngine;

namespace PluginSet.Patch
{
    public partial class FileManifest
    {
        [Serializable]
        public struct FileInfo
        {
            [SerializeField]
            public string Name;
            [SerializeField]
            public string FileName;
            [SerializeField]
            public int Size;
            [SerializeField]
            public string Md5;
            [SerializeField]
            public string BundleHash;
        }
        
        [Serializable]
        private struct FileInfoList
        {
            [SerializeField]
            public List<FileInfo> List;
        }

        public static AsyncOperationHandle<FileManifest> RequestFileManifest(string name, string url)
        {
            return AsyncFileManifestLoadHandle.Create(name, url);
        }

        public static FileManifest LoadFileManifest(string name, string filePath)
        {
            byte[] buffer = null;
            if (File.Exists(filePath))
                buffer = File.ReadAllBytes(filePath);
            
            return new FileManifest(name, buffer);
        }
        
        public static FileManifest LoadPatchFileManifest(string name, string savePath)
        {
            var filePath = Path.Combine(savePath, name);
            return LoadFileManifest(name, filePath);
        }

        private static bool IsUnityManifest(byte[] buffer)
        {
            var title = Encoding.UTF8.GetString(buffer, 0, 5);
            return title.ToLower().Equals("unity");
        }

        private static int ReadInt(byte[] buffer, ref int position)
        {
            int i1 = (buffer[position] << 24)
                     | (buffer[position + 1] << 16)
                     | (buffer[position + 2] << 8)
                     | (buffer[position + 3]);
            position += 4;
            return i1;
        }

        private static byte[] ReadBytes(byte[] buffer, ref int position)
        {
            var size = ReadInt(buffer, ref position);
            var result = new byte[size];
            Array.Copy(buffer, position, result, 0, size);
            position += size;
            return result;
        }

        private static string ReadString(byte[] buffer, ref int position)
        {
            var len = ReadInt(buffer, ref position);
            var size = ReadInt(buffer, ref position);
            var result = Encoding.UTF8.GetString(buffer, position, len);
            position += size;
            return result;
        }

        private static FileInfoList ParseFileInfoList(byte[] buffer)
        {
            MemoryStream stream = new MemoryStream(buffer);
            BinaryFormatter bf = new BinaryFormatter();
            return (FileInfoList) bf.Deserialize(stream);
        }
        
        private static readonly EnumeratorLocker ManifestLoadAsyncLocker = new EnumeratorLocker();

        public string Name { get; protected set; }
        public string Version { get; protected set; }
        public string Tag { get; protected set; }
        
        public bool IsEmpty { get; protected set; }
        
        public string[] SubPatches { get; protected set; }

        private int _fileVersion;

        public AssetBundleManifest Manifest
        {
            get
            {
                if (_manifest == null)
                    LoadManifest();
                
                return _manifest;
            }
        }

        public byte[] Buffer { get; protected set; }

        public IEnumerable<FileInfo> AllFileInfo
        {
            get
            {
                if (_fileInfoMap == null)
                    LoadManifest();

                return _fileInfoList.List;
            }
        }

        private FileInfoList _fileInfoList;
        private Dictionary<string, FileInfo> _fileInfoMap;
        
        private byte[] _manifestBuffer;
        private AssetBundleManifest _manifest;
        private Dictionary<string, string> _bundleRealNames;
        
        private Dictionary<string, string> _fileNameMap = new Dictionary<string, string>();
        private Dictionary<string, string[]> _dependMap = new Dictionary<string, string[]>();
        
        public FileManifest(string name, byte[] buffer)
        {
            Name = name;
            InitWithBuffer(buffer);
        }

        private void InitWithBuffer(byte[] buffer)
        {
            IsEmpty = buffer == null;
            if (IsEmpty)
                return;
            
            Buffer = buffer;
            
            if (IsUnityManifest(buffer))
            {
                _manifestBuffer = buffer;
                return;
            }

            var position = 0;
            _fileVersion = ReadInt(buffer, ref position);
            _manifestBuffer = ReadBytes(buffer, ref position);
            var fileData = ReadBytes(buffer, ref position);
            _fileInfoList = ParseFileInfoList(fileData);
            Version = ReadString(buffer, ref position);
            var calMd5 = PluginUtil.GetMd5(buffer, 0, position);
            var md5 = ReadString(buffer, ref position);
            if (!md5.Equals(calMd5))
                Version = PluginUtil.GetVersionString("0", 0);
            if (_fileVersion > 0)
                Tag = ReadString(buffer, ref position);
            
            var subCount = ReadInt(buffer, ref position);
            SubPatches = new string[subCount];
            for (int i = 0; i < subCount; i++)
            {
                SubPatches[i] = ReadString(buffer, ref position);
            }

            _fileInfoMap = new Dictionary<string, FileInfo>();
            if (_fileVersion > 1)
            {
                var fileCount = ReadInt(buffer, ref position);
                for (int i = 0; i < fileCount; i++)
                {
                    var fileInfo = new FileInfo
                    {
                        Name = ReadString(buffer, ref position),
                        FileName = ReadString(buffer, ref position),
                        Size = ReadInt(buffer, ref position),
                        Md5 = ReadString(buffer, ref position),
                        BundleHash = ReadString(buffer, ref position),
                    };
                    _fileInfoMap[fileInfo.Name] = fileInfo;
                }
            }
            else
            {
                foreach (var fileInfo in _fileInfoList.List)
                {
                    _fileInfoMap[fileInfo.Name] = fileInfo;
                }
            }
        }

        private void InitFakeFileInfo()
        {
            if (_fileInfoMap != null)
                return;
            
            _fileInfoMap = new Dictionary<string, FileInfo>();
            _fileInfoList = new FileInfoList
            {
                List = new List<FileInfo>()
            };

            var list = _fileInfoList.List;
            foreach (var name in _manifest.GetAllAssetBundles())
            {
                list.Add(new FileInfo
                {
                    Name = name,
                    Size = -1,
                });
                _fileNameMap.Add(name, name);
            }
        }

        public IEnumerator LoadManifestAsync()
        {
            if (_manifestBuffer == null)
                yield break;
            
            if (_manifest != null)
                yield break;

            if (ManifestLoadAsyncLocker.Locked)
                yield return ManifestLoadAsyncLocker;
            
            ManifestLoadAsyncLocker.Lock();
            var abRequest = AssetBundle.LoadFromMemoryAsync(_manifestBuffer);
            yield return abRequest;
            _manifestBuffer = null;

            var ab = abRequest.assetBundle;
            if (ab == null)
            {
                Debug.LogWarning("FileManifest 加载失败！");
                yield break;
            }

            var manifestRequest = ab.LoadAssetAsync<AssetBundleManifest>("AssetBundleManifest");
            yield return manifestRequest;
            
            _manifest = manifestRequest.asset as AssetBundleManifest;
            
            ab.Unload(false);
            ManifestLoadAsyncLocker.Unlock();
            
            if (_manifest == null)
                yield break;

            CollectAssetBundleRealNames(_manifest);
            InitFakeFileInfo();
        }

        public AssetBundleManifest LoadManifest()
        {
            if (_manifestBuffer == null)
                return _manifest;
            
            var ab = AssetBundle.LoadFromMemory(_manifestBuffer);
            _manifestBuffer = null;
            
            if (ab == null)
            {
                Debug.LogWarning("FileManifest 加载失败！");
                return null;
            }

            _manifest = ab.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
            ab.Unload(false);

            if (_manifest != null)
            {
                CollectAssetBundleRealNames(_manifest);
                InitFakeFileInfo();
            }
            
            return _manifest;
        }

        public void Unload()
        {
            if (_manifest == null)
                return;
            
            Resources.UnloadAsset(_manifest);
            _manifest = null;
        }

        public bool ExistBundle(string bundleName)
        {
            if (_manifest == null || _bundleRealNames == null)
                return false;

            return _bundleRealNames.ContainsKey(bundleName);
        }

        public string GetRealBundleName(string bundleName)
        {
            if (_manifest == null || _bundleRealNames == null)
                return null;

            if (_bundleRealNames.TryGetValue(bundleName, out var result))
                return result;

            return null;
        }

        public bool GetFileInfo(string fileName, out FileInfo info)
        {
            return _fileInfoMap.TryGetValue(fileName, out info);
        }

        public string[] GetAllDependencies(string name)
        {
            if (_dependMap.TryGetValue(name, out var result))
                return result;

            var realName = GetRealBundleName(name);
            var dependencies = _manifest.GetAllDependencies(realName);
            _dependMap.Add(name, dependencies);
            return dependencies;
        }

        public void SaveTo(string path)
        {
            using (FileStream file = new FileStream(path, FileMode.Create))
            {
                file.Write(Buffer, 0, Buffer.Length);
            }
        }

        private void CollectAssetBundleRealNames(AssetBundleManifest manifest)
        {
            _bundleRealNames = new Dictionary<string, string>();
            var tail = $".{Name}";
            var tailLen = tail.Length;
            foreach (var bundleName in manifest.GetAllAssetBundles())
            {
                if (bundleName.EndsWith(tail))
                    _bundleRealNames[bundleName.Substring(0, bundleName.Length-tailLen)] = bundleName;
                
                if (!_bundleRealNames.ContainsKey(bundleName))
                    _bundleRealNames.Add(bundleName, bundleName);
            }
        }
    }
}