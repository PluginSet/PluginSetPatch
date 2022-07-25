using System.Collections;
using System.Collections.Generic;
using PluginSet.Core.Editor;
using UnityEngine;


namespace PluginSet.TIM.Editor
{
    [BuildChannelsParams("TIM")]
    public class BuildTIMParams : ScriptableObject
    {
        [Tooltip("是否开启聊天TIMSDK")]
        public bool Enable;

        [Tooltip("TIMSDK提供的应用标识")]
        public long AppId; 
    }
}
