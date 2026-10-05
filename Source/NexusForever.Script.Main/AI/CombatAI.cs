using System.Numerics;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Abstract.Entity.Movement.Command;
using NexusForever.Game.Abstract.Entity.Movement.Command.Position;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Game.Static.Reputation;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Entity;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter.Dynamic;
using NexusForever.Shared;
using NexusForever.Shared.Game;

namespace NexusForever.Script.Main.AI
{
    //[ScriptFilterIgnore]
    [ScriptFilterDynamic<IScriptFilterDynamicCombatAI>]
    public class CombatAI : IUnitScript, IOwnedScript<ICreatureEntity>
    {
        protected ICreatureEntity entity;

        private int autoAttackIndex = 0;
        protected List<uint> autoAttacks = [5649, 5652];
        private readonly UpdateTimer autoAttackTimer = new(TimeSpan.FromSeconds(1.5d));

        // a ranged creature stops this far into its auto attack range, a melee one this far from its target's edge
        private const float MeleeRange = 5f;
        private const float RangedStopFactor = 0.8f;
        private const float MeleeGap = 1f;

        // chasers of one target spread over this many approach angles, about ApproachSpacing metres apart where they stop
        // (15-40 degrees), so they stand close but not on top of each other (Teun, 5 Oct 2026; guesses)
        private const int ApproachAngles = 7;
        private const float ApproachSpacing = 2.5f;
        private const float ApproachAngleMin = MathF.PI / 12f;
        private const float ApproachAngleMax = MathF.PI * 2f / 9f;

        // the closest range a ready special needs that the target is out of (e.g. Flametorch's 6 m for a rifleman who
        // keeps his distance), so the creature moves in for it
        private float specialRange = float.MaxValue;

        // the unit this creature turns to face outside of its casts
        private uint facingGuid;
        private readonly UpdateTimer chaseDistanceTimer = new(TimeSpan.FromSeconds(1f));

        /// <summary>
        /// A spell from the creature's spell list (not an auto attack) and the seconds until it can be cast again.
        /// </summary>
        private class CreatureSpellState
        {
            public ICreatureSpell Spell;
            public double ReadyIn;
        }

        private readonly List<CreatureSpellState> creatureSpells = [];
        private bool wasInCombat;

        #region Dependency Injection

        private readonly IFactory<ISpellParameters> spellParametersFactory;
        private readonly IGameTableManager gameTableManager;
        private readonly ICreatureSpellManager creatureSpellManager;

        public CombatAI(
            IFactory<ISpellParameters> spellParametersFactory,
            IGameTableManager gameTableManager,
            ICreatureSpellManager creatureSpellManager = null)
        {
            this.spellParametersFactory = spellParametersFactory;
            this.gameTableManager = gameTableManager;
            this.creatureSpellManager = creatureSpellManager;
        }

        #endregion

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public virtual void OnLoad(ICreatureEntity owner)
        {
            entity = owner;
            entity.SetInRangeCheck(15f);

            // the creature → spell list: its auto attacks replace the default ones, the rest are cast when ready
            IReadOnlyList<ICreatureSpell> spells = creatureSpellManager?.GetCreatureSpells(entity.CreatureId) ?? [];
            List<uint> listAutoAttacks = spells
                .Where(s => (s.Flags & CreatureSpellFlags.AutoAttack) != 0)
                .Select(s => s.Spell4Id)
                .ToList();
            if (listAutoAttacks.Count > 0)
                autoAttacks = listAutoAttacks;

            foreach (ICreatureSpell spell in spells.Where(s => (s.Flags & CreatureSpellFlags.AutoAttack) == 0))
                creatureSpells.Add(new CreatureSpellState { Spell = spell });
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public virtual void Update(double lastTick)
        {
            if (!entity.IsAlive)
                return;

            UpdateCreatureSpellTimers(lastTick);

            if (!entity.TargetGuid.HasValue)
                return;

            // busy after a special (NpcExecutionDelay): no chase, no other cast
            if (entity.IsCasting() || entity.IsBusy)
                return;

            UpdateAI(lastTick);
        }

        protected virtual void UpdateAI(double lastTick)
        {
            // keep facing the target between casts (a special locks the direction while it is cast)
            if (!entity.IsTurret && entity.TargetGuid.HasValue && facingGuid != entity.TargetGuid.Value)
            {
                facingGuid = entity.TargetGuid.Value;
                entity.MovementManager.SetRotationFaceUnit(facingGuid);
            }

            if (TryCastCreatureSpell())
                return;

            autoAttackTimer.Update(lastTick);
            if (autoAttackTimer.HasElapsed)
            {
                DoAutoAttack();
                autoAttackTimer.Reset();
            }

            chaseDistanceTimer.Update(lastTick);
            if (chaseDistanceTimer.HasElapsed)
            {
                DoChase();
                chaseDistanceTimer.Reset();
            }
        }

        private void DoAutoAttack()
        {
            if (autoAttacks.Count == 0)
                return;

            if (!entity.TargetGuid.HasValue)
                return;

            IUnitEntity target = entity.Map.GetEntity<IUnitEntity>(entity.TargetGuid.Value);
            if (target == null)
                return;

            autoAttackIndex %= autoAttacks.Count;
            uint spell4Id = autoAttacks[autoAttackIndex];
            autoAttackIndex = (autoAttackIndex + 1) % autoAttacks.Count;

            Spell4Entry spell4Entry = gameTableManager.Spell4.GetEntry(spell4Id);
            if (spell4Entry == null)
                return;

            if (entity.Position.GetDistance(target.Position) > spell4Entry.TargetMaxRange + Reach(target))
                return;

            ISpellParameters spellParameters = spellParametersFactory.Resolve();
            spellParameters.PrimaryTargetId = entity.TargetGuid.Value;
            entity.CastSpell(spell4Id, spellParameters);
        }

        /// <summary>
        /// Count down the spell list's timers while in combat; entering combat starts them at their initial delays.
        /// </summary>
        private void UpdateCreatureSpellTimers(double lastTick)
        {
            if (creatureSpells.Count == 0)
                return;

            if (!entity.InCombat)
            {
                wasInCombat = false;
                return;
            }

            if (!wasInCombat)
            {
                wasInCombat = true;
                foreach (CreatureSpellState state in creatureSpells)
                    state.ReadyIn = RandomSeconds(state.Spell.InitialDelayMin, state.Spell.InitialDelayMax);
                return;
            }

            foreach (CreatureSpellState state in creatureSpells)
                state.ReadyIn -= lastTick;
        }

        /// <summary>
        /// Cast the first ready spell from the creature's spell list whose target is in range; return true if one was cast.
        /// </summary>
        private bool TryCastCreatureSpell()
        {
            if (creatureSpells.Count == 0 || !entity.TargetGuid.HasValue)
                return false;

            IUnitEntity victim = entity.Map.GetEntity<IUnitEntity>(entity.TargetGuid.Value);
            if (victim == null)
                return false;

            specialRange = float.MaxValue;
            foreach (CreatureSpellState state in creatureSpells)
            {
                if (state.ReadyIn > 0d)
                    continue;

                Spell4Entry spell4Entry = gameTableManager.Spell4.GetEntry(state.Spell.Spell4Id);
                if (spell4Entry == null)
                    continue;

                float minRange = state.Spell.MinRange > 0f ? state.Spell.MinRange : spell4Entry.TargetMinRange;
                float maxRange = state.Spell.MaxRange > 0f ? state.Spell.MaxRange : spell4Entry.TargetMaxRange;

                IUnitEntity target = state.Spell.Target switch
                {
                    CreatureSpellTarget.Self                 => entity,
                    CreatureSpellTarget.RandomHostileInRange => RandomHostileInRange(minRange, maxRange),
                    _                                        => victim
                };
                if (target == null)
                    continue;

                if (target != entity)
                {
                    float distance = entity.Position.GetDistance(target.Position);
                    if (maxRange > 0f && distance > maxRange && target == victim)
                        specialRange = MathF.Min(specialRange, maxRange);
                    if (distance < minRange || (maxRange > 0f && distance > maxRange))
                        continue;
                }

                // no interval: once per fight
                double readyIn = state.Spell.IntervalMax == 0u
                    ? double.MaxValue
                    : RandomSeconds(state.Spell.IntervalMin, state.Spell.IntervalMax);

                // handled by the map's public event scripts instead of a cast (e.g. a call for reinforcements)
                if ((state.Spell.Flags & CreatureSpellFlags.ScriptHandled) != 0)
                {
                    entity.Map?.PublicEventManager?.OnCreatureSpell(entity, target, state.Spell.Spell4Id);
                    state.ReadyIn = readyIn;
                    return true;
                }

                // a new set of parameters for every cast
                ISpellParameters spellParameters = spellParametersFactory.Resolve();
                spellParameters.PrimaryTargetId = target.Guid;
                if (state.Spell.Target == CreatureSpellTarget.VictimPosition)
                    spellParameters.TargetPosition = new Position(target.Position);

                // the direction is locked at the start of the cast, so its telegraph hits where it was drawn and can be
                // dodged (facing the unit kept turning the hit area after the target, 5 Oct 2026)
                if (!entity.IsTurret && target != entity)
                {
                    entity.MovementManager.Finalise();
                    Vector3 toTarget = target.Position - entity.Position;
                    entity.MovementManager.SetRotation(new Vector3(MathF.Atan2(-toTarget.X, -toTarget.Z), 0f, 0f), false);
                    facingGuid = 0u;
                }

                entity.CastSpell(state.Spell.Spell4Id, spellParameters);

                // also after a cast that didn't start, so a failing spell isn't retried every tick
                state.ReadyIn = readyIn;
                return true;
            }

            return false;
        }

        private IUnitEntity RandomHostileInRange(float minRange, float maxRange)
        {
            List<IUnitEntity> inRange = entity.ThreatManager
                .Select(h => entity.Map.GetEntity<IUnitEntity>(h.HatedUnitId))
                .Where(u => u is { IsAlive: true })
                .Where(u =>
                {
                    float distance = entity.Position.GetDistance(u.Position);
                    return distance >= minRange && (maxRange <= 0f || distance <= maxRange);
                })
                .ToList();

            return inRange.Count > 0 ? inRange[Random.Shared.Next(inRange.Count)] : null;
        }

        private static double RandomSeconds(uint minMs, uint maxMs)
        {
            if (maxMs < minMs)
                maxMs = minMs;
            return (minMs + Random.Shared.NextDouble() * (maxMs - minMs)) / 1000d;
        }

        private void DoChase()
        {
            if (!entity.TargetGuid.HasValue)
                return;

            // a turret doesn't move after its target
            if (entity.IsTurret)
                return;

            IUnitEntity target = entity.Map.GetEntity<IUnitEntity>(entity.TargetGuid.Value);
            if (target == null)
                return;

            // stay at range: only move while the target is out of reach of every auto attack
            float range = MathF.Min(GetAutoAttackRange(), specialRange);
            if (entity.Position.GetDistance(target.Position) < range + Reach(target))
                return;

            // a ranged creature stops well inside its range, a melee one just short of its target's edge, each at its own
            // angle around the target
            float stop  = (range > MeleeRange ? range * RangedStopFactor : MeleeGap) + Reach(target);
            float step  = Math.Clamp(ApproachSpacing / stop, ApproachAngleMin, ApproachAngleMax);
            float angle = ((int)(entity.Guid % ApproachAngles) - ApproachAngles / 2) * step;
            entity.MovementManager.Chase(target, stop, angle);
            facingGuid = 0u;
        }

        /// <summary>
        /// Ranges count from the edges of both units, not their centres: a big unit (Arsenax, the bots) can't get within a
        /// melee range of its target's centre and kept walking during the fight (Teun, 5 Oct 2026).
        /// </summary>
        private float Reach(IUnitEntity target)
        {
            return entity.HitRadius + target.HitRadius;
        }

        /// <summary>
        /// Return the range all of the creature's auto attacks reach.
        /// </summary>
        private float GetAutoAttackRange()
        {
            float range = autoAttacks
                .Select(id => gameTableManager.Spell4.GetEntry(id)?.TargetMaxRange ?? 0f)
                .Where(r => r > 0f)
                .DefaultIfEmpty(MeleeRange)
                .Min();
            return range;
        }

        /// <summary>
        /// Invoked when <see cref="IPositionCommand"/> is finalised.
        /// </summary>
        public void OnEntityCommandFinalise(IEntityCommand command)
        {
            if (command is not IPositionCommand)
                return;

            if (!entity.TargetGuid.HasValue)
                return;

            if (entity.IsTurret)
                return;

            entity.MovementManager.SetRotationFaceUnit(entity.TargetGuid.Value);
        }

        /// <summary>
        /// Invoked when <see cref="IGridEntity"/> is added to range check range.
        /// </summary>
        public void OnEnterRange(IGridEntity entity)
        {
            AggroEntity(entity);
        }

        /// <summary>
        /// Invoked when <see cref="IGridEntity"/> is removed from vision range.
        /// </summary>
        public void OnRemoveVisibleEntity(IGridEntity entity)
        {
            if (entity.Guid == this.entity.TargetGuid)
                SelectTarget();
        }

        /// <summary>
        /// Invoked when health is changed by source <see cref="IUnitEntity"/>.
        /// </summary>
        public virtual void OnHealthChange(IUnitEntity source, uint amount, DamageType? type)
        {
            if (type is DamageType.Heal or null)
                return;

            AggroEntity(source);
        }

        private void AggroEntity(IGridEntity source)
        {
            if (!entity.IsAlive)
                return;

            if (entity.InCombat)
                return;

            if (source is not IUnitEntity unit)
                return;

            if (entity.GetDispositionTo(unit.Faction1) != Disposition.Hostile)
                return;

            var spellParameters = spellParametersFactory.Resolve();
            entity.CastSpell(41368, spellParameters);

            // a turret keeps its own movement (e.g. riding a vehicle along a path)
            if (!entity.IsTurret)
            {
                entity.MovementManager.Finalise();
                entity.MovementManager.SetRotationFaceUnit(unit.Guid);
            }

            entity.ThreatManager.UpdateThreat(unit, 1);
        }

        /// <summary>
        /// Invoked when <see cref="IUnitEntity"/> enters combat.
        /// </summary>
        public virtual void OnEnterCombat()
        {
            // deliberately empty
        }

        /// <summary>
        /// Invoked when <see cref="IUnitEntity"/> leaves combat.
        /// </summary>
        public virtual void OnLeaveCombat()
        {
            // deliberately empty
        }

        /// <summary>
        /// Invoked when <see cref="IUnitEntity"/> is killed.
        /// </summary>
        public virtual void OnDeath()
        {
            // deliberately empty
        }

        /// <summary>
        /// Invoked when a new <see cref="IHostileEntity"/> is added to the threat list.
        /// </summary>
        public void OnThreatAddTarget(IHostileEntity hostile)
        {
            SelectTarget();
        }

        /// <summary>
        /// Invoked when an existing <see cref="IHostileEntity"/> is removed from the threat list.
        /// </summary>
        public void OnThreatRemoveTarget(IHostileEntity hostile)
        {
            SelectTarget();
        }

        /// <summary>
        /// Invoked when an existing <see cref="IHostileEntity"/> is update on the threat list.
        /// </summary>
        public void OnThreatChange(IHostileEntity hostile)
        {
            SelectTarget();
        }

        protected virtual void SelectTarget()
        {
            IHostileEntity hostile = entity.ThreatManager.GetTopHostile();
            if (hostile == null)
            {
                Reset();
                return;
            }

            if (entity.TargetGuid == hostile.HatedUnitId)
                return;

            entity.SetTarget(hostile.HatedUnitId, hostile.Threat);
        }

        private void Reset()
        {
            entity.SetTarget((IWorldEntity)null);

            // a dying unit's threat list is cleared too: it isn't healed or sent home (it came back with full health)
            if (!entity.IsAlive)
                return;

            entity.ModifyHealth(entity.MaxHealth, DamageType.Heal, null);

            // TODO: cancel all pending spells

            if (entity.IsTurret)
                return;

            float speed = entity.GetPropertyValue(Property.MoveSpeedMultiplier) * 10f;
            entity.MovementManager.LaunchSpline([entity.Position, entity.LeashPosition], SplineType.Linear, SplineMode.OneShot, speed);
        }
    }
}
