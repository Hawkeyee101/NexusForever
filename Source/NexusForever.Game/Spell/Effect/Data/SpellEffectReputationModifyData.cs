using NexusForever.Game.Abstract.Spell.Effect.Data;
using NexusForever.Game.Static.Reputation;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Spell.Effect.Data
{
    public class SpellEffectReputationModifyData : ISpellEffectReputationModifyData
    {
        public Spell4EffectsEntry Entry { get; private set; }
        public Faction FactionId { get; private set; }
        public float Value { get; private set; }

        public void Populate(Spell4EffectsEntry entry)
        {
            Entry     = entry;
            FactionId = (Faction)entry.DataBits00;
            Value     = BitConverter.UInt32BitsToSingle(entry.DataBits01);
        }
    }
}
