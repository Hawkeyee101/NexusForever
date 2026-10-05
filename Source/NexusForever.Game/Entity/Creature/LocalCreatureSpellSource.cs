using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Static.Entity;

namespace NexusForever.Game.Entity.Creature
{
    /// <summary>
    /// Creature → spell rows kept in code until there is a database table for them (local, Hycrest enemies first).
    /// </summary>
    /// <remarks>
    /// Spells and creatures from the tables and Jabbithole's ability lists (docs research/enemy-spells-plan.md and
    /// enemy-spells-list.md). The timing isn't in the client tables (Spell4 cooldowns are almost always 0): every "first"
    /// and "every" below is a guess from enemy-spells-list.md ("Guess: first use / every"), to tune after Teun's video
    /// check. Each use is spread by <see cref="FirstJitter"/> / <see cref="EveryJitter"/> so a group doesn't fire in step.
    /// </remarks>
    public class LocalCreatureSpellSource : ICreatureSpellSource
    {
        // spread around the guessed times, in seconds (a guess too)
        private const double FirstJitter = 1d;
        private const double EveryJitter = 2d;

        private static ICreatureSpell AutoAttack(uint creatureId, uint spell4Id, string comment)
        {
            return new CreatureSpell
            {
                CreatureId = creatureId,
                Spell4Id   = spell4Id,
                Flags      = CreatureSpellFlags.AutoAttack,
                Comment    = comment
            };
        }

        /// <summary>
        /// A special: first use <paramref name="first"/> seconds after the pull, then every <paramref name="every"/> seconds,
        /// when the target is within <paramref name="range"/> metres (0: the spell's own range).
        /// </summary>
        private static ICreatureSpell Special(uint creatureId, uint spell4Id, double first, double every, float range,
            CreatureSpellTarget target, byte priority, string comment)
        {
            return new CreatureSpell
            {
                CreatureId      = creatureId,
                Spell4Id        = spell4Id,
                InitialDelayMin = Milliseconds(first - FirstJitter),
                InitialDelayMax = Milliseconds(first + FirstJitter),
                IntervalMin     = Milliseconds(every - EveryJitter),
                IntervalMax     = Milliseconds(every + EveryJitter),
                MaxRange        = range,
                Target          = target,
                Priority        = priority,
                Comment         = comment
            };
        }

        private static uint Milliseconds(double seconds)
        {
            return (uint)Math.Max(0d, seconds * 1000d);
        }

        private const uint DominionScout              = 17856u;
        private const uint DominionSoldier            = 17857u;
        private const uint DominionEngineeringOfficer = 17949u;

        private const uint PlasmaShot = 65717u;
        private const uint ShellStorm = 66281u;
        private const uint Longshot   = 66282u;

        private static readonly ICreatureSpell[] spells =
        [
            // Dominion Scout: rifle, Shell Storm (cone 17 m / 34°), Longshot (line of boxes, 20 m); Mortar Barrage waits for
            // ground targeting, Snipe stays scripted (alarm/sniper)
            AutoAttack(DominionScout, PlasmaShot, "Plasma Shot"),
            Special(DominionScout, ShellStorm, first: 6d, every: 12d, range: 15f, CreatureSpellTarget.Victim, 1, "Shell Storm"),
            Special(DominionScout, Longshot, first: 10d, every: 18d, range: 20f, CreatureSpellTarget.Victim, 0, "Longshot"),

            // Dominion Soldier: rifle, Shell Storm
            AutoAttack(DominionSoldier, PlasmaShot, "Plasma Shot"),
            Special(DominionSoldier, ShellStorm, first: 6d, every: 12d, range: 15f, CreatureSpellTarget.Victim, 0, "Shell Storm"),

            // Dominion Engineering Officer (Leveling the Field): as the Soldier
            AutoAttack(DominionEngineeringOfficer, PlasmaShot, "Plasma Shot"),
            Special(DominionEngineeringOfficer, ShellStorm, first: 6d, every: 12d, range: 15f, CreatureSpellTarget.Victim, 0, "Shell Storm"),
        ];

        public IEnumerable<ICreatureSpell> GetCreatureSpells()
        {
            return spells;
        }
    }
}
