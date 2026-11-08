using MelonLoader;

[assembly: MelonInfo(typeof(NgoPlatesMod.Main), "NgoPlates", "1.0.0", "TOTO")]

namespace NgoPlatesMod
{
    // Animated NGO nameplates (雨極 / 雨将 / 雨神 / 雨舞舞) for maimai DX: the plate data and stills come with the A588
    // option folder, the frames with Package\LocalAssets\NgoPlates. Without this mod the plates still show their stills.
    public sealed class Main : MelonMod
    {
        public override void OnInitializeMelon() => NgoPlates.Initialize();
        public override void OnUpdate() => NgoPlates.Poll();
    }
}
