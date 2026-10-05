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
    /// SetBusy (107): the unit is busy for the effect's duration, like NpcExecutionDelay. Reading the same duration field
    /// is a guess.
    /// </summary>
    [SpellEffectHandler(SpellEffectType.SetBusy)]
    public class SpellEffectSetBusyHandler : ISpellEffectApplyHandler<ISpellEffectDefaultData>
    {
        /// <summary>
        /// Handle <see cref="ISpell"/> effect apply on <see cref="IUnitEntity"/> target.
        /// </summary>
        public SpellEffectExecutionResult Apply(ISpellExecutionContext executionContext, IUnitEntity target, ISpellTargetEffectInfo info, ISpellEffectDefaultData data)
        {
            if (info.Entry.DurationTime > 0u)
                target.SetBusy(TimeSpan.FromMilliseconds(info.Entry.DurationTime));

            return SpellEffectExecutionResult.Ok;
        }
    }
}
