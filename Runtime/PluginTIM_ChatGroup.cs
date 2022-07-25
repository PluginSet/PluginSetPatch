using System;
using com.tencent.imsdk.unity;
using com.tencent.imsdk.unity.enums;
using com.tencent.imsdk.unity.types;
using Newtonsoft.Json;
using PluginSet.Core;

namespace PluginSet.TIM
{
    public partial class PluginTIM: IChatGroup
    {
        public void CreateGroup(string groupId, string jsonParams, Action<Result> callback = null)
        {
            var param =JsonConvert.DeserializeObject<CreateGroupParam>(jsonParams);
            param.create_group_param_group_id = groupId;
            
            var result = TencentIMSDK.GroupCreate(param, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void DeleteGroup(string groupId, Action<Result> callback = null)
        {
            var result = TencentIMSDK.GroupDelete(groupId, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void DeleteGroupConv(string groupId, Action<Result> callback = null)
        {
            DeleteConv(groupId, TIMConvType.kTIMConv_Group, callback);
        }

        public void JoinGroup(string groupId, Action<Result> callback = null)
        {
            JoinGroup(groupId, "", callback);
        }

        public void JoinGroup(string groupId, string hello, Action<Result> callback = null)
        {
            var result = TencentIMSDK.GroupJoin(groupId, hello, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void QuitGroup(string groupId, Action<Result> callback = null)
        {
            var result = TencentIMSDK.GroupQuit(groupId, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public string SendGroupTextMessage(string convId, string content, Action<Result> callback = null)
        {
            return SendConvTextMessage(convId, TIMConvType.kTIMConv_Group, content, callback);
        }

        public string SendGroupCustomMessage(string convId, string customType, string content, Action<Result> callback = null)
        {
            return SendConvCustomMessage(convId, TIMConvType.kTIMConv_Group, customType, content, callback);
        }

        public void CancelSendGroupMessage(string convId, string messageId, Action<Result> callback = null)
        {
            CancelSendConvMessage(convId, TIMConvType.kTIMConv_Group, messageId, callback);
        }

        public void RevokeGroupMessage(string convId, string messageId, Action<Result> callback = null)
        {
            RevokeConvMessage(convId, TIMConvType.kTIMConv_Group, messageId, callback);
        }

        public void DeleteGroupMessage(string convId, string messageId, Action<Result> callback = null)
        {
            DeleteConvMessage(convId, TIMConvType.kTIMConv_Group, messageId, callback);
        }

        public void GetGroupMessageList(string convId, Action<Result> callback)
        {
            GetConvMessageList(convId, TIMConvType.kTIMConv_Group, null, null, callback);
        }

        public void GetGroupMessageList(string convId, uint maxCount, Action<Result> callback)
        {
            GetConvMessageList(convId, TIMConvType.kTIMConv_Group, maxCount, null, callback);
        }

        public void GetGroupMessageList(string convId, uint maxCount, string lastMessageId, Action<Result> callback)
        {
            GetConvMessageList(convId, TIMConvType.kTIMConv_Group, maxCount, lastMessageId, callback);
        }

        public void ClearGroupHistoryMessage(string convId, Action<Result> callback = null)
        {
            ClearConvHistoryMessage(convId, TIMConvType.kTIMConv_Group, callback);
        }
    }
}