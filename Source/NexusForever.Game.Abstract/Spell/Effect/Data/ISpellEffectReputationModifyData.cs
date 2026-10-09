using NexusForever.Game.Static.Reputation;

namespace NexusForever.Game.Abstract.Spell.Effect.Data
{
    public interface ISpellEffectReputationModifyData : ISpellEffectData
    {
        Faction FactionId { get; }
        float Value { get; }
    }
}
