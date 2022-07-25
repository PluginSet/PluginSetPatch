using System;
using System.Collections;
using System.Collections.Generic;
using com.tencent.imsdk.unity;
using com.tencent.imsdk.unity.callback;
using com.tencent.imsdk.unity.enums;
using com.tencent.imsdk.unity.types;
using PluginSet.Core;
using UnityEngine;
using Logger = PluginSet.Core.Logger;

namespace PluginSet.TIM
{
    public partial class PluginTIM : PluginBase, IStartPlugin, IChatBase
    {
        [Serializable]
        private struct LoginParams
        {
            [SerializeField]
            public string userId;
            [SerializeField]
            public string userSign;
        }
        
        private static readonly Logger Logger = LoggerManager.GetLogger("TIMSDK");

        private const string PluginName = "TIM";

        private static readonly Result CoveredLoginError = new Result
        {
            Success = false,
            PluginName = PluginName,
            Error = "Covered login",
            Code = PluginConstants.FailDefaultCode,
        };
        
        private static readonly Result LogoutWithoutLoginError = new Result
        {
            Success = false,
            PluginName = PluginName,
            Error = "Logout without login",
            Code = PluginConstants.FailDefaultCode,
        };
        
        public override string Name => PluginName;

        public int StartOrder => PluginsStartOrder.Default + 1;
        
        public bool IsRunning { get; private set; }

        private long sdkAppId;

        private bool _isLogined;
        private string loginParams = null;
        private string loginData = null;

        private string _nextLoginParams = null;
        private Action<Result> _nextLoginCallback = null;
        private Action<Result> _nextLogin = null;

        private readonly List<Action<Result>> _loginCallback = new List<Action<Result>>();
        private readonly List<Action<Result>> _logoutCallback = new List<Action<Result>>();

        private event Action<Result> _newMessageReceiveEvent;

        protected override void Init(PluginSetConfig config)
        {
            sdkAppId = config.Get<PluginTIMConfig>("TIM").AppId;
        }

        public IEnumerator StartPlugin()
        {
            if (!IsRunning)
            {
                var sdkConfig = new SdkConfig();
                sdkConfig.sdk_config_config_file_path = Application.persistentDataPath + "/TIM-Config";
                sdkConfig.sdk_config_log_file_path = Application.persistentDataPath + "/TIM-Log";

                var result = TencentIMSDK.Init(sdkAppId, sdkConfig);
                if (result == TIMResult.TIM_SUCC)
                    IsRunning = true;
                else
                    Logger.Error($"Init Tencent IMSDK fail with result {result}");
                
            }
            
            TencentIMSDK.AddRecvNewMsgCallback(null, delegate(string message, string data)
            {
                _newMessageReceiveEvent?.Invoke(new Result
                {
                    Success = true,
                    PluginName = Name,
                    Code = 0,
                    Data = message,
                });
            });

            yield break;
        }

        public void DisposePlugin(bool isAppQuit = false)
        {
            TencentIMSDK.RemoveRecvNewMsgCallback();
            
            if (_isLogined)
                LogoutChat();

            if (isAppQuit && IsRunning)
            {
                TencentIMSDK.Uninit();
                IsRunning = false;
            }
        }
        
        public void LoginChat(string json, Action<Result> callback = null)
        {
            var status = TencentIMSDK.GetLoginStatus();
            switch (status)
            {
                case TIMLoginStatus.kTIMLoginStatus_Logined:
                    if (string.Equals(loginParams, json))
                    {
                        callback?.Invoke(new Result
                        {
                            Success = true,
                            PluginName = Name,
                            Data = loginData,
                        });
                    }
                    else
                    {
                        SetNextLoginParams(json, callback);
                        LogoutInternal();
                    }
                    break;
                case TIMLoginStatus.kTIMLoginStatus_Logining:
                    if (string.Equals(loginParams, json))
                    {
                        if (callback != null)
                            _loginCallback.Add(callback);
                    }
                    else
                    {
                        SetNextLoginParams(json, callback);
                        if (!_loginCallback.Contains(LogoutAfterLogin))
                            _loginCallback.Add(LogoutAfterLogin);
                    }
                    break;
                case TIMLoginStatus.kTIMLoginStatus_Logouting:
                    SetNextLoginParams(json, callback);
                    break;
                case TIMLoginStatus.kTIMLoginStatus_UnLogined:
                default:
                    if (callback != null)
                        _loginCallback.Add(callback);
                    LoginInternal(json);
                    break;
            }
        }

        public void LogoutChat(Action<Result> callback = null)
        {
            var status = TencentIMSDK.GetLoginStatus();
            switch (status)
            {
                case TIMLoginStatus.kTIMLoginStatus_Logined:
                    if (callback != null)
                        _logoutCallback.Add(callback);
                    SetNextLoginParams(null, null);
                    LogoutInternal();
                    break;
                case TIMLoginStatus.kTIMLoginStatus_Logining:
                    SetNextLoginParams(null, null);
                    if (!_loginCallback.Contains(LogoutAfterLogin))
                        _loginCallback.Add(LogoutAfterLogin);
                    break;
                case TIMLoginStatus.kTIMLoginStatus_Logouting:
                    SetNextLoginParams(null, null);
                    if (callback != null)
                        _logoutCallback.Add(callback);
                    break;
                case TIMLoginStatus.kTIMLoginStatus_UnLogined:
                default:
                    callback?.Invoke(LogoutWithoutLoginError);
                    break;
            }
        }

        public void AddNewMessageReceiveCallback(Action<Result> callback)
        {
            _newMessageReceiveEvent += callback;
        }

        public void RemoveNewMessageReceiveCallback(Action<Result> callback)
        {
            _newMessageReceiveEvent -= callback;
        }

        private TIMResult LoginInternal(string json)
        {
            loginParams = json;
            var param = JsonUtil.FromJson<LoginParams>(json);
            var result = TencentIMSDK.Login(param.userId, param.userSign, delegate(int code, string desc, string data)
                {
                    _isLogined = code == 0;
                    if (_isLogined)
                        loginData = data;
                        
                    var callback = new Result
                    {
                        Success = _isLogined,
                        PluginName = Name,
                        Code = code,
                        Error = desc,
                        Data = data,
                    };
                    
                    foreach (var cb in _loginCallback)
                    {
                        cb.Invoke(callback);
                    }
                    _loginCallback.Clear();
                });

            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
            return result;
        }

        private TIMResult LogoutInternal()
        {
            var result = TencentIMSDK.Logout(delegate(int code, string desc, string data)
            {
                _isLogined = false;
                loginData = null;
                
                var callback = new Result{
                    Success = code == 0,
                    PluginName = Name,
                    Code = code,
                    Error = desc,
                    Data = data,
                };

                foreach (var cb in _logoutCallback)
                {
                    cb.Invoke(callback);
                }
                _logoutCallback.Clear();
            });

            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
            return result;
        }

        private void LogoutAfterLogin(Result _)
        {
            LogoutInternal();
        }

        private void SetNextLoginParams(string json, Action<Result> callback)
        {
            _nextLoginCallback?.Invoke(CoveredLoginError);
            _nextLoginParams = json;
            _nextLoginCallback = callback;
            if (!string.IsNullOrEmpty(json) && _nextLogin == null)
            {
                _nextLogin = delegate(Result result)
                {
                    LoginChat(_nextLoginParams, _nextLoginCallback);
                    _nextLoginParams = null;
                    _nextLoginCallback = null;
                };
                _logoutCallback.Add(_nextLogin);
            }
            else if (string.IsNullOrEmpty(json) && _nextLogin != null)
            {
                if (_logoutCallback.Contains(_nextLogin))
                    _logoutCallback.Remove(_nextLogin);
                _nextLogin = null;
            }
        }

        private static void LoggerResultValue(string funcName, TIMResult result)
        {
            Logger.Info($"Get result {result.ToString()} in func {funcName}");
        }


        private ValueCallback<string> CommonDelegateCallback(Action<Result> callback = null)
        {
            if (callback == null)
                return null;
            
            return delegate(int code, string desc, string data, string userData)
            {
                callback?.Invoke(new Result
                {
                    PluginName = Name,
                    Success = code == 0,
                    Code = code,
                    Error = desc,
                    Data = data,
                });
            };
        }
    }

}
