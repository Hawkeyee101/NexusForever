namespace NexusForever.Game.Static.Entity
{
    /// <summary>
    /// Who or where a creature casts one of its spells at.
    /// </summary>
    public enum CreatureSpellTarget
    {
        /// <summary>
        /// The creature's current target.
        /// </summary>
        Victim,

        /// <summary>
        /// The creature itself.
        /// </summary>
        Self,

        /// <summary>
        /// A random hostile unit on the creature's threat list within the spell's range.
        /// </summary>
        RandomHostileInRange,

        /// <summary>
        /// The position of the creature's current target (ground targeted spells).
        /// </summary>
        VictimPosition
    }
}
