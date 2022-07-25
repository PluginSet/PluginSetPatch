using PluginSet.Core;
using UnityEngine;

namespace PluginSet.Patch
{
    public class AsyncResourceLoadHandle<T>: AsyncOperationHandle<T> where T: Object
    {
        public static AsyncResourceLoadHandle<T> Create(string path)
        {
            var handle = new AsyncResourceLoadHandle<T>();
            handle.StartLoad(path);
            return handle;
        }

        private ResourceRequest _request;

        public override float progress => isDone ? 1 : _request?.progress ?? 0;

        private void StartLoad(string path)
        {
            var request = Resources.LoadAsync<T>(path);
            _request = request;
            request.completed += OnResourceLoadCompleted;
        }

        private void OnResourceLoadCompleted(AsyncOperation request)
        {
            request.completed -= OnResourceLoadCompleted;
            OnLoadAsset(((ResourceRequest) request).asset as T);
        }

        private void OnLoadAsset(T asset)
        {
            result = asset;
            isDone = true;
            _request = null;
            InvokeCompletionEvent();
        }
    }
}