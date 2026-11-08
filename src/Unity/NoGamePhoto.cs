using HarmonyLib;
using Manager;

namespace PartnerLive2D.Unity
{
    // settings.ini `gamePhoto = off` (owner 2026-11-05): no commemorative photo. With the account's photo consent on, the
    // game turns the webcam on at song start, shoots during the song and once more at the end (camera bear + shutter
    // sound), then offers the shots at the end of the credit (PhotoEditProcess). All of it reads GameManager.IsPhotoAgree,
    // which RegionalSelectProcess sets from the account bit; GameProcess calls PhotoManager.Initialize right before its
    // first check, so clearing the runtime flag there stops the whole chain. The account bit itself is left alone.
    static class NoGamePhoto
    {
        public static bool Enabled;

        [HarmonyPatch(typeof(Process.PhotoManager), "Initialize")]
        static class AtSongStart
        {
            static void Prefix()
            {
                if (!Enabled || !GameManager.IsPhotoAgree) return;
                GameManager.IsPhotoAgree = false;
                Mod.Msg("[photo] commemorative photo off for this credit (gamePhoto = off)");
            }
        }
    }
}
