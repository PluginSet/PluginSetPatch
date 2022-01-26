using System.Collections;
using PluginSet.Core;
using System.IO;
using Common;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PluginSet.Patch
{
    /// <summary>
    /// 初始化资源管理器，需要在所有其它插件之前完成Start
    /// </summary>
    [PluginRegister]
    public class PluginResourcesInit : PluginBase, IStartPlugin
    {
        public override string Name => "ResourcesInit";

        public int StartOrder => int.MinValue;

        public bool IsRunning { get; private set; }

#if UNITY_EDITOR
        private PatchInfo[] _patchInfos;
        private PathInfo[] _streamPaths;
#endif

        protected override void Init(PluginSetConfig config)
        {
            //ResourcesManager.NewInstance<PatchResourcesManager>();
#if UNITY_EDITOR
            var cfg = config.Get<PluginPatchConfig>("Patch");
            _patchInfos = cfg.Patches;
            _streamPaths = cfg.StreamPaths;
#endif
        }

        public IEnumerator StartPlugin()
        {
            IsRunning = true;
            
#if !UNITY_EDITOR && UNITY_ANDROID
            if (!AndroidHelper.RequestPermissions("android.permission.WRITE_EXTERNAL_STORAGE|android.permission.READ_EXTERNAL_STORAGE"))
            {
                Application.Quit();
                yield break;
            }
#endif

            ResourcesManager.PurgeInstance();
            ResourcesManager.NewInstance<PatchResourcesManager>();
            PatchResourcesManager manager = (PatchResourcesManager) ResourcesManager.Instance;
            if (manager == null)
                yield break;

            string savePath = ResourcesManager.PatchesSavePath;
            string streamingName = manager.StreamingAssetsName;

            var prefix = ResourcesManager.StreamingInternalUrlPrefix;
            var internalRequest = FileManifest.RequestFileManifest(string.Empty, prefix + streamingName);
            yield return internalRequest;
            var fileManifest = internalRequest.result;

            var needRewriteFiles = fileManifest != null;
            string streamingPath = Path.Combine(savePath, streamingName);
            if (File.Exists(streamingPath))
            {
                var writeFileManifest = FileManifest.LoadFileManifest(string.Empty, streamingPath);
                var resourceCheck = PatchUtil.CheckResourceVersion(writeFileManifest.Version, fileManifest?.Version);
                if (resourceCheck != CheckResult.DownloadPatches)
                {
                    needRewriteFiles = false;
                    fileManifest = writeFileManifest;
                }
            }

            if (needRewriteFiles)
            {
                if (!Directory.Exists(savePath))
                {
                    var rootPath = ResourcesManager.PatchesRootPath;
                    if (Directory.Exists(rootPath))
                        Directory.Delete(rootPath, true);

                    Directory.CreateDirectory(savePath);
                }

                // 将APK内部AssetBundle资源转存到可写目录
                Debug.Log(">>>>>>>>开始转移资源");
                var transfer = new PatchesDownloader(prefix, streamingName, savePath);
                transfer.SetTaskProgressUpdate(DownloadProgressUpdated);
                transfer.SetAutoRetry();
                yield return transfer.StartDownload(fileManifest);
                Debug.Log("<<<<<<<<转移资源完成");
            }

            // 初始化资源管理器
#if UNITY_EDITOR
            yield return manager.Init(fileManifest, _patchInfos, _streamPaths);
#else
            yield return manager.Init(fileManifest);
#endif
        }

        private void DownloadProgressUpdated(int current, int max)
        {
            SendNotification(PluginConstants.PROGRESS_UPDATED, new ProgressUpdatedData
            {
                Name = Name,
                Current = current,
                Max = max
            });
        }

        public void DisposePlugin(bool isAppQuit = false)
        {
            IsRunning = false;
            //  释放资源,只需要在重启的时候释放
            if (!isAppQuit)
                ResourcesManager.Instance.ReleaseAll();
        }

#if DEBUG
        private void OnGUI()
        {
            var fontStyle = new UnityEngine.GUIStyle();
            fontStyle.normal.background = null; //设置背景填充
            fontStyle.normal.textColor = new UnityEngine.Color(0, 1, 0); //设置字体颜色
            fontStyle.fontSize = 32;
            var rect = new UnityEngine.Rect(10, 50, 750, 100);
            UnityEngine.GUI.Label(rect, string.Format("Current ABCount: {0}", AssetBundleRef.AssetCount), fontStyle);
        }
#endif
    }
}
