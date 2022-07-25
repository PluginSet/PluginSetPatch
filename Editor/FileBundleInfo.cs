using System.Collections.Generic;
using System.IO;
using PluginSet.Core;
using UnityEngine;

namespace PluginSet.Patch.Editor
{
    public class FileBundleInfo
    {
            public static PatchDependsBuildType BuildType;
            
            public static Dictionary<string, string> TempBundleMap = new Dictionary<string, string>();
            public string BundleName
            {
                get
                {
                    if (!string.IsNullOrEmpty(_bundleName))
                        return _bundleName;

                    string subPath;
                    string name;
                    if (BuildType == PatchDependsBuildType.BuildWithReference)
                    {
                        // 智能分组 AB数量少，资源加载冗余较，但资源包容易变化
                        if (_references.Count <= 0)
                            return string.Empty;
                        
                        _references.Sort();
                        string key = string.Join<string>("|", _references);
                        if (TempBundleMap.TryGetValue(key, out name))
                            return name;
                        
                        name = PluginUtil.GetMd5(key).Substring(0, 6);
                        TempBundleMap.Add(key, name);
                        return name;
                    }
                    
                    if (BuildType == PatchDependsBuildType.BuildWithSingleFile)
                    {
                        // 单文件分组 AB数量多，IO次数多，无加载冗余
                        subPath = Global.GetSubPath(Application.dataPath, _filePath);
                        name = subPath.Replace("/", "_").Replace(".", "_").ToLower();
                        TempBundleMap.Add(_filePath, name);
                        return name;
                        
                    }
                    
                    // 目录分组 根据文件夹结构区分资源包，可控，AB依赖关系较复杂，加载冗余多
                    var path = Path.GetDirectoryName(_filePath);
                    if (string.IsNullOrEmpty(path))
                        return string.Empty;
                    
                    if (TempBundleMap.TryGetValue(path, out name))
                        return name;

                    subPath = Global.GetSubPath(Application.dataPath, path);
                    name = subPath.Replace("/", "_").ToLower();
                    TempBundleMap.Add(path, name);
                    return name;
                }

                set => _bundleName = value;
            }

            private string _filePath;
            private string _bundleName;

            private List<string> _references = new List<string>();

            public FileBundleInfo(string filePath, string bundleName = null)
            {
                _filePath = filePath;
                _bundleName = bundleName;
            }

            public void AddReference(string flag)
            {
                if (!string.IsNullOrEmpty(_bundleName))
                    return;

                if (_references.Contains(flag))
                    return;
                
                _references.Add(flag);
            }
    }
}