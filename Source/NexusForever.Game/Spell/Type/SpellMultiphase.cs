using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Target;
using NexusForever.Game.Abstract.Spell.Validator;
using NexusForever.Game.Spell.Event;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Spell.Type
{
    public class SpellMultiphase : Spell
    {
        public override CastMethod CastMethod => CastMethod.Multiphase;

        #region Dependency Injection

        private readonly ILogger<SpellMultiphase> log;

        public SpellMultiphase(
            ILogger<SpellMultiphase> log,
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

            uint spellDelay = 0;
            double missileTravel = GetMissileTravelTime();
            double previousPhaseAt = 0d;
            for (int i = 0; i < Parameters.SpellInfo.Phases.Count; i++)
            {
                int index = i;
                SpellPhaseEntry spellPhase = Parameters.SpellInfo.Phases[i];
                spellDelay += spellPhase.PhaseDelay;

                // phases stay in order when one waits for its telegraph
                double phaseAt = Math.Max(previousPhaseAt, missileTravel + GetPhaseResolveTime((byte)spellPhase.OrderIndex, spellDelay) / 1000d);
                previousPhaseAt = phaseAt;

                events.EnqueueEvent(new SpellEvent(phaseAt, () =>
                {
                    currentPhase = (byte)spellPhase.OrderIndex;
                    Execute();

                    if (i == Parameters.SpellInfo.Phases.Count - 1)
                    {
                        status = SpellStatus.Finishing;
                        log.LogTrace($"SpellMultiphase {Parameters.SpellInfo.Entry.Id} has finished executing.");
                    }
                }));
            }

            status = SpellStatus.Casting;
            log.LogTrace($"SpellMultiphase {Parameters.SpellInfo.Entry.Id} has started casting.");
            return true;
        }

        /// <summary>
        /// Return when a phase of an NPC's spell resolves, in ms from the start of its phases: when the telegraphs that belong
        /// to that phase alone end, if they end after the phase starts.
        /// </summary>
        /// <remarks>
        /// Local reading of the tables (5 Oct 2026, Teun: damage came as the telegraph appeared, not as it resolved): Triple
        /// Threat 66275 hits at 300/700/1100 ms while its three telegraphs end at 500/900/1300; Feet of Fury 66354 (800/1000)
        /// and Energy Leap 66244 (2433 → 2500) end where their phases already are. Telegraphs over every phase (progressive
        /// spells like Eradicate 66395 or Slashing Strikes 66353) don't move a phase. Players' spells keep the phase times.
        /// </remarks>
        private uint GetPhaseResolveTime(byte phase, uint phaseStart)
        {
            if (Caster is IPlayer || phase >= 32)
                return phaseStart;

            uint phaseMask = 1u << phase;
            uint telegraphEnd = Parameters.SpellInfo.Telegraphs
                .Where(t => t.PhaseFlags != 1u
                    && (t.PhaseFlags & 0xFFFFu) != 0xFFFFu
                    && (t.PhaseFlags & phaseMask) != 0u
                    && t.TelegraphTimeEndMs > 0u)
                .Select(t => t.TelegraphTimeEndMs)
                .DefaultIfEmpty(0u)
                .Min();

            return Math.Max(phaseStart, telegraphEnd);
        }

        /// <summary>
        /// Return the seconds an NPC's missile spell takes to land at its position (Spell4 missileSpeed, metres per second).
        /// </summary>
        /// <remarks>
        /// Kirmmin (Discord, 6 Oct 2020): "if there was a missile or projectile, it would already know if it was going to get
        /// hit, it's just delayed in when it lands". Mortar Barrage's shell 69305 (25 m/s) did its damage as its impact zone
        /// appeared (Teun, 5 Oct 2026). Local reading; players' spells and normal casts aren't delayed.
        /// </remarks>
        private double GetMissileTravelTime()
        {
            uint missileSpeed = Parameters.SpellInfo.Entry.MissileSpeed;
            if (Caster is IPlayer || missileSpeed == 0u)
                return 0d;

            return Vector3.Distance(Caster.Position, GetSpellPosition()) / missileSpeed;
        }

        protected override bool _IsCasting()
        {
            return base._IsCasting() && (status == SpellStatus.Casting || status == SpellStatus.Executing);
        }

        protected override bool IsTelegraphValid(TelegraphDamageEntry telegraph)
        {
            // Ensure only telegraphs that apply to this Execute phase are evaluated.
            if (currentPhase >= 255)
                return true;

            int phaseMask = 1 << currentPhase;
            return telegraph.PhaseFlags != 1 && (phaseMask & telegraph.PhaseFlags) != 0;
        }

        protected override bool CanExecuteEffect(Spell4EffectsEntry spell4EffectsEntry)
        {
            if (currentPhase < 255)
            {
                int phaseMask = 1 << currentPhase;
                if (spell4EffectsEntry.PhaseFlags != 1 && spell4EffectsEntry.PhaseFlags != uint.MaxValue && (phaseMask & spell4EffectsEntry.PhaseFlags) == 0)
                    return false;
            }

            return base.CanExecuteEffect(spell4EffectsEntry);
        }
    }
}
