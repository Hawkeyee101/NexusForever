using System.Collections.Immutable;
using NexusForever.Game.Abstract.Entity.Creature;

namespace NexusForever.Game.Entity.Creature
{
    public class CreatureSpellManager : ICreatureSpellManager
    {
        private readonly ImmutableDictionary<uint, ImmutableList<ICreatureSpell>> creatureSpells;

        public CreatureSpellManager(
            IEnumerable<ICreatureSpellSource> sources)
        {
            creatureSpells = sources
                .SelectMany(s => s.GetCreatureSpells())
                .GroupBy(s => s.CreatureId)
                .ToImmutableDictionary(g => g.Key, g => g.OrderBy(s => s.Priority).ToImmutableList());
        }

        /// <summary>
        /// Return the spells the creature with <paramref name="creatureId"/> casts in combat, empty if it has no list.
        /// </summary>
        public IReadOnlyList<ICreatureSpell> GetCreatureSpells(uint creatureId)
        {
            return creatureSpells.TryGetValue(creatureId, out ImmutableList<ICreatureSpell> spells) ? spells : [];
        }
    }
}
