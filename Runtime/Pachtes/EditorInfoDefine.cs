#if UNITY_EDITOR
using System;
using PluginSet.Core;
using UnityEngine;

namespace PluginSet.Patch
{
    public enum PathBuildType
    {
        AllInBundle,
        TopDirectory,
        AllDirectories,
    }

    [Serializable]
    public struct PathInfo
    {
        [FolderDrag]
        [SerializeField]
        public string Path;

        [Tooltip("勾选后会在bundle名称前增加res_前缀，可以使用ResourcesManager.Instance.Load<T>接口来加载资源")]
        [SerializeField]
        public bool UseResourceLoad;

        [HideInInspector]
        [SerializeField]
        public bool BuildByFile;

        [Tooltip("选择目录构建Bundle方式：\n" +
                 "AllInBundle:目录中所有资源归入目录bundle中\n" +
                 "TopDirectory:目录中的所有资源按其顶层目录归入相应的bundle中\n" +
                 "AllDirectories:目录中的所有资源按其自有名称单独构建bundle")]
        [SerializeField]
        public PathBuildType BuildType;

        [Tooltip("为空时使用目录名称代替")]
        [SerializeField]
        public string BundleName;

        [HideInInspector]
#if UNITY_2020_2_OR_NEWER
        [NonReorderable]
#endif
        public string[] FileList;

        public override string ToString()
        {
            return $"{Path} BundleName={BundleName} UseResourceLoad={UseResourceLoad} BuildType={BuildType}";
        }
    }

    [Serializable]
    public struct PatchInfo
    {
        [SerializeField]
        public string Name;

        [SerializeField]
        public bool Ignore;

        [SerializeField]
        public bool CopyToStream;

        [SerializeField]
#if UNITY_2020_2_OR_NEWER
        [NonReorderable]
#endif
        public PathInfo[] Paths;
    }
}
#endif
