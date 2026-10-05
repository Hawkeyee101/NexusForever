using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Effect.Data;
using NexusForever.Game.Abstract.Spell.Event;
using NexusForever.Game.Prerequisite;
using NexusForever.Game.Spell.Event;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.World.Entity;

namespace NexusForever.Game.Spell
{
    public class Proxy : IProxy
    {
        public IUnitEntity Target { get; }
        public ISpellEffectProxyData Data { get; }
        public ISpell ParentSpell { get; }
        public bool CanCast { get; private set; } = false;

        private readonly ISpellParameters parentParameters;

        public Proxy(IUnitEntity target, ISpellEffectProxyData data, ISpell parentSpell, ISpellParameters parameters)
        {
            Target = target;
            Data = data;
            ParentSpell = parentSpell;
            parentParameters = parameters;
        }

        /// <summary>
        /// Return new <see cref="ISpellParameters"/> for a cast of the proxy spell, every cast needs its own as the cast fills them in.
        /// </summary>
        private ISpellParameters BuildParameters()
        {
            return new SpellParameters
            {
                ParentSpellInfo = parentParameters.SpellInfo,
                RootSpellInfo = parentParameters.RootSpellInfo,
                PrimaryTargetId = Target.Guid,
                UserInitiatedSpellCast = parentParameters.UserInitiatedSpellCast,
                // a ground targeted parent passes on where it lands (e.g. Mortar Barrage's 69305 to its damage 69344), not its
                // positional unit, which goes when the parent finishes; any other parent's proxies aim at their own target as
                // they're cast (Mortar Barrage 66283's four shells all landed on the first spot, Teun 5 Oct 2026)
                TargetPosition = ParentSpell.TargetPosition.HasValue ? new Position(ParentSpell.TargetPosition.Value) : null,
                IsProxy = true
            };
        }

        public void Evaluate()
        {
            if (Target is not IPlayer)
                CanCast = true;

            if (Data.PrerequisiteId == 0)
                CanCast = true;

            if (CanCast)
                return;

            if (PrerequisiteManager.Instance.Meets(Target as IPlayer, Data.PrerequisiteId))
                CanCast = true;
        }

        public void Cast(IUnitEntity caster, ISpellEventManager events)
        {
            if (!CanCast)
                return;

            if (ParentSpell.CastMethod == CastMethod.Aura && Data.Entry.TickTime > 0)
            {
                caster.CastSpell(Data.PeriodicSpellId, BuildParameters());
                return;
            }

            if (Data.Entry.TickTime > 0)
            {
                // the periodic spell is cast every tick for the duration of the effect (e.g. spotlight 46970 casts 46971
                // every 500 ms for 5 s), without a duration it is cast MaxExecutions times, or once
                uint tickTime = Data.Entry.TickTime;
                uint ticks    = Data.Entry.DurationTime > 0 ? Data.Entry.DurationTime / tickTime : Math.Max(Data.MaxExecutions, 1u);
                if (Data.MaxExecutions > 0)
                    ticks = Math.Min(ticks, Data.MaxExecutions);

                for (uint i = 1; i <= ticks; i++)
                {
                    events.EnqueueEvent(new SpellEvent(tickTime * i / 1000d, () =>
                    {
                        if (caster.IsAlive)
                            caster.CastSpell(Data.PeriodicSpellId, BuildParameters());
                    }));
                }
            }
            else
                caster.CastSpell(Data.SpellId, BuildParameters());
        }
    }
}