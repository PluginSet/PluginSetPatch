using System;
using System.Collections.Generic;
using com.tencent.imsdk.unity;
using com.tencent.imsdk.unity.types;
using Newtonsoft.Json;
using PluginSet.Core;

namespace PluginSet.TIM
{
    public partial class PluginTIM: IProfile
    {
        public void GetUserProfileList(List<string> userIds, Action<Result> callback)
        {
            var param = new FriendShipGetProfileListParam();
            param.friendship_getprofilelist_param_identifier_array = userIds;
            param.friendship_getprofilelist_param_force_update = true;
            TencentIMSDK.ProfileGetUserProfileList(param, CommonDelegateCallback(callback));
        }

        public void GetUserProfileList(string jsonUserIds, Action<Result> callback)
        {
            GetUserProfileList(JsonConvert.DeserializeObject<List<string>>(jsonUserIds), callback);
        }

        public void ModifySelfUserProfile(string json, Action<Result> callback = null)
        {
            var userProfileItem = JsonConvert.DeserializeObject<UserProfileItem>(json);
            TencentIMSDK.ProfileModifySelfUserProfile(userProfileItem, CommonDelegateCallback(callback));
        }

        
    }
}