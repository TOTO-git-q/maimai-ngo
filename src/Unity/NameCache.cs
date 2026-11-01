using System.Collections.Generic;

namespace PartnerLive2D.Unity
{
    // UnityEngine.Object.name builds a new managed string on every read. The NGO hooks on Image (sprite setter,
    // OnEnable, OnPopulateMesh) run for every UI image the game touches, many times per frame in gameplay, so reading
    // names there made steady garbage and ever longer collections the longer the game ran. Names are read once per
    // object here (keyed by instance id) and reused.
    static class NameCache
    {
        static readonly Dictionary<int, string> Names = new Dictionary<int, string>();

        public static string Of(UnityEngine.Object o)
        {
            if (o == null) return null;
            int id = o.GetInstanceID();
            string n;
            if (Names.TryGetValue(id, out n)) return n;
            if (Names.Count > 50000) Names.Clear();   // ids of destroyed objects pile up over a long session
            n = o.name;
            Names[id] = n;
            return n;
        }
    }
}
