using HarmonyLib;
using Monitor;
using Process;
using UnityEngine;

namespace PartnerLive2D.Unity
{
    // NGO skin: the attract screen shows only the maimai NEEDY GIRL OVERDOSE title. The SEGA / ALL.Net logos are
    // played at 30x (skipping their clip outright left the circle black: it also brings the title canvas up), and the title's one "InOut" clip is frozen half way, while
    // the title is fully shown (its end is a fade to black, after which the game would go to the song demos).
    // SyncTitle is also turned back into Title as a safety net. Login input is checked at the top of OnUpdate,
    // before the sequence switch, so card / button entry works as usual.
    [HarmonyPatch(typeof(AdvertiseProcess), "OnUpdate")]
    static class NgoTitleHold
    {
        const float HoldAt = .5f;   // normalized time of the title clip to freeze at
        const float LogoSpeed = 30f;
        static readonly AccessTools.FieldRef<AdvertiseProcess, AdvertiseProcess.AdvertiseSequence> State =
            AccessTools.FieldRefAccess<AdvertiseProcess, AdvertiseProcess.AdvertiseSequence>("_state");
        static readonly AccessTools.FieldRef<AdvertiseProcess, AdvertiseMonitor[]> Monitors =
            AccessTools.FieldRefAccess<AdvertiseProcess, AdvertiseMonitor[]>("_monitors");
        static readonly AccessTools.FieldRef<AdvertiseMonitor, Animator> LogoAnimator =
            AccessTools.FieldRefAccess<AdvertiseMonitor, Animator>("_logoAnimator");
        static readonly AccessTools.FieldRef<AdvertiseMonitor, Animator> TitleAnimator =
            AccessTools.FieldRefAccess<AdvertiseMonitor, Animator>("_titleAnimator");

        static void Prefix(AdvertiseProcess __instance)
        {
            ref var s = ref State(__instance);
            if (s == AdvertiseProcess.AdvertiseSequence.SyncTitle) s = AdvertiseProcess.AdvertiseSequence.Title;
            // the SEGA / ALL.Net logo clip also brings the title canvas up, so it cannot be skipped: play it fast
            if (s == AdvertiseProcess.AdvertiseSequence.Logo && Monitors(__instance) != null)
                foreach (var m in Monitors(__instance)) { var a = m == null ? null : LogoAnimator(m); if (a != null && a.speed < LogoSpeed) a.speed = LogoSpeed; }
        }

        static void Postfix(AdvertiseProcess __instance)
        {
            if (State(__instance) != AdvertiseProcess.AdvertiseSequence.Title) return;
            var monitors = Monitors(__instance);
            if (monitors == null) return;
            foreach (var m in monitors)
            {
                if (m == null) continue;
                var anim = TitleAnimator(m);
                if (anim == null || anim.speed == 0f) continue;
                var info = anim.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash == AnimDefine.AnimInOutHash && info.normalizedTime >= HoldAt) anim.speed = 0f;
            }
        }
    }
}
