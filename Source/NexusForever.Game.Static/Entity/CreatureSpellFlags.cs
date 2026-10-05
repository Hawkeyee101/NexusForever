namespace NexusForever.Game.Static.Entity
{
    [Flags]
    public enum CreatureSpellFlags
    {
        None       = 0x00,

        /// <summary>
        /// The spell is one of the creature's auto attacks, cast in turn when no other spell is ready.
        /// </summary>
        AutoAttack = 0x01
    }
}
