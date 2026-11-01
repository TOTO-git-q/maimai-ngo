using System.Collections.Generic;

namespace PartnerLive2D.Unity
{
    public sealed class MusicSelectImageOptions
    {
        public string Sprite, Parent;
        public object SiblingIndex = "last";
        public float X, Y, Width = 100, Height = 100;
        public bool Hidden;
        public MusicSelectImageOptions() { }
        public MusicSelectImageOptions(MusicSelectImageOptions prior)
        {
            Sprite = prior.Sprite; Parent = prior.Parent; SiblingIndex = prior.SiblingIndex;
            X = prior.X; Y = prior.Y; Width = prior.Width; Height = prior.Height; Hidden = prior.Hidden;
        }
        public Dictionary<string, object> ToDict() => new Dictionary<string, object>
        {
            { "sprite", Sprite }, { "parent", Parent }, { "siblingIndex", SiblingIndex },
            { "x", X }, { "y", Y }, { "width", Width }, { "height", Height }, { "hidden", Hidden }
        };
        public int ResolveSibling(int childCount) => Equals(SiblingIndex, "first") ? 0
            : Equals(SiblingIndex, "last") ? System.Math.Max(0, childCount - 1) : System.Math.Min((int)SiblingIndex, System.Math.Max(0, childCount - 1));
    }
}
