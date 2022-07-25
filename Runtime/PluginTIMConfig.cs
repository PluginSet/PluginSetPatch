using PluginSet.Core;
using UnityEngine;

namespace PluginSet.TIM
{
    [PluginSetConfig("TIM")]
    public class PluginTIMConfig : ScriptableObject
    {
        [Tooltip("TIMSDK提供的应用标识")]
        public long AppId; 
    }
}
