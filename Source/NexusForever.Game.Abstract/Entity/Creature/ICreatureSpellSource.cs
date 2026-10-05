namespace NexusForever.Game.Abstract.Entity.Creature
{
    /// <summary>
    /// Where creature → spell rows come from (a C# list for now, a database table later).
    /// </summary>
    public interface ICreatureSpellSource
    {
        IEnumerable<ICreatureSpell> GetCreatureSpells();
    }
}
