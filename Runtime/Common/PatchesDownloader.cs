using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PluginSet.Core;
using UnityEngine;

namespace PluginSet.Patch
{
    public class PatchesDownloader
    {
        public enum DownloadState
        {
            DownloadFail,
            Failure,
            Succeed,
        }

        private const int DefaultMaxDownloaderCount = 5;

        private string _urlPrefix;
        private string _streamingUrl;
        private string _streamingName;
        private string _savePath;
        private TaskLimiter _tasks;

        private DownloadState _state;

        private bool _completed;
        private bool _downloading;
        private int _downloadedCount;
        private ulong _downloadedSize;
        private int _totalTaskCount;
        private ulong _totalTaskSize;

        private Action<float> _progress;

        public DownloadState State => _state;
        public bool IsCompleted => _completed;
        public bool IsDownloading => _downloading;

        public int TotalTaskCount => _totalTaskCount;

        public int DownloadedCount => _downloadedCount;

        public double TotalTaskSize => _totalTaskSize;

        public int TimeOut = 0;

        public double DownloadedSize
        {
            get
            {
                var total = _downloadedSize;
                foreach (var task in _tasks.Iter)
                {
                    if (task is FileDownloader d)
                        total += d.downloadedBytes;
                }

                return total;
            }
        }

        private List<IAsyncOperationTask> _failedTasks = new List<IAsyncOperationTask>();

        private bool _autoRetry;

        private FileManifest _fileManifest;

        public PatchesDownloader(string urlPrefix, string streaming, string savePath, int maxDownloaderCount = DefaultMaxDownloaderCount)
        {
            _urlPrefix = urlPrefix;
            _streamingName = streaming;
            _savePath = savePath;
            _tasks = new TaskLimiter(maxDownloaderCount);
        }

        public PatchesDownloader(string urlPrefix, string streamingUrl, string streaming, string savePath, int maxDownloaderCount = DefaultMaxDownloaderCount)
            : this(urlPrefix, streaming, savePath, maxDownloaderCount)
        {
            _streamingUrl = urlPrefix + streamingUrl;
        }

        public void SetTaskProgressUpdate(Action<int, int> downloadProgressUpdated)
        {
            _tasks.OnTaskProgressUpdate += downloadProgressUpdated;
        }

        public void SetAutoRetry()
        {
            _autoRetry = true;
        }

        public void OnProgress(Action<float> progress)
        {
            _progress = progress;
        }

        public IEnumerator PrepareDownload()
        {
            _state = DownloadState.Failure;

            // 首先尝试加载 manifest
            var streamingUrl = _streamingUrl;
            if (string.IsNullOrEmpty(streamingUrl))
            {
                Debug.LogError("PrepareDownload need set a streamingUrl");
                yield break;
            }

            var request = FileManifest.RequestFileManifest(string.Empty, streamingUrl);
            yield return request;

            var fileManifest = request.result;
            if (fileManifest == null)
            {
                Debug.LogWarning($"Cannot download {_streamingName}:{streamingUrl}");
                yield break;
            }

            yield return PrepareDownload(fileManifest);
        }

        public IEnumerator PrepareDownload(FileManifest fileManifest)
        {
            _state = DownloadState.Failure;

            _totalTaskCount = 0;
            _totalTaskSize = 0;
            _downloadedCount = 0;
            _downloadedSize = 0;

            yield return fileManifest.LoadManifestAsync();

            if (fileManifest.Manifest == null)
            {
                Debug.LogError($"Why AssetBundle:{_streamingName} has no AssetBundleManifest!?");
                yield break;
            }

            // 默认所有任务成功
            _fileManifest = fileManifest;

            // 检测存储目录
            if (!Directory.Exists(_savePath))
                Directory.CreateDirectory(_savePath);

            // 创建所有下载任务
            foreach (var fileInfo in fileManifest.AllFileInfo)
            {
                var fileName = fileInfo.FileName;
                var filePath = Path.Combine(_savePath, fileName);
                if (File.Exists(filePath))
                {
                    var val = fileInfo;
                    if (PatchUtil.CheckFileInfo(filePath, ref val))
                        continue;

                    File.Delete(filePath);
                }

                var fileUrl = _urlPrefix + fileName;
                var downloader = new FileDownloader(fileUrl, filePath, fileInfo, TimeOut);
                downloader.OnSuccess(OnFileDownloadSuccess);
                downloader.OnError(OnFileDownloadFailure);
                _tasks.PushTask(downloader, false);
                _totalTaskCount += 1;
                _totalTaskSize += (ulong) fileInfo.Size;
            }
        }

        public IEnumerator Start()
        {
            // 等待所有下载任务完成
            _state = DownloadState.Succeed;
            yield return _tasks.Start();

            if (_state != DownloadState.Succeed)
            {
                Debug.Log("patches updater download fail");
                yield break;
            }

            yield return OnAllDownloadCompleted();
        }

        public IEnumerator StartDownload()
        {
            _completed = false;
            _downloading = true;
            if (_fileManifest == null)
                yield return PrepareDownload();

            yield return Start();
            _downloading = false;
            _completed = true;
        }

        public IEnumerator StartDownload(FileManifest fileManifest)
        {
            _completed = false;
            _downloading = true;
            yield return PrepareDownload(fileManifest);
            yield return Start();
            _downloading = false;
            _completed = true;
        }

        public IEnumerator Retry()
        {
            if (_state == DownloadState.Failure)
            {
                yield return StartDownload();
            }
            else
            {
                foreach (var task in _failedTasks)
                {
                    _tasks.PushTask(task);
                }

                _state = DownloadState.Succeed;
                yield return _tasks.Start();

                if (_state != DownloadState.Succeed)
                {
                    Debug.Log("patches updater download fail");
                    yield break;
                }

                yield return OnAllDownloadCompleted();
            }
        }

        public void Pause()
        {
            _tasks.IsPause = true;
        }

        public void Resume()
        {
            _tasks.IsPause = false;
        }

        public void Stop()
        {
            _state = DownloadState.Failure;
            _tasks.Clear();
            _failedTasks.Clear();
        }

        private IEnumerator OnAllDownloadCompleted()
        {
            // 所有下载完成后，重新存储streaming文件
            if (_fileManifest == null)
                yield break;

            yield return CheckSubPatches(_savePath, _fileManifest);

            var streamingPath = Path.Combine(_savePath, _streamingName);
            _fileManifest.SaveTo(streamingPath);
        }

        private void OnFileDownloadSuccess(FileDownloader downloader)
        {
            downloader.OffError(OnFileDownloadFailure);
            downloader.OffSuccess(OnFileDownloadSuccess);
            _downloadedCount += 1;
            _downloadedSize += downloader.downloadedBytes;
            _progress?.Invoke(_downloadedCount / (float) _totalTaskCount);
        }

        private void OnFileDownloadFailure(FileDownloader downloader, string error)
        {
            Debug.LogWarning($"Download file {downloader.Url} fail: {error}");
            if (_autoRetry)
            {
                // 重新下载
                _tasks.PushTask(downloader);
            }
            else
            {
                _failedTasks.Add(downloader);
                _state = DownloadState.DownloadFail;
            }
        }

        private static IEnumerator CheckSubPatches(string savePath, FileManifest manifest)
        {
            var subPatches = manifest?.SubPatches;
            if (subPatches == null || subPatches.Length <= 0)
                yield break;

            var version = manifest.Version;
            if (string.IsNullOrEmpty(version))
                yield break;

            if (!Directory.Exists(savePath))
                Directory.CreateDirectory(savePath);

            foreach (var patch in subPatches)
            {
                if (!manifest.GetFileInfo(patch, out var fileInfo))
                    continue;

                var srcPath = Path.Combine(savePath, fileInfo.FileName);
                var destPath = Path.Combine(savePath, patch);
                yield return CheckSubPatchFile(srcPath, destPath, version);
            }
        }

        private static IEnumerator CheckSubPatchFile(string srcPath, string destPath, string version)
        {
            if (!File.Exists(srcPath))
                yield break;

            if (!File.Exists(destPath))
            {
                File.Copy(srcPath, destPath);
                yield break;
            }

            var currentManifest = FileManifest.LoadFileManifest("temp", destPath);
            var currentVersion = currentManifest.Version;
            var needReWrite = true;
            if (!string.IsNullOrEmpty(currentVersion))
            {
                var check = PatchUtil.CheckResourceVersion(currentVersion, version);
                if (check == CheckResult.Nothing)
                    needReWrite = false;
            }

            if (needReWrite)
                File.Copy(srcPath, destPath, true);
        }
    }
}
