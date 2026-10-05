namespace NexusForever.Game.Static.Entity
{
    [Flags]
    public enum CreatureSpellFlags
    {
        None       = 0x00,

        /// <summary>
        /// The spell is one of the creature's auto attacks, cast in turn when no other spell is ready.
        /// </summary>
        AutoAttack    = 0x01,

        /// <summary>
        /// The spell isn't cast: when the AI picks it, the public event scripts on the map are told instead and handle it
        /// (e.g. a call for reinforcements the engine can't summon).
        /// </summary>
        ScriptHandled = 0x02,

        /// <summary>
        /// The cast is interrupted when its target dashes during it (e.g. Snipe: "Dash to interrupt a sniper's aim!").
        /// </summary>
        InterruptOnTargetDash = 0x04
    }
}
