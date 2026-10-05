using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Effect;
using NexusForever.Game.Abstract.Spell.Target;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Static.Spell.Effect;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Combat;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Shared;
using NexusForever.Shared.Game;

namespace NexusForever.Game.Spell.Target
{
    public class SpellTargetEffectInfo : ISpellTargetEffectInfo
    {
        /// <summary>
        /// Return whether the <see cref="ISpellTargetEffectInfo"/> has been finalised.
        /// </summary>
        /// <remarks>
        /// The effect will be finalised when the duration expires or the effect is prematurely finished.
        /// </remarks>
        public bool IsFinalised { get; private set; }

        /// <summary>
        /// Return whether the <see cref="ISpellTargetEffectInfo"/> is pending execution.
        /// </summary>
        /// <remarks>
        /// A pending effect has been sent to the client but has not yet been executed.
        /// </remarks>
        public bool IsPending { get; private set; }

        public ISpellTargetInfo Target { get; private set; }
        public uint EffectId { get; private set; }
        public Spell4EffectsEntry Entry { get; private set; }
        public IDamageDescription Damage { get; private set; }

        private UpdateTimer duration;
        private UpdateTimer tick;

        // effects that are applied again every tick while their duration runs (damage and heal over time), other effect
        // types with a tick time either handle it themselves (proxies) or would stack when applied again
        private static readonly HashSet<SpellEffectType> periodicEffectTypes =
        [
            SpellEffectType.Damage,
            SpellEffectType.DistanceDependentDamage,
            SpellEffectType.DistributedDamage,
            SpellEffectType.Transference,
            SpellEffectType.Heal,
            SpellEffectType.VitalModifier
        ];

        #region Dependency Injection

        private ILogger<SpellTargetEffectInfo> log;
        private ISpellEffectHandlerInvoker spellEffectHandlerInvoker;
        private IGlobalSpellEffectManager globalSpellEffectManager;

        public SpellTargetEffectInfo(
            ILogger<SpellTargetEffectInfo> log,
            ISpellEffectHandlerInvoker spellEffectHandlerInvoker,
            IGlobalSpellEffectManager globalSpellEffectManager)
        {
            this.log                       = log;
            this.spellEffectHandlerInvoker = spellEffectHandlerInvoker;
            this.globalSpellEffectManager  = globalSpellEffectManager;
        }

        #endregion

        /// <summary>
        /// Initialise <see cref="ISpellTargetEffectInfo"/> with supplied <see cref="ISpellTargetInfo"/> and <see cref="Spell4EffectsEntry"/>.
        /// </summary>
        public void Initialise(ISpellTargetInfo target, Spell4EffectsEntry entry)
        {
            if (EffectId != 0)
                throw new InvalidOperationException("SpellTargetEffectInfo has already been initialised!");

            EffectId  = globalSpellEffectManager.NextEffectId;
            Target    = target;
            Entry     = entry;
            IsPending = entry.DelayTime > 0;

            if (!entry.Flags.HasFlag(SpellEffectFlags.CancelOnly))
                duration = new UpdateTimer(TimeSpan.FromMilliseconds(entry.DurationTime + entry.DelayTime));

            // auras apply their periodic effects again themselves
            if (entry.TickTime > 0
                && periodicEffectTypes.Contains(entry.EffectType)
                && target.Collection.Spell.CastMethod != CastMethod.Aura)
                tick = new UpdateTimer(TimeSpan.FromMilliseconds(entry.TickTime));

            log.LogTrace($"Initalised effect {Entry.EffectType} for target {Target.Guid} for spell {Target.Collection.Spell.Spell4Id}");
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public void Update(double lastTick)
        {
            if (IsFinalised)
                return;

            if (IsPending)
                return;

            duration?.Update(lastTick);
            if (duration != null && duration.HasElapsed)
            {
                Finish();
                return;
            }

            if (tick == null)
                return;

            tick.Update(lastTick);
            if (tick.HasElapsed)
            {
                Tick();
                tick.Reset();
            }
        }

        /// <summary>
        /// Apply a periodic effect again, the first application was its execution.
        /// </summary>
        private void Tick()
        {
            IUnitEntity target = Target.GetTarget();
            if (target is not { IsAlive: true })
                return;

            var executionContext = new SpellExecutionContext();
            executionContext.Initialise(Target.Collection.Spell, true);

            try
            {
                if (spellEffectHandlerInvoker.InvokeApplyHandler(executionContext, target, this) != SpellEffectExecutionResult.Ok)
                    return;
            }
            catch (Exception e)
            {
                log.LogError(e, $"An exception occurred ticking effect {Entry.EffectType} for target {Target.Guid} for spell {Target.Collection.Spell.Spell4Id}!");
                return;
            }

            // ticks aren't part of a ServerSpellGo, the damage of each tick goes out as a periodic combat log
            foreach (ICombatLog combatLog in executionContext.GetCombatLogs())
                target.EnqueueToVisible(new ServerCombatLog { CombatLog = combatLog }, true);

            if (Damage != null && Damage.CombatResult != CombatResult.Avoid)
                target.EnqueueToVisible(new ServerCombatLog
                {
                    CombatLog = new CombatLogDamage
                    {
                        MitigatedDamage = Damage.AdjustedDamage,
                        RawDamage       = Damage.RawDamage,
                        Shield          = Damage.ShieldAbsorbAmount,
                        Absorption      = Damage.AbsorbedAmount,
                        Overkill        = Damage.OverkillAmount,
                        BKilled         = Damage.KilledTarget,
                        BPeriodic       = true,
                        DamageType      = Damage.DamageType,
                        EffectType      = Entry.EffectType,
                        CastData        = new CombatLogCastData
                        {
                            CasterId     = Target.Collection.Spell.Caster.Guid,
                            TargetId     = target.Guid,
                            SpellId      = Target.Collection.Spell.Spell4Id,
                            CombatResult = Damage.CombatResult
                        }
                    }
                }, true);

            log.LogTrace($"Ticked effect {Entry.EffectType} for target {Target.Guid} for spell {Target.Collection.Spell.Spell4Id}");
        }

        public EffectInfo Build()
        {
            var effectInfo = new EffectInfo
            {
                Spell4EffectId = Entry.Id,
                EffectUniqueId = EffectId,
                DelayTime      = Entry.DelayTime,
                TimeRemaining  = duration != null ? (int)TimeSpan.FromSeconds(duration.Duration).TotalMilliseconds : -1
            };

            if (Damage != null)
            {
                effectInfo.InfoType              = 1;
                effectInfo.DamageDescriptionData = Damage.Build();
            }

            return effectInfo;
        }

        /// <summary>
        /// Execute the effect.
        /// </summary>
        /// <remarks>
        /// This will also invoke the <see cref="ISpellEffectApplyHandler"/> for the effect type if it exists.
        /// </remarks>
        public SpellEffectExecutionResult Execute(ISpellExecutionContext executionContext)
        {
            if (IsPending && !executionContext.IsDelayed)
                return SpellEffectExecutionResult.Pending;

            try
            {
                IUnitEntity target = Target.GetTarget();

                SpellEffectExecutionResult result = spellEffectHandlerInvoker.InvokeApplyHandler(executionContext, target, this);
                if (result == SpellEffectExecutionResult.Ok)
                {
                    if (executionContext.IsDelayed)
                    {
                        IsPending = false;

                        target.EnqueueToVisible(new ServerSpellEffectExecute()
                        {
                            CastingId           = executionContext.Spell.CastingId,
                            TargetGuid          = target.Guid,
                            SpellEffectUniqueId = EffectId
                        }, true);
                    }
                }

                log.LogTrace($"Applied effect {Entry.EffectType} for target {Target.Guid} for spell {Target.Collection.Spell.Spell4Id}");

                return result;
            }
            catch (Exception e)
            {
                log.LogError(e, $"An exception occurred applying effect {Entry.EffectType} for target {Target.Guid} for spell {Target.Collection.Spell.Spell4Id}!");
                return SpellEffectExecutionResult.NoHandler;
            }
        }

        /// <summary>
        /// Finish the effect.
        /// </summary>
        /// <remarks>
        /// This will also invoke the <see cref="ISpellEffectRemoveHandler"/> for the effect type if it exists.
        /// </remarks>
        public void Finish()
        {
            if (IsFinalised)
                return;

            HandleSpellEffectRemove();
            IsFinalised = true;

            log.LogTrace($"Finished effect {Entry.EffectType} for target {Target.Guid} for spell {Target.Collection.Spell.Spell4Id}");
        }

        private void HandleSpellEffectRemove()
        {
            // don't execute remove effect if apply effect was not executed
            if (IsPending)
                return;

            try
            {
                spellEffectHandlerInvoker.InvokeRemoveHandler(Target.Collection.Spell, Target.GetTarget(), this);
            }
            catch (Exception e)
            {
                log.LogError(e, $"An exception occurred removing effect {Entry.EffectType} for target {Target.Guid} for spell {Target.Collection.Spell.Spell4Id}!");
            }

            log.LogTrace($"Removed effect {Entry.EffectType} for target {Target.Guid} for spell {Target.Collection.Spell.Spell4Id}");
        }

        /// <summary>
        /// Add damage to the effect.
        /// </summary>
        public void AddDamage(IDamageDescription damage)
        {
            Damage = damage;
            // TODO: what about delayed effects, is there a message we should send?
        }

        /// <summary>
        /// Return the effect duration.
        /// </summary>
        public TimeSpan? GetDuration()
        {
            return duration != null ? TimeSpan.FromSeconds(duration.Duration) : null;
        }

        /// <summary>
        /// Set the effect duration.
        /// </summary>
        /// <remarks>
        /// This will overwrite the default duration.
        /// </remarks>
        public void SetDuration(TimeSpan? timeSpan)
        {
            if (timeSpan != null)
                duration = new UpdateTimer(timeSpan.Value);
            else
                duration = null;

            // non delayed effects will send the updated duration in ServerSpellGo
            if (IsPending)
            {
                IUnitEntity target = Target.GetTarget();
                if (target == null)
                    return;

                target.EnqueueToVisible(new ServerSpellUpdateEffectDuration
                {
                    CastingId           = Target.Collection.Spell.CastingId,
                    TargetId            = target.Guid,
                    SpellEffectUniqueId = EffectId,
                    Duration            = duration != null ? (int)TimeSpan.FromSeconds(duration.Duration).TotalMilliseconds : -1
                }, true);
            }
        }
    }
}
