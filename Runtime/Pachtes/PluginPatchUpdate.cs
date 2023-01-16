#if !DISABLE_PATCH_UPDATE
using System.Collections;
using PluginSet.Core;
using UnityEngine;

namespace PluginSet.Patch
{
    [PluginRegister]
    public class PluginPatchUpdate: PluginBase, IStartPlugin
    {
        public override string Name => "PatchUpdate";
        public int StartOrder => PluginsStartOrder.Default - 1;
        
        public bool IsRunning { get; private set; }

        private bool _continueIfUpdateFail;
        private bool _updateCompleted;

        private PatchesDownloader _currentDownloader;

        private string _downloadUrl;
        private string _streamingUrl;

        protected override void Init(PluginSetConfig config)
        {
            var cfg = config.Get<PluginPatchConfig>("Patch");
            _continueIfUpdateFail = cfg.ContinueIfUpdateFail;
        }

        public IEnumerator StartPlugin()
        {
            _updateCompleted = false;
            
            // 开始热更
            SendNotification(PluginConstants.PATCH_NOTIFY_UPDATE_START);
            
            while (!_updateCompleted)
            {
                yield return CheckAndUpdate();
            }
            
            SendNotification(PluginConstants.PATCH_NOTIFY_UPDATE_COMPLETE);

            IsRunning = true;
        }

        private IEnumerator CheckAndUpdate()
        {
            var context = PluginsEventContext.Get(this);
            context.Data = CheckResult.Nothing;

            var opCheck = WaitingForOperation.Get(null, OnCancelUpdate);
            context.Confirm = opCheck.Confirm;
            context.Cancel = opCheck.Cancel;
            if (NotifyAnyOne(PluginConstants.PATCH_NOTIFY_CHECK_UPDATE, context))
            {
                yield return opCheck.Wait();
            }
            WaitingForOperation.Return(opCheck);

            var result = (CheckResult) context.Data;
            if (result == CheckResult.NeedCheck)
            {
                result = CheckNeedUpdate(context);
            }
            
            if (result == CheckResult.DownloadApp)
            {
                _downloadUrl = context.Get<string>(PluginConstants.PATCH_DOWNLOAD_APP_URL);
                
                var operation = WaitingForOperation.Get(OnDownloadApp, OnCancelUpdate);
                context.Confirm = operation.Confirm;
                context.Cancel = operation.Cancel;

                if (NotifyAnyOne(PluginConstants.PATCH_NOTIFY_REQUEST_DOWNLOAD_APP, context))
                {
                    yield return operation.Wait();
                }
                else
                {
                    yield return OnDownloadApp();
                }
                WaitingForOperation.Return(operation);
            }
            else if (result == CheckResult.DownloadPatches)
            {
                _downloadUrl = context.Get<string>(PluginConstants.PATCH_UPDATE_PATCH_URL);
                _streamingUrl = context.Get<string>(PluginConstants.PATCH_STREAMING_URL);
                
                var downloader = CreateDownloader();
                yield return downloader.PrepareDownload();
                
                var operation = WaitingForOperation.Get(OnDownloadPatches, OnCancelUpdate);
                context.Confirm = operation.Confirm;
                context.Cancel = operation.Cancel;
                context.Data = downloader.TotalTaskSize;

                if (downloader.TotalTaskCount > 0 && NotifyAnyOne(PluginConstants.PATCH_NOTIFY_REQUEST_DOWNLOAD_PATCHES, context))
                {
                    yield return operation.Wait();
                }
                else
                {
                    yield return OnDownloadPatches();
                }
                
                WaitingForOperation.Return(operation);
            }
            else if (result != CheckResult.Retry)
            {
                _updateCompleted = true;
            }
            
            PluginsEventContext.Return(context);
        }

        private IEnumerator OnUpdateNetworkError()
        {
            var operation = WaitingForOperation.Get(null, OnCancelUpdate);
            if (NotifyAnyOne(PluginConstants.PATCH_NOTIFY_NET_ERROR, null,
                operation.Confirm, operation.Cancel))
            {
                yield return operation.Wait();
            }
            else
            {
                yield return null;
            }
            WaitingForOperation.Return(operation);
        }

        private CheckResult CheckNeedUpdate(PluginsEventContext context)
        {
            var current = ResourcesManager.ResourceVersion;
            var target = context.Get<string>(PluginConstants.PATCH_VERSION_STRING);
            return PatchUtil.CheckResourceVersion(current, target);
        }

        private IEnumerator OnDownloadApp()
        {
            Application.OpenURL(_downloadUrl);
            yield return OnCancelUpdate();
        }

        private PatchesDownloader CreateDownloader()
        {
            var savePath = ResourcesManager.PatchesSavePath;
            var streamingName = ResourcesManager.Instance.StreamingAssetsName;
            var downloader = new PatchesDownloader(_downloadUrl, _streamingUrl, streamingName, savePath);
            downloader.OnProgress(delegate(float progress)
            {
                SendNotification(PluginConstants.PATCH_NOTIFY_UPDATE_PROGRESS, progress);
            });
            
            _currentDownloader = downloader;

            return downloader;
        }

        private IEnumerator OnDownloadPatches()
        {
            var downloader = _currentDownloader ?? CreateDownloader();
            yield return downloader.StartDownload();
            while (downloader.State != PatchesDownloader.DownloadState.Succeed)
            {
                yield return OnUpdateNetworkError();
                // 取消更新
                if (_updateCompleted)
                    yield break;
                yield return downloader.Retry();
            }

            _currentDownloader = null;
            _updateCompleted = true;
            // 热更完成，需要重启
            SendNotification(PluginConstants.NOTIFY_RESTART);
        }

        private IEnumerator OnCancelUpdate()
        {
            if (_currentDownloader != null)
            {
                _currentDownloader.Stop();
                _currentDownloader = null;
            }
            
            _updateCompleted = true;
            if (!_continueIfUpdateFail)
            {
                Application.Quit();
            }
            yield break;
        }

        public void DisposePlugin(bool isAppQuit = false)
        {
            IsRunning = false;
            _currentDownloader?.OnProgress(null);
        }
    }
}
#endif