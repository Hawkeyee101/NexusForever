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
    /// <summary>
    /// RavelSignal (0x51): a server side script trigger, the client does nothing with it. DataBits01 is the signal (script)
    /// id, DataBits00 a small kind (1-5, meaning unknown). The public event scripts on the caster's map are told.
    /// </summary>
    /// <remarks>
    /// Client reverse engineering and Discord (Rāwaho, 2025: "server side triggers for scripts"), docs
    /// research/client-re-castbar-ravel.md (local, 6 Oct 2026).
    /// </remarks>
    [SpellEffectHandler(SpellEffectType.RavelSignal)]
    public class SpellEffectRavelSignalHandler : ISpellEffectApplyHandler<ISpellEffectDefaultData>
    {
        #region Dependency Injection

        private readonly ILogger<SpellEffectRavelSignalHandler> log;

        public SpellEffectRavelSignalHandler(
            ILogger<SpellEffectRavelSignalHandler> log)
        {
            this.log = log;
        }

        #endregion

        /// <summary>
        /// Handle <see cref="ISpell"/> effect apply on <see cref="IUnitEntity"/> target.
        /// </summary>
        public SpellEffectExecutionResult Apply(ISpellExecutionContext executionContext, IUnitEntity target, ISpellTargetEffectInfo info, ISpellEffectDefaultData data)
        {
            IUnitEntity caster = executionContext.Spell.Caster;
            uint kind          = info.Entry.DataBits00;
            uint signal        = info.Entry.DataBits01;

            log.LogDebug($"Ravel signal {signal} (kind {kind}) from spell {executionContext.Spell.Spell4Id}, caster {caster?.Guid}, target {target?.Guid}.");
            caster?.Map?.PublicEventManager?.OnRavelSignal(caster, target, executionContext.Spell.Spell4Id, kind, signal);

            return SpellEffectExecutionResult.Ok;
        }
    }
}
