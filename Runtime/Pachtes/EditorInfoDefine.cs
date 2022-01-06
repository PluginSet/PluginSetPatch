#if UNITY_EDITOR
using System;
using PluginSet.Core;
using UnityEngine;

namespace PluginSet.Patch
{
    [Serializable]
    public struct PathInfo
    {
        [FolderDrag]
        [SerializeField]
        public string Path;

        [Tooltip("勾选后会在bundle名称前增加res_前缀，可以使用ResourcesManager.Instance.Load<T>接口来加载资源")]
        [SerializeField]
        public bool UseResourceLoad;

        [Tooltip("勾选后会在bundle名称前增加res_前缀，可以使用ResourcesManager.Instance.Load<T>接口来加载资源")]
        [SerializeField]
        public bool BuildByFile;

        [Tooltip("为空时使用目录名称代替")]
        [SerializeField]
        public string BundleName;

        [HideInInspector]
        public string[] FileList;

        public override string ToString()
        {
            return $"{Path} BundleName={BundleName} UseResourceLoad={UseResourceLoad} BuildByFile={BuildByFile}";
        }
    }

    [Serializable]
    public struct PatchInfo
    {
        [SerializeField]
        public string Name;

        [SerializeField]
        public bool CopyToStream;

        [SerializeField]
        public PathInfo[] Paths;
    }
}
#endif
