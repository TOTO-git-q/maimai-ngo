namespace PartnerLive2D.Unity
{
    // Pure input arbiter, shared verbatim with the headless input doubles.
    public sealed class MusicSelectCategoryWindow
    {
        public const double DelayMs = 70;
        public static bool CanSample(bool enabled, bool valid, bool sharedLocked, bool playerLocked)
            => enabled && valid && !sharedLocked && !playerLocked;
        int _pending;
        double _since;
        public void Reset() { _pending = 0; }
        public int Sample(double now, bool eligible, bool rightDown, bool leftDown,
            bool rightHeld, bool leftHeld, bool touchRight, bool touchLeft)
        {
            if (!eligible) { Reset(); return 0; }
            // Cancel even after the first button was released. Never consume game input.
            if ((rightDown && leftDown) || (rightHeld && leftHeld)
                || (_pending != 0 && now - _since <= DelayMs && (_pending > 0 ? leftDown : rightDown)))
            { Reset(); return 0; }
            if (touchRight || touchLeft)
            {
                Reset();
                return touchRight == touchLeft ? 0 : touchRight ? 1 : -1;
            }
            if (_pending != 0 && now - _since >= DelayMs)
            { int result = _pending; Reset(); return result; }
            if (rightDown || leftDown)
            { _pending = rightDown ? 1 : -1; _since = now; }
            return 0;
        }
    }
}
