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
    /// NpcExecutionDelay (35): the unit is busy for the effect's duration after the spell executes (no chase, no other
    /// cast). Almost every NPC special has one of 700-2000 ms on the caster (e.g. Shell Storm 66281: 1000 ms).
    /// </summary>
    [SpellEffectHandler(SpellEffectType.NpcExecutionDelay)]
    public class SpellEffectNpcExecutionDelayHandler : ISpellEffectApplyHandler<ISpellEffectDefaultData>
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
