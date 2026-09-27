using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Effect;
using NexusForever.Game.Abstract.Spell.Effect.Data;
using NexusForever.Game.Abstract.Spell.Target;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Static.Spell.Effect;

namespace NexusForever.Game.Spell.Effect.Handler
{
    [SpellEffectHandler(SpellEffectType.SpellForceRemove)]
    public class SpellEffectSpellForceRemoveHandler : ISpellEffectApplyHandler<ISpellEffectSpellForceRemoveData>
    {
        #region Dependency Injection

        private readonly ILogger<SpellEffectSpellForceRemoveHandler> log;

        public SpellEffectSpellForceRemoveHandler(
            ILogger<SpellEffectSpellForceRemoveHandler> log)
        {
            this.log = log;
        }

        #endregion

        public SpellEffectExecutionResult Apply(ISpellExecutionContext executionContext, IUnitEntity target, ISpellTargetEffectInfo info, ISpellEffectSpellForceRemoveData data)
        {
            // the spell executing this effect can match its own filter (e.g. the Hycrest skybox spells remove every spell
            // in their shared group before applying their own sky), it must only remove the other spells
            switch (data.Type)
            {
                case SpellEffectForceSpellRemoveType.SpellGroupId:
                {
                    foreach (ISpell spellToRemove in target.GetSpellsByGroupId(data.Data).ToList())
                        if (spellToRemove != executionContext.Spell)
                            spellToRemove.Finish();
                    break;
                }
                case SpellEffectForceSpellRemoveType.Spell4:
                {
                    ISpell spellToRemove = target.GetSpellBySpellId(data.Data);
                    if (spellToRemove != executionContext.Spell)
                        spellToRemove?.Finish();
                    break;
                }
                case SpellEffectForceSpellRemoveType.SpellBase:
                {
                    ISpell spellToRemove = target.GetSpellByBaseSpellId(data.Data);
                    if (spellToRemove != executionContext.Spell)
                        spellToRemove?.Finish();
                    break;
                }
                default:
                    log.LogWarning($"Unhandled EffectForceSpellRemoveType Type {data.Type}");
                    break;
            }

            return SpellEffectExecutionResult.Ok;
        }
    }
}
