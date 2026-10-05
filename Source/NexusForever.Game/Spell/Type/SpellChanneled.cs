using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Target;
using NexusForever.Game.Abstract.Spell.Validator;
using NexusForever.Game.Spell.Event;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.World.Message.Static;

namespace NexusForever.Game.Spell.Type
{
    public class SpellChanneled : Spell
    {
        public override CastMethod CastMethod => CastMethod.Channeled;

        #region Dependency Injection

        private readonly ILogger log;

        public SpellChanneled(
            ILogger log,
            ISpellTargetInfoCollection spellTargetInfoCollection,
            IGlobalSpellManager globalSpellManager,
            ICastResultValidatorManager castResultValidatorManager,
            IDisableManager disableManager)
            : base(log, spellTargetInfoCollection, globalSpellManager, castResultValidatorManager, disableManager)
        {
            this.log = log;
        }

        #endregion

        public override bool Cast()
        {
            if (!base.Cast())
                return false;

            events.EnqueueEvent(new SpellEvent(Parameters.SpellInfo.Entry.ChannelInitialDelay / 1000d, () =>
            {
                CastResult checkResources = CheckResourceConditions();
                if (checkResources != CastResult.Ok)
                {
                    CancelCast(checkResources);
                    return;
                }

                Execute();
            })); // Execute after initial delay
            events.EnqueueEvent(new SpellEvent(Parameters.SpellInfo.Entry.ChannelMaxTime / 1000d, Finish)); // End Spell Cast

            // Calculate number of "ticks" in this spell cast, a channel without a pulse time only executes once
            uint numberOfPulses = Parameters.SpellInfo.Entry.ChannelPulseTime > 0
                ? Parameters.SpellInfo.Entry.ChannelMaxTime / Parameters.SpellInfo.Entry.ChannelPulseTime
                : 0u;

            // Add ticks at each pulse
            for (int i = 1; i <= numberOfPulses; i++)
                events.EnqueueEvent(new SpellEvent((Parameters.SpellInfo.Entry.ChannelInitialDelay + Parameters.SpellInfo.Entry.ChannelPulseTime * i) / 1000d, () =>
                {
                    CastResult checkResources = CheckResourceConditions();
                    if (checkResources != CastResult.Ok)
                    {
                        CancelCast(checkResources);
                        return;
                    }

                    Execute();
                }));

            status = SpellStatus.Casting;
            log.LogTrace($"Spell {Parameters.SpellInfo.Entry.Id} has started casting.");
            return true;
        }

        protected override bool _IsCasting()
        {
            return base._IsCasting() && (status == SpellStatus.Casting || status == SpellStatus.Executing);
        }
    }
}
