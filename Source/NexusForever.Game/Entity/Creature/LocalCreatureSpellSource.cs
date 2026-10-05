using NexusForever.Game.Abstract.Entity.Creature;

namespace NexusForever.Game.Entity.Creature
{
    /// <summary>
    /// Creature → spell rows kept in code until there is a database table for them (local, Hycrest enemies first).
    /// </summary>
    /// <remarks>
    /// Spells and creatures from the tables and Jabbithole's ability lists (docs research/enemy-spells-plan.md). The timing
    /// isn't in the client tables (Spell4 cooldowns are almost always 0): every delay and interval here is a guess, to tune
    /// with retail videos.
    /// </remarks>
    public class LocalCreatureSpellSource : ICreatureSpellSource
    {
        private static readonly ICreatureSpell[] spells =
        [
        ];

        public IEnumerable<ICreatureSpell> GetCreatureSpells()
        {
            return spells;
        }
    }
}
