namespace PartnerLive2D.Unity
{
    interface IMusicSelectTouchPartner { void TouchArea(string area); }
    // The observing router is shared by Live2D and the game's stock presentation.
    interface IMusicSelectPartnerReactions
    {
        void LookAt(float x, float y, float seconds);
        void Nod();
        void SelectSongReaction();
    }
}
