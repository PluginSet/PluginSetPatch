using System;
using System.Collections.Generic;
using com.tencent.imsdk.unity;
using com.tencent.imsdk.unity.enums;
using com.tencent.imsdk.unity.types;
using Newtonsoft.Json;
using PluginSet.Core;

namespace PluginSet.TIM
{
    public partial class PluginTIM: IFriendShip
    {
        public void ModifyFriendProfile(string userId, string json, Action<Result> callback = null)
        {
            var profileItem = JsonConvert.DeserializeObject<FriendProfileItem>(json);
            var param = new FriendshipModifyFriendProfileParam
            {
                friendship_modify_friend_profile_param_identifier = userId,
                friendship_modify_friend_profile_param_item = profileItem
            };
            var result = TencentIMSDK.FriendshipModifyFriendProfile(param, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void GetFriendProfileList(Action<Result> callback)
        {
            var result = TencentIMSDK.FriendshipGetFriendProfileList(CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void GetFriendsInfo(List<string> userIds, Action<Result> callback)
        {
            var result = TencentIMSDK.FriendshipGetFriendsInfo(userIds, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void GetFriendsInfo(string jsonUserIds, Action<Result> callback)
        {
            GetFriendsInfo(JsonConvert.DeserializeObject<List<string>>(jsonUserIds), callback);
        }

        public void AddFriend(string userId, string json, Action<Result> callback = null)
        {
            var param = JsonConvert.DeserializeObject<FriendshipAddFriendParam>(json);
            param.friendship_add_friend_param_identifier = userId;
            var result = TencentIMSDK.FriendshipAddFriend(param, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void DeleteFriend(List<string> userIds, bool both, Action<Result> callback = null)
        {
            var param = new FriendshipDeleteFriendParam
            {
                friendship_delete_friend_param_identifier_array = userIds,
                friendship_delete_friend_param_friend_type =
                    both ? TIMFriendType.FriendTypeBoth : TIMFriendType.FriendTypeSignle
            };
            var result = TencentIMSDK.FriendshipDeleteFriend(param, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void DeleteFriend(string jsonUserIds, bool both, Action<Result> callback = null)
        {
            DeleteFriend(JsonConvert.DeserializeObject<List<string>>(jsonUserIds), both, callback);
        }

        public void CheckFriendType(List<string> userIds, bool both, Action<Result> callback)
        {
            var param = new FriendshipCheckFriendTypeParam();
            param.friendship_check_friendtype_param_identifier_array = userIds;
            param.friendship_check_friendtype_param_check_type =
                both ? TIMFriendType.FriendTypeBoth : TIMFriendType.FriendTypeSignle;
            var result = TencentIMSDK.FriendshipCheckFriendType(param, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void CheckFriendType(string jsonUserIds, bool both, Action<Result> callback)
        {
            CheckFriendType(JsonConvert.DeserializeObject<List<string>>(jsonUserIds), both, callback);
        }

        public void AddToBlackList(List<string> userIds, Action<Result> callback = null)
        {
            var result = TencentIMSDK.FriendshipAddToBlackList(userIds, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void AddToBlackList(string jsonUserIds, Action<Result> callback = null)
        {
            AddToBlackList(JsonConvert.DeserializeObject<List<string>>(jsonUserIds), callback);
        }

        public void GetBlackList(Action<Result> callback)
        {
            var result = TencentIMSDK.FriendshipGetBlackList(CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void DeleteFromBlackList(List<string> userIds, Action<Result> callback = null)
        {
            var result = TencentIMSDK.FriendshipDeleteFromBlackList(userIds, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void DeleteFromBlackList(string jsonUserIds, Action<Result> callback = null)
        {
            DeleteFromBlackList(JsonConvert.DeserializeObject<List<string>>(jsonUserIds), callback);
        }

        public void GetRequestList(Action<Result> callback)
        {
            var param = new FriendshipGetPendencyListParam
            {
                friendship_get_pendency_list_param_type = TIMFriendPendencyType.FriendPendencyTypeComeIn,
                friendship_get_pendency_list_param_limited_size = int.MaxValue
            };
            var result = TencentIMSDK.FriendshipGetPendencyList(param, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void DeleteRequest(List<string> requestIds, Action<Result> callback = null)
        {
            var param = new FriendshipDeletePendencyParam();
            param.friendship_delete_pendency_param_identifier_array = requestIds;
            param.friendship_delete_pendency_param_type = TIMFriendPendencyType.FriendPendencyTypeComeIn;
            var result = TencentIMSDK.FriendshipDeletePendency(param, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void DeleteRequest(string jsonRequestIds, Action<Result> callback = null)
        {
            DeleteRequest(JsonConvert.DeserializeObject<List<string>>(jsonRequestIds), callback);
        }

        public void HandleAgreeRequest(string userId, string json, Action<Result> callback = null)
        {
            var param = JsonConvert.DeserializeObject<FriendRespone>(json);
            param.friend_respone_identifier = userId;
            param.friend_respone_action = TIMFriendResponseAction.ResponseActionAgree;
            var result = TencentIMSDK.FriendshipHandleFriendAddRequest(param, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void HandleAgreeAndAddRequest(string userId, string json, Action<Result> callback = null)
        {
            var param = JsonConvert.DeserializeObject<FriendRespone>(json);
            param.friend_respone_identifier = userId;
            param.friend_respone_action = TIMFriendResponseAction.ResponseActionAgreeAndAdd;
            var result = TencentIMSDK.FriendshipHandleFriendAddRequest(param, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }

        public void HandleRejectRequest(string userId, string json, Action<Result> callback = null)
        {
            var param = JsonConvert.DeserializeObject<FriendRespone>(json);
            param.friend_respone_identifier = userId;
            param.friend_respone_action = TIMFriendResponseAction.ResponseActionReject;
            var result = TencentIMSDK.FriendshipHandleFriendAddRequest(param, CommonDelegateCallback(callback));
            LoggerResultValue(System.Reflection.MethodBase.GetCurrentMethod().Name, result);
        }
    }
}