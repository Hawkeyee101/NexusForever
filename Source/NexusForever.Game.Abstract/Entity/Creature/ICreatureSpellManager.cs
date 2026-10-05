namespace NexusForever.Game.Abstract.Entity.Creature
{
    public interface ICreatureSpellManager
    {
        /// <summary>
        /// Return the spells the creature with <paramref name="creatureId"/> casts in combat, empty if it has no list.
        /// </summary>
        IReadOnlyList<ICreatureSpell> GetCreatureSpells(uint creatureId);
    }
}
