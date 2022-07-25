using System;
using System.IO;
using PluginSet.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace PluginSet.Patch
{

    public class CertHandler : CertificateHandler {
        protected override bool ValidateCertificate (byte[] certificateData) {
            return true;
        }
    }
    
    public class FileDownloader: IStopableAsyncOperationTask
    {
        protected event Action<FileDownloader, string> OnDownloadError;
        
        protected event Action<FileDownloader> OnDownloadSuccess;
        
        protected string _url;
        protected string _savePath;
        protected int _errorTimes;
        protected bool _completed = false;
        protected FileManifest.FileInfo? _fileInfo;
        protected int _size;
        protected int _timeOut;

        protected UnityWebRequest _request;

        public string Url => _url;
        public string SavePath => _savePath;
        public int ErrorTimes => _errorTimes;

        public float downloadProgress => _completed ? 1 : (_request?.downloadProgress ?? 0);
        public ulong downloadedBytes => _completed ? (ulong)_size : (_request?.downloadedBytes ?? 0);

        public bool Completed => _completed;

        public FileDownloader(string url, string savePath, int timeOut = 0)
        {
            _url = url;
            _savePath = savePath;
            _errorTimes = 0;
            _timeOut = timeOut;
        }
        
        public FileDownloader(string url, string savePath, FileManifest.FileInfo fileInfo, int timeOut = 0)
            :this(url, savePath, timeOut)
        {
            _fileInfo = fileInfo;
        }

        public void OnError(Action<FileDownloader, string> onerror)
        {
            OnDownloadError += onerror;
        }
        
        public void OffError(Action<FileDownloader, string> onerror)
        {
            OnDownloadError -= onerror;
        }

        public void OnSuccess(Action<FileDownloader> success)
        {
            OnDownloadSuccess += success;
        }

        public void OffSuccess(Action<FileDownloader> success)
        {
            OnDownloadSuccess -= success;
        }

        public void Stop()
        {
            if (_request == null)
                return;
            
            _request.Abort();
            _request.Dispose();
            _request = null;
        }

        public AsyncOperationHandle Start()
        {
            var downloadHandler = new DownloadHandlerFile(_savePath);
            downloadHandler.removeFileOnAbort = true;
            var request = new UnityWebRequest(_url, "GET", downloadHandler, null);
            request.certificateHandler = new CertHandler();
            if (_timeOut > 0)
                request.timeout = _timeOut;
            
            AsyncOperation operation = request.SendWebRequest();
            _request = request;
            operation.completed += OnCompleted;
            return AsyncOperationHandle.Create(operation);
        }

        protected virtual void OnCompleted(AsyncOperation operation)
        {
            operation.completed -= OnCompleted;
            _request = null;
            
            var request = ((UnityWebRequestAsyncOperation) operation).webRequest;
            if (request.isHttpError || request.isNetworkError)
            {
                _errorTimes += 1;
                OnDownloadError?.Invoke(this, request.error);
            }
            else if (CheckDownloadedFile(request))
            {
                _completed = true;
                OnDownloadSuccess?.Invoke(this);
            }
            else
            {
                _errorTimes += 1;
                OnDownloadError?.Invoke(this, "Downloaded file not match!");
            }
        }

        protected bool CheckDownloadedFile(UnityWebRequest request)
        {
            if (!_fileInfo.HasValue)
            {
                _size = File.ReadAllBytes(_savePath).Length;
                return true;
            }

            if (!File.Exists(_savePath))
                return false;

            var val = _fileInfo.Value;
            if (!PatchUtil.CheckFileInfo(_savePath, ref val))
            {
                File.Delete(_savePath);
                return false;
            }

            _size = _fileInfo.Value.Size;
            return true;
        }
    }
}