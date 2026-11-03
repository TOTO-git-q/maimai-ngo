using PartnerLive2D.Core;
using UnityEngine;

namespace PartnerLive2D.Unity
{
    static class RuntimeQuality
    {
        public static QualityPolicy Policy { get; private set; } = new QualityPolicy("high", 0, 0);
        public static void Initialize(string setting)
        {
            Policy = new QualityPolicy(setting, SystemInfo.graphicsMemorySize, SystemInfo.processorCount);
            Mod.Msg("[quality] " + (Policy.Low ? "low" : "high") + ": " + Policy.Reason);
        }
    }
}
