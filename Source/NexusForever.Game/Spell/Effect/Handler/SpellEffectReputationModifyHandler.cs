using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Effect;
using NexusForever.Game.Abstract.Spell.Effect.Data;
using NexusForever.Game.Abstract.Spell.Target;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Static.Spell.Effect;

namespace NexusForever.Game.Spell.Effect.Handler
{
    /// <summary>
    /// A temporary reputation with a faction while the effect lasts, e.g. "Hidden in Plain Sight" (60752) in Hycrest: the
    /// Hycrest Watch (759) at 32,000 (Beloved) while disguised as a citizen. Not saved, the real reputation is untouched.
    /// </summary>
    [SpellEffectHandler(SpellEffectType.ReputationModify)]
    public class SpellEffectReputationModifyHandler : ISpellEffectApplyHandler<ISpellEffectReputationModifyData>, ISpellEffectRemoveHandler<ISpellEffectReputationModifyData>
    {
        /// <summary>
        /// Handle <see cref="ISpell"/> effect apply on <see cref="IUnitEntity"/> target.
        /// </summary>
        public SpellEffectExecutionResult Apply(ISpellExecutionContext executionContext, IUnitEntity target, ISpellTargetEffectInfo info, ISpellEffectReputationModifyData data)
        {
            if (target is not IPlayer player)
                return SpellEffectExecutionResult.PreventEffect;

            player.ReputationManager.AddOverride(data.FactionId, data.Value);
            return SpellEffectExecutionResult.Ok;
        }

        /// <summary>
        /// Handle <see cref="ISpell"/> effect remove on <see cref="IUnitEntity"/> target.
        /// </summary>
        public void Remove(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info, ISpellEffectReputationModifyData data)
        {
            if (target is IPlayer player)
                player.ReputationManager.RemoveOverride(data.FactionId);
        }
    }
}
