using UnityEngine;

namespace qp {

    /// <summary>
    /// One thing in a gift. A feature lists its rewards in its own config; the gift code never
    /// asks what kind a reward is — it gives it, takes its card and lets it fill that card.
    /// A new kind of reward is a new class and a new card prefab, and nothing else changes.
    /// </summary>
    public interface IReward {

        /// <summary>Give it to the player. Returns the reward as it was actually given — itself
        /// for most, a copy that knows which item it turned out to be for a random one — or null
        /// when there was nothing to give.</summary>
        IReward Grant();

        /// <summary>The prefab that shows this reward in a gift. Each kind has its own: they do
        /// not look alike, and one card with parts switched on and off would know about them all.</summary>
        GameObject Card { get; }

        /// <summary>Fill an instance of <see cref="Card"/> with this reward.</summary>
        void Dress(GameObject card);

        /// <summary>The player can put it on right away — a gift holding one offers "Use".</summary>
        bool IsUsable { get; }

        /// <summary>Put it on. Does nothing for a reward that is not usable.</summary>
        void Use();
    }
}
