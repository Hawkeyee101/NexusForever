using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Static.Entity;

namespace NexusForever.Game.Entity.Creature
{
    public class CreatureSpell : ICreatureSpell
    {
        public uint CreatureId { get; init; }
        public uint Spell4Id { get; init; }
        public CreatureSpellFlags Flags { get; init; }
        public uint InitialDelayMin { get; init; }
        public uint InitialDelayMax { get; init; }
        public uint IntervalMin { get; init; }
        public uint IntervalMax { get; init; }
        public float MinRange { get; init; }
        public float MaxRange { get; init; }
        public CreatureSpellTarget Target { get; init; }
        public byte Priority { get; init; }
        public uint TipTextId { get; init; }
        public string Comment { get; init; }
    }
}
