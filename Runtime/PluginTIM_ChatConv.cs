using System;
using System.Collections.Generic;
using System.Text;
using com.tencent.imsdk.unity;
using com.tencent.imsdk.unity.enums;
using com.tencent.imsdk.unity.types;
using PluginSet.Core;

namespace PluginSet.TIM
{
    public partial class PluginTIM: IChatConv
    {
        private static readonly Result MessageNotFoundError = new Result
        {
            Success = false,
            PluginName = PluginName,
            Error = "Message not found",
            Code = PluginConstants.FailDefaultCode,
        };
        
        public void DeleteConv(string convId, Action<Result> callback = null)
        {
            DeleteConv(convId, TIMConvType.kTIMConv_C2C, callback);
        }
        
        private void DeleteConv(string convId, TIMConvType type, Action<Result> callback = null)
        {
            var result = TencentIMSDK.ConvDelete(convId, type, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void GetConvList(Action<Result> callback)
        {
            var result = TencentIMSDK.ConvGetConvList(CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public string SendConvTextMessage(string convId, string content, Action<Result> callback = null)
        {
            return SendConvTextMessage(convId, TIMConvType.kTIMConv_C2C, content, callback);
        }
        
        private string SendConvTextMessage(string convId, TIMConvType type, string content, Action<Result> callback = null)
        {
            var msgElem = new Elem();
            msgElem.elem_type = TIMElemType.kTIMElem_Text;
            msgElem.text_elem_content = content;

            return SendMessage(convId, type, msgElem, callback);
        }
        
        public string SendConvCustomMessage(string convId, string customType, string content, Action<Result> callback = null)
        {
            return SendConvCustomMessage(convId, TIMConvType.kTIMConv_C2C, customType, content, callback);
        }

        private string SendConvCustomMessage(string convId, TIMConvType type, string customType, string content, Action<Result> callback = null)
        {
            var msgElem = new Elem();
            msgElem.elem_type = TIMElemType.kTIMElem_Custom;
            msgElem.custom_elem_data = content;
            msgElem.custom_elem_desc = customType;
            
            return SendMessage(convId, type, msgElem, callback);
        }

        public void CancelSendConvMessage(string convId, string messageId, Action<Result> callback = null)
        {
            CancelSendConvMessage(convId, TIMConvType.kTIMConv_C2C, messageId, callback);
        }
        
        private void CancelSendConvMessage(string convId, TIMConvType type, string messageId, Action<Result> callback = null)
        {
            var result = TencentIMSDK.MsgCancelSend(convId, type, messageId,
                CommonDelegateCallback(callback));
            
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void RevokeConvMessage(string convId, string messageId, Action<Result> callback = null)
        {
            RevokeConvMessage(convId, TIMConvType.kTIMConv_C2C, messageId, callback);
        }
        
        private void RevokeConvMessage(string convId, TIMConvType type, string messageId, Action<Result> callback = null)
        {
            FindMessage(messageId, delegate(Message message)
            {
                var result = TencentIMSDK.MsgRevoke(convId, type, message,
                    CommonDelegateCallback(callback));
                
                LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
            }, callback);
        }
        
        public void DeleteConvMessage(string convId, string messageId, Action<Result> callback = null)
        {
            DeleteConvMessage(convId, TIMConvType.kTIMConv_C2C, messageId, callback);
        }

        public void DeleteConvMessage(string convId, TIMConvType type, string messageId, Action<Result> callback = null)
        {
            FindMessage(messageId, delegate(Message message)
            {
                var param = new MsgDeleteParam();
                param.msg_delete_param_msg = message;
                param.msg_delete_param_is_remble = true;
                var result = TencentIMSDK.MsgDelete(convId, type, param,
                    CommonDelegateCallback(callback));
                
                LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
            }, callback);
        }

        public void GetConvMessageList(string convId, Action<Result> callback)
        {
            GetConvMessageList(convId, TIMConvType.kTIMConv_C2C, null, null, callback);
        }

        public void GetConvMessageList(string convId, uint maxCount, Action<Result> callback)
        {
            GetConvMessageList(convId, TIMConvType.kTIMConv_C2C, maxCount, null, callback);
        }

        public void GetConvMessageList(string convId, uint maxCount, string lastMessageId, Action<Result> callback)
        {
            GetConvMessageList(convId, TIMConvType.kTIMConv_C2C, maxCount, lastMessageId, callback);
        }

        private void GetConvMessageList(string convId, TIMConvType type, uint? maxCount, string lastMessageId, Action<Result> callback)
        {
            if (string.IsNullOrEmpty(lastMessageId))
            {
                var param = new MsgGetMsgListParam();
                param.msg_getmsglist_param_count = maxCount;
                TencentIMSDK.MsgGetMsgList(convId, type, param, CommonDelegateCallback(callback));
            }
            else
            {
                FindMessage(lastMessageId, delegate(Message message)
                {
                    var param = new MsgGetMsgListParam();
                    param.msg_getmsglist_param_count = maxCount;
                    param.msg_getmsglist_param_last_msg = message;
                    TencentIMSDK.MsgGetMsgList(convId, type, param, CommonDelegateCallback(callback));
                }, callback);
            }
        }

        public void ClearConvHistoryMessage(string convId, Action<Result> callback = null)
        {
            ClearConvHistoryMessage(convId, TIMConvType.kTIMConv_C2C, callback);
        }
        
        private void ClearConvHistoryMessage(string convId, TIMConvType type, Action<Result> callback = null)
        {
            TencentIMSDK.MsgClearHistoryMessage(convId, type, CommonDelegateCallback(callback));
        }

        private string SendMessage(string convId, TIMConvType type, Elem msgElem, Action<Result> callback)
        {
            var message = new Message();
            message.message_conv_id = convId;
            message.message_conv_type = type;
            var list = new List<Elem> {msgElem};
            message.message_elem_array = list;
            
            var messageId = new StringBuilder(128);
            var result = TencentIMSDK.MsgSendMessage(convId, type, message, messageId,
                CommonDelegateCallback(callback));
            
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
            return messageId.ToString();
        }

        private void FindMessage(string messageId, Action<Message> callback, Action<Result> fail = null)
        {
            var list = new List<string> {messageId};
            var result = TencentIMSDK.MsgFindMessages(list,
                delegate(int code, string desc, List<Message> data, string userData)
                {
                    if (data.Count > 0)
                        callback.Invoke(data[0]);
                    else
                        fail?.Invoke(MessageNotFoundError);
                });
            
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }
    }
}