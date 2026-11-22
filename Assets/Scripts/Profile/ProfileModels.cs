using System;
using System.Collections.Generic;

namespace qp {

    /// <summary>What the server needs to know about a player: the name and what they wear.</summary>
    [Serializable]
    public class PlayerProfile {
        public string name;
        public List<SkinRef> skins = new List<SkinRef>();
    }
}
