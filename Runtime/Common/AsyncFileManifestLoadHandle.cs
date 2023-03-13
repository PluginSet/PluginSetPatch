using PluginSet.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace PluginSet.Patch
{
    public class AsyncFileManifestLoadHandle: AsyncOperationHandle<FileManifest>
    {
        public static AsyncFileManifestLoadHandle Create(string name, string url)
        {
            var handle = new AsyncFileManifestLoadHandle();
            handle.StartLoad(name, url);
            return handle;
        }

        private string _name;
        private UnityWebRequest _request;

        public override float progress => isDone ? 1 : _request?.downloadProgress ?? 0;

        private void StartLoad(string name, string url)
        {
            _name = name;
            var downloadHandler = new DownloadHandlerBuffer();
            var request = UnityWebRequest.Get(url);
            request.downloadHandler = downloadHandler;
            _request = request;
            
            var handler = request.SendWebRequest();
            handler.completed += OnFileBufferDownloaded;
        }

        private void OnFileBufferDownloaded(AsyncOperation request)
        {
            request.completed -= OnFileBufferDownloaded;

            if (_request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"LoadFileManifest HttpError:{_request.error}");
                result = null;
            }
            else
            {
                result = new FileManifest(_name, _request.downloadHandler.data);
            }
            
            isDone = true;
            _request = null;
            InvokeCompletionEvent();
        }
    }
}