using NexusForever.Game.Static.Entity;

namespace NexusForever.Game.Abstract.Entity.Creature
{
    /// <summary>
    /// A spell a creature casts in combat: one row of the creature → spell list.
    /// </summary>
    /// <remarks>
    /// Only the base AI spells of ordinary creatures; complex fights stay in scripts. Spell4 cooldowns are almost always 0,
    /// so the timing is explicit.
    /// </remarks>
    public interface ICreatureSpell
    {
        uint CreatureId { get; }

        /// <summary>
        /// The spell cast: the root spell, never a proxy.
        /// </summary>
        uint Spell4Id { get; }

        CreatureSpellFlags Flags { get; }

        /// <summary>
        /// First use after entering combat, in milliseconds (random between min and max).
        /// </summary>
        uint InitialDelayMin { get; }
        uint InitialDelayMax { get; }

        /// <summary>
        /// Time between uses, in milliseconds (random between min and max); 0 = once per fight. Ignored for auto attacks.
        /// </summary>
        uint IntervalMin { get; }
        uint IntervalMax { get; }

        /// <summary>
        /// Range the target has to be in; 0 uses the spell's own target range.
        /// </summary>
        float MinRange { get; }
        float MaxRange { get; }

        CreatureSpellTarget Target { get; }

        /// <summary>
        /// Among the spells that are ready, the lowest number is cast first.
        /// </summary>
        byte Priority { get; }

        /// <summary>
        /// A tip (text id) shown on screen to a player the first time they are this spell's target, 0 for none (e.g. Snipe's
        /// "Dash to interrupt a sniper's aim!").
        /// </summary>
        uint TipTextId { get; }

        /// <summary>
        /// The spell's name and where the row comes from.
        /// </summary>
        string Comment { get; }
    }
}
