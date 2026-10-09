using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Reputation;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Shared;
using NexusForever.Shared;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    /// <summary>
    /// The Dominion alarm: a Recon Specialist shows up, calls reinforcements and, unless stopped, a Rapid Response Team
    /// surfaces with a Chua Ground Drill.
    /// </summary>
    /// <remarks>
    /// Retail (videos + tables): the specialist says 560790 and casts Call Reinforcements 48499 (5 s cast), which summons
    /// an Assistance Flare 50310 for 14 s (tooltip 458313, Vesna communicator 5022). Interrupting the cast or killing the
    /// specialist or the flare stops it. Otherwise the drill surfaces (Surface 48564) with a Rapid Response Commando and
    /// two Rapid Response Drones, already in combat (tooltip 458314, Vesna communicator 5023). The engine doesn't handle
    /// the summon effect, so the flare and the team are spawned here. Hiding to make the team retreat isn't built.
    /// </remarks>
    public class HycrestAlarm
    {
        private const uint ReconSpecialist = 17823u;
        private const Faction DominionFaction = (Faction)1452u;
        private const uint AssistanceFlare = 50310u;
        private const uint ChuaGroundDrill = 50363u;
        private const uint RapidResponseCommando = 50378u;
        private const uint RapidResponseDrone = 50834u;

        private const uint CallReinforcements = 48499u;
        private const uint Surface = 48564u;

        // when he calls: "Wait until my friends show up." / "You'll be outnumbered soon!" (Teun's video, The Science of Revenge)
        private static readonly uint[] SpecialistBarks = [560790u, 560792u];
        private const uint FlareTooltip = 458313u;   // "A Dominion Unit has released a signal flare, summoning a Rapid Response Team!"
        private const uint TeamTooltip = 458314u;    // "A Dominion Rapid Response Team has arrived nearby!"
        // Vesna's lines of communicators 5022/5023, sent as story communicators: the client shows a communicator message
        // only once, the alarm can go off several times
        private const uint VesnaTaranoft = 17778u;
        private const uint VesnaFlare = 461028u;     // "Stop the Dominion from calling in reinforcements..."
        private const uint VesnaTeam = 461029u;      // "...if you hide in the shadows, they will eventually retreat."
        private const uint VesnaFlareDurationMs = 10000u;

        // TEST (client RE, 6 Oct 2026): a regular communicator plays its voice (CommunicatorLib.PlaySpamVO), a story
        // communicator never does. The client shows a regular one only once, so each player gets the real communicator
        // (5022 / 5023, it shows its own text) the first time and the story communicator after that. false = story only.
        private const bool RealCommunicators = true;
        private static readonly Dictionary<uint, ushort> CommunicatorByText = new()
        {
            { VesnaFlare, 5022 },
            { VesnaTeam,  5023 }
        };
        private static readonly HashSet<(ulong CharacterId, ushort CommunicatorId)> communicatorsShown = [];
        private const uint VesnaTeamDurationMs = 16000u;

        private static readonly TimeSpan CastDelay = TimeSpan.FromSeconds(1);
        // the specialist runs at the player he engaged and calls once he is this close, or after the time limit
        private const float CallRange = 10f;
        private static readonly TimeSpan MaxApproachTime = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ApproachBusy = TimeSpan.FromSeconds(0.5);
        private const double ApproachChaseInterval = 1d;
        private double approachChaseTimer;
        private static readonly TimeSpan CastTime = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RecastDelay = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan FlareDuration = TimeSpan.FromSeconds(14);
        private static readonly TimeSpan DrillSurfaceTime = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan DrillDuration = TimeSpan.FromSeconds(10);

        // the drill surfaces this far from the flare, towards the player the specialist engaged
        private const float DrillDistance = 10f;

        private enum AlarmState
        {
            Idle,
            Calling,
            Flare,
            Arriving,
            Done
        }

        /// <summary>
        /// The alarm is running: the specialist is calling, the flare is up or the team is arriving.
        /// </summary>
        public bool IsActive => state is AlarmState.Calling or AlarmState.Flare or AlarmState.Arriving;

        /// <summary>
        /// Return true if the creature is one the alarm spawns (they don't count as kills for the next alarm).
        /// </summary>
        public static bool IsAlarmUnit(uint creatureId)
        {
            return creatureId is ReconSpecialist or AssistanceFlare or ChuaGroundDrill or RapidResponseCommando or RapidResponseDrone;
        }

        private AlarmState state;
        // references, spawned entities only get a guid once they are on the map
        private IUnitEntity specialist;
        // specialists that already called (the mission's engage call, the kill-count alarm, the creature spell list): each
        // calls at most once
        private readonly HashSet<IUnitEntity> called = [];
        private IUnitEntity flare;
        private uint targetGuid;
        private ISpell callSpell;
        private double callElapsed;
        private double approachTime;
        private double stateTimer;

        private readonly TimedActionQueue actionQueue = new();

        private readonly ILogger log;
        private readonly IPublicEvent publicEvent;
        private readonly IMapInstance mapInstance;
        private readonly ICreatureInfoManager creatureInfoManager;
        private readonly IFactory<ISpellParameters> spellParametersFactory;
        private readonly IStoryBuilder storyBuilder;
        private readonly HycrestDialogue dialogue;

        public HycrestAlarm(
            ILogger log,
            IPublicEvent publicEvent,
            IMapInstance mapInstance,
            ICreatureInfoManager creatureInfoManager,
            IFactory<ISpellParameters> spellParametersFactory,
            IStoryBuilder storyBuilder,
            HycrestDialogue dialogue)
        {
            this.log                    = log;
            this.publicEvent            = publicEvent;
            this.mapInstance            = mapInstance;
            this.creatureInfoManager    = creatureInfoManager;
            this.spellParametersFactory = spellParametersFactory;
            this.storyBuilder           = storyBuilder;
            this.dialogue               = dialogue;
        }

        /// <summary>
        /// Spawn the Recon Specialist at <paramref name="position"/>, engaging <paramref name="target"/>.
        /// </summary>
        public void Trigger(Vector3 position, IPlayer target)
        {
            // it can go off again once the previous alarm is over
            if (IsActive)
                return;

            flare = null;

            specialist = Spawn(ReconSpecialist, position, target.Position);
            if (specialist == null)
                return;

            called.Add(specialist);

            targetGuid     = target.Guid;
            state          = AlarmState.Calling;
            callSpell      = null;
            stateTimer     = CastDelay.TotalSeconds;
            approachTime   = 0d;

            // engage once on the map, so he runs at the player
            IUnitEntity runner = specialist;
            actionQueue.Enqueue(TimeSpan.FromMilliseconds(500), () =>
            {
                IPlayer player = mapInstance.GetEntity<IPlayer>(targetGuid);
                if (runner.InWorld && runner.IsAlive && player is { IsAlive: true })
                    runner.ThreatManager.UpdateThreat(player, 1);
            });

            SendCommunicator(VesnaFlare, VesnaFlareDurationMs);
            log.LogInformation($"Hycrest: alarm, Recon Specialist spawned at {position}.");
        }

        /// <summary>
        /// A Recon Specialist that was already standing in the field (<paramref name="unit"/>) calls reinforcements against
        /// <paramref name="target"/>.
        /// </summary>
        public void TriggerWith(IUnitEntity unit, IPlayer target)
        {
            if (IsActive || unit is not { InWorld: true, IsAlive: true } || target == null || !called.Add(unit))
                return;

            flare        = null;
            specialist   = unit;
            targetGuid   = target.Guid;
            state        = AlarmState.Calling;
            callSpell    = null;
            stateTimer   = CastDelay.TotalSeconds;
            approachTime = 0d;
            approachChaseTimer = 0d;

            SendCommunicator(VesnaFlare, VesnaFlareDurationMs);
            log.LogInformation($"Hycrest: alarm, the Recon Specialist at {unit.Position} calls reinforcements.");
        }

        /// <summary>
        /// A creature's AI picked a script-handled spell from its spell list (the public event hook): a Recon
        /// Specialist's Call Reinforcements sets off this alarm, unless it is already running or he already called.
        /// </summary>
        public void OnCreatureSpell(IUnitEntity caster, IUnitEntity target, uint spell4Id)
        {
            if (spell4Id != CallReinforcements || caster.CreatureId != ReconSpecialist || caster.Map != mapInstance)
                return;

            IPlayer player = target as IPlayer
                ?? mapInstance.GetPlayers()
                    .Where(p => p.IsAlive)
                    .OrderBy(p => Vector3.Distance(p.Position, caster.Position))
                    .FirstOrDefault();
            TriggerWith(caster, player);
        }

        public void Update(double lastTick)
        {
            actionQueue.Update(lastTick);

            switch (state)
            {
                case AlarmState.Calling:
                    UpdateCalling(lastTick);
                    break;
                case AlarmState.Flare:
                    UpdateFlare(lastTick);
                    break;
                case AlarmState.Arriving:
                    UpdateArriving(lastTick);
                    break;
            }
        }

        private void UpdateCalling(double lastTick)
        {
            if (!specialist.InWorld || !specialist.IsAlive)
            {
                // not added to the map yet
                if (callSpell == null && stateTimer > 0d)
                {
                    stateTimer -= lastTick;
                    return;
                }

                Stop("the Recon Specialist is dead");
                return;
            }

            if (callSpell == null)
            {
                stateTimer -= lastTick;
                if (stateTimer > 0d)
                    return;

                // still running in
                approachTime += lastTick;
                IPlayer target = mapInstance.GetEntity<IPlayer>(targetGuid);
                if (target is { IsAlive: true }
                    && Vector3.Distance(target.Position, specialist.Position) > CallRange
                    && approachTime < MaxApproachTime.TotalSeconds)
                {
                    // he runs in to call, without shooting first: his combat AI would keep him at pistol range and attack
                    // (Teun, 7 Oct 2026), so it waits (busy) while the alarm runs him in
                    specialist.SetBusy(ApproachBusy);
                    approachChaseTimer -= lastTick;
                    if (approachChaseTimer <= 0d)
                    {
                        approachChaseTimer = ApproachChaseInterval;
                        specialist.MovementManager.Chase(target, CallRange - 2f);
                    }
                    return;
                }

                StartCall();
                return;
            }

            callElapsed += lastTick;
            // the cast is done once it stops casting: the spell itself goes on while its summon effect lasts (until the
            // specialist dies), so waiting for it to finish never released the flare (Teun's test, 5 Oct 2026)
            if (callSpell.IsCasting)
                return;

            // a cancelled cast stops before its cast time
            if (callSpell.IsFailed || callElapsed < CastTime.TotalSeconds - 0.1d)
            {
                log.LogInformation("Hycrest: alarm, Call Reinforcements was interrupted.");
                callSpell  = null;
                stateTimer = RecastDelay.TotalSeconds;
                return;
            }

            ReleaseFlare();
        }

        private void StartCall()
        {
            dialogue.Say(specialist, SpecialistBarks[Random.Shared.Next(SpecialistBarks.Length)], false);

            IPlayer target = mapInstance.GetEntity<IPlayer>(targetGuid);
            if (target == null || !target.IsAlive)
                target = mapInstance.GetPlayers()
                    .Where(p => p.IsAlive)
                    .OrderBy(p => Vector3.Distance(p.Position, specialist.Position))
                    .FirstOrDefault();
            if (target == null)
                return;

            targetGuid = target.Guid;
            specialist.ThreatManager.UpdateThreat(target, 1);

            ISpellParameters parameters = spellParametersFactory.Resolve();
            parameters.PrimaryTargetId        = target.Guid;
            parameters.UserInitiatedSpellCast = false;
            specialist.CastSpell(CallReinforcements, parameters);

            callSpell   = specialist.GetSpellBySpellId(CallReinforcements);
            callElapsed = 0d;
            if (callSpell == null)
            {
                log.LogWarning("Hycrest: alarm, the Recon Specialist couldn't cast Call Reinforcements.");
                stateTimer = RecastDelay.TotalSeconds;
            }
        }

        private void ReleaseFlare()
        {
            // the flare and the drill are faction 1451, which the engine treats as hostile to the Dominion: the
            // specialist's own Precision Blast destroyed the flare (Teun's test, 5 Oct 2026). They take his faction, the
            // players can still destroy them
            flare      = Spawn(AssistanceFlare, specialist.Position, specialist.Position, specialist.Faction1);
            state      = AlarmState.Flare;
            stateTimer = FlareDuration.TotalSeconds;

            Broadcast(FlareTooltip);
            log.LogInformation("Hycrest: alarm, signal flare released.");
        }

        private void UpdateFlare(double lastTick)
        {
            // the flare counts as destroyed once it has been on the map and is gone or dead
            if (flare != null && flare.Guid == 0u && stateTimer < FlareDuration.TotalSeconds - 1d
                || flare is { InWorld: true, IsAlive: false })
            {
                Stop("the flare was destroyed");
                return;
            }

            stateTimer -= lastTick;
            if (stateTimer > 0d)
                return;

            Vector3 origin = flare?.Position ?? specialist.Position;
            if (flare is { InWorld: true })
                flare.RemoveFromMap();

            IPlayer target = mapInstance.GetEntity<IPlayer>(targetGuid);
            Vector3 towards = target?.Position ?? origin;
            Vector3 position = MoveTowards(origin, towards, DrillDistance);

            IUnitEntity drill = Spawn(ChuaGroundDrill, position, towards, DominionFaction);
            if (drill != null)
            {
                actionQueue.Enqueue(TimeSpan.FromMilliseconds(100), () =>
                {
                    ISpellParameters parameters = spellParametersFactory.Resolve();
                    parameters.UserInitiatedSpellCast = false;
                    drill.CastSpell(Surface, parameters);
                });
                actionQueue.Enqueue(DrillDuration, () =>
                {
                    if (drill.InWorld)
                        drill.RemoveFromMap();
                });
            }

            state      = AlarmState.Arriving;
            stateTimer = DrillSurfaceTime.TotalSeconds;
            flare      = null;
            arrivalPosition = position;
            arrivalFacing   = towards;
        }

        private Vector3 arrivalPosition;
        private Vector3 arrivalFacing;

        private void UpdateArriving(double lastTick)
        {
            stateTimer -= lastTick;
            if (stateTimer > 0d)
                return;

            state = AlarmState.Done;

            Vector3 side = Vector3.Normalize(Vector3.Cross(arrivalFacing - arrivalPosition, Vector3.UnitY));
            if (float.IsNaN(side.X))
                side = Vector3.UnitX;

            var team = new List<IUnitEntity>
            {
                Spawn(RapidResponseCommando, arrivalPosition, arrivalFacing),
                Spawn(RapidResponseDrone, arrivalPosition + side * 3f, arrivalFacing),
                Spawn(RapidResponseDrone, arrivalPosition - side * 3f, arrivalFacing)
            };

            // engage once they are on the map
            actionQueue.Enqueue(TimeSpan.FromMilliseconds(500), () =>
            {
                foreach (IUnitEntity unit in team.Where(u => u is { InWorld: true }))
                {
                    IPlayer target = mapInstance.GetPlayers()
                        .Where(p => p.IsAlive)
                        .OrderBy(p => Vector3.Distance(p.Position, unit.Position))
                        .FirstOrDefault();
                    if (target != null)
                        unit.ThreatManager.UpdateThreat(target, 1);
                }
            });

            // Vesna together with the team, not after it (she came 3 s late, after the drop pod had gone: Teun, 7 Oct 2026)
            Broadcast(TeamTooltip);
            SendCommunicator(VesnaTeam, VesnaTeamDurationMs);
            log.LogInformation("Hycrest: alarm, Rapid Response Team arrived.");
        }

        private void Stop(string reason)
        {
            if (flare is { InWorld: true })
                flare.RemoveFromMap();

            state = AlarmState.Done;
            log.LogInformation($"Hycrest: alarm stopped, {reason}.");
        }

        private IUnitEntity Spawn(uint creatureId, Vector3 position, Vector3 facing, Faction? faction = null)
        {
            ICreatureInfo creatureInfo = creatureInfoManager.GetCreatureInfo(creatureId);
            if (creatureInfo == null)
                return null;

            float? height = mapInstance.GetTerrainHeight(position.X, position.Z);
            if (height.HasValue)
                position.Y = height.Value;

            var entity = publicEvent.CreateEntity<INonPlayerEntity>();
            entity.Initialise(creatureInfo);
            if (faction.HasValue)
            {
                entity.Faction1 = faction.Value;
                entity.Faction2 = faction.Value;
            }

            Vector3 direction = facing - position;
            if (direction.LengthSquared() > 0.01f)
            {
                direction = Vector3.Normalize(direction);
                entity.Rotation = new Vector3(MathF.Atan2(-direction.X, -direction.Z), 0f, 0f);
            }

            entity.AddToMap(mapInstance, position);
            return entity;
        }

        private static Vector3 MoveTowards(Vector3 from, Vector3 to, float distance)
        {
            Vector3 direction = to - from;
            direction.Y = 0f;
            if (direction.LengthSquared() < 0.01f)
                return from;

            return from + Vector3.Normalize(direction) * MathF.Min(distance, direction.Length());
        }

        private void Broadcast(uint textId)
        {
            string text = dialogue.GetText(textId);
            foreach (IPlayer player in mapInstance.GetPlayers())
                player.Session.EnqueueMessageEncrypted(new ServerRealmBroadcast
                {
                    Tier    = BroadcastTier.Medium,
                    Message = text
                });
        }

        private void SendCommunicator(uint textId, uint durationMs)
        {
            foreach (IPlayer player in mapInstance.GetPlayers())
            {
                if (RealCommunicators
                    && CommunicatorByText.TryGetValue(textId, out ushort communicatorId)
                    && communicatorsShown.Add((player.CharacterId, communicatorId)))
                {
                    player.Session.EnqueueMessageEncrypted(new ServerCommunicatorMessage
                    {
                        CommunicatorId = communicatorId
                    });
                    continue;
                }

                storyBuilder.SendStoryCommunicator(textId, VesnaTaranoft, player, durationMs);
            }
        }
    }
}
