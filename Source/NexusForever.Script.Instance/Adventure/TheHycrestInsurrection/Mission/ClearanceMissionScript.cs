using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Abstract.Entity.Trigger;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Entity.Movement.Command.Mode;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.Game.Static.Reputation;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model.Abilities;
using NexusForever.Script.Template.Filter;
using NexusForever.Shared;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// Clearance (public event 429, tier 4 Merciful): hack a dormant Securitybot, become it and scan the Fleeing Farmers
    /// queueing at Checkpoint Gamma one by one, then meet Ayita in the Bell Farmhouse; the finale All Aboard follows.
    /// </summary>
    /// <remarks>
    /// From Teun's retail video (1 Oct 2026) and the tables (docs/hycrest/research/clearance.md): 42 farmers queue along
    /// spline 14819; a scanned farmer (wherever he stands) runs to the front and out along spline 14818, walking from the
    /// gate on, and the rest move up. The dormant bots stand in clusters of three and come back 10-20 s after being used.
    /// Activating one hides the player in it (the engine's vehicle, pilot seat); one scan (button 1: the farmer in a 10 m,
    /// 45 degree cone in front) and the player is back in their body where they took control. Enemies come later.
    /// </remarks>
    [ScriptFilterOwnerId(429u)]
    public class ClearanceMissionScript : HycrestFieldMissionScript, IClearanceMissionScript
    {
        private const uint HackSecuritybot = 541u;
        private const uint ScanFarmers     = 1923u;
        private const uint MeetAyita       = 2167u;

        private const uint DormantSecuritybot = 50772u;
        private const uint SecuritybotVehicle = 48438u;
        private const uint VehicleId          = 486u;
        private const uint RepairingSpell     = 49970u;   // the dormant bot's activate spell
        private const ushort VehicleBar       = 440;      // ActionBarShortcutSet: Security Clearance + exit
        private static readonly uint[] FleeingFarmers = [48434u, 50805u];
        private const uint AyitaSinnatus = 48032u;
        private const uint DominionGatekeeper = 26853u;   // at the queue's front (SQL, event 429)
        // the gatekeeper as a cleared farmer passes him
        private static readonly uint[] GatekeeperLines = [463923u, 463924u, 463925u, 463926u, 463927u];

        private const uint AyitaHint       = 454711u; // communicator: "Hey! Maybe you can reprogram some of the securitybots..."
        private const uint AyitaPayoff     = 465461u; // communicator: "Ha ha! I'd love to see the look on General Arsenax's face..."
        private const uint FarmerScanned   = 460756u; // "Security clearance? Me?"
        private static readonly uint[] FarmerCleared = [460757u, 460758u, 460759u, 460760u];
        private static readonly uint[] QueueBarks =
        [
            465739u, 465740u, 465741u, 465742u, 465743u, 465744u, 465745u, 465746u, 465747u, 465748u,
            465749u, 465750u, 465751u, 465752u, 465753u, 465754u, 465755u, 465756u, 465757u, 465758u
        ];

        // the queue: spline 14819 winds through the pen. Its nodes 5-47 carry the node events 1-43, the queue's places
        // (1 the front, by the checkpoint corridor); nodes 0-4 are the corridor itself, nobody stands there. 42 farmers
        // take places 1-42 (Teun, retail video: 42)
        private const ushort QueueSpline = 14819;
        // the way out: spline 15939 runs the whole queue backwards and leaves it after place 1 (its node events 99/100)
        // through the corridor to the checkpoint gate; spline 14818 goes on from the gate out of the farmlands
        private const ushort GateSpline  = 15939;
        private const ushort ExitSpline  = 14818;
        private const uint LeaveQueueEvent = 99u;
        private const int FarmerCount    = 42;
        private const float QueueWalkSpeed = 2f;
        private const float ExitWalkSpeed  = 2.5f;
        private static readonly TimeSpan BarkInterval = TimeSpan.FromSeconds(9);

        // dormant bots: clusters of three (Teun) at the Jabbithole clusters south of the pen (around WorldLocation2 24880)
        private static readonly Vector2[] BotClusters =
        [
            new(-2349f, -1308f), new(-2350f, -1282f), new(-2375f, -1289f), new(-2393f, -1283f), new(-2388f, -1307f),
            // more tight groups of sightings (Teun missed clusters in the first test); to check against the video
            new(-2353f, -1254f), new(-2329.74f, -1265.28f), new(-2322f, -1272f), new(-2316f, -1244f), new(-2362f, -1216f),
            new(-2314f, -1222f)
        ];
        private const float ClusterRadius = 1.6f;
        private static readonly Vector2 PenCentre = new(-2333.4f, -1222.3f);
        private static readonly TimeSpan BotRespawn = TimeSpan.FromSeconds(15);   // retail: 10-20 s

        // the scan: Security Clearance (46792) casts for 2 s, then one farmer in a 10 m, 45 degree cone in front
        private static readonly TimeSpan ScanCastTime = TimeSpan.FromSeconds(2);
        private const float ScanRange = 10f;
        private const float ScanHalfAngle = MathF.PI / 8f;

        // the Bell Farmhouse, where Ayita waits (objective 2167, trigger volume 5722); a small sphere inside the building
        private static readonly Vector3 AyitaFarmhouse = new(-2261.7f, -924.8f, -1330.2f);
        private const uint TriggerId = 114910u;
        private const float TriggerRange = 4f;

        private static readonly TimeSpan HintDelay = TimeSpan.FromSeconds(20);

        // the patrol (Teun, retail video): three Dominion Shocktroopers walk spline 4470 (150 m, from east of the pen to
        // the bot clusters) there and back, side by side; the standing units are in the SQL (event 429 phase 0)
        private const ushort PatrolSpline = 4470;
        private const uint DominionShocktrooper = 17858u;
        private const Faction DominionFaction = (Faction)1452;
        private const Faction FriendlyFaction = (Faction)219;
        private static readonly float[] PatrolOffsets = [-1.8f, 0f, 1.8f];   // sideways from the spline, metres
        private const float PatrolSpeed = 2f;
        private readonly Dictionary<IUnitEntity, (List<Vector3> Route, float Speed)> patrollers = [];

        // map markers: Checkpoint Gamma (39184, the scan objective's own point) for the hack and the scan alike (Teun,
        // 1 Oct 2026: 541's own point 24880 put the marker on some of the bots)
        private const uint CheckpointLocation = 39184u;
        private const uint FarmhouseLocation = 39450u; // the Bell Farmhouse

        private class BotSpot
        {
            public Vector3 Position;
            public float Rotation;
            public IWorldEntity Bot;
            public double RespawnIn;
        }

        private class Pilot
        {
            public uint PlayerGuid;
            public Vector3 Body;
            public bool Scanning;
            public uint Health;   // the pilot's health and shield at the last tick: a drop is a hit on the bot
            public uint Shield;
        }

        // enemies and the bots (Teun, retail video): Dominion units go for a bot that comes within their aggro range, and a
        // hit destroys it and puts the pilot back in their body. The bot isn't a unit in the engine (VehicleEntity), so
        // they are set on the pilot, who rides inside it: the script adds the pilot to their threat lists and their own
        // CombatAI runs at the bot and attacks; the first damage the pilot takes is the bot's (undone), and the bot goes
        private const float BotAggroRange = 15f;   // CombatAI's range check
        private static readonly TimeSpan BotAggroInterval = TimeSpan.FromSeconds(0.5);
        private double botAggroTimer;

        private readonly List<BotSpot> botSpots = [];
        private readonly Dictionary<IVehicleEntity, Pilot> pilots = [];
        private readonly List<IUnitEntity> queue = [];           // front first
        private readonly HashSet<IUnitEntity> farmers = [];      // every farmer this mission spawned
        private Vector3[] places;          // the queue's places, front first
        private List<Vector3> toGate;      // from place 1 to the checkpoint gate
        private List<Vector3> outside;     // from the gate out of the farmlands
        private double barkTimer;
        private bool hacked;
        private IVolumeGridTriggerEntity trigger;
        private IUnitEntity gatekeeper;

        private readonly TimedActionQueue actionQueue = new();

        #region Dependency Injection

        private readonly ILogger<ClearanceMissionScript> log;
        private readonly IGameTableManager gameTableManager;
        private readonly IStoryBuilder storyBuilder;
        private readonly ICreatureInfoManager creatureInfoManager;
        private readonly HycrestDialogue dialogue;

        public ClearanceMissionScript(
            ILogger<ClearanceMissionScript> log,
            IGameTableManager gameTableManager,
            IStoryBuilder storyBuilder,
            ICreatureInfoManager creatureInfoManager,
            IFactory<ISpellParameters> spellParametersFactory)
            : base(log, gameTableManager, spellParametersFactory)
        {
            this.log                 = log;
            this.gameTableManager    = gameTableManager;
            this.storyBuilder        = storyBuilder;
            this.creatureInfoManager = creatureInfoManager;
            dialogue = new HycrestDialogue(gameTableManager, actionQueue);
        }

        #endregion

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public override void OnLoad(IPublicEvent owner)
        {
            base.OnLoad(owner);

            places = SplineNodeEntries(QueueSpline)
                .Where(n => n.EventId > 0u)
                .OrderBy(n => n.EventId)
                .Select(n => new Vector3(n.Position0, n.Position1, n.Position2))
                .ToArray();
            toGate = SplineNodeEntries(GateSpline)
                .SkipWhile(n => n.EventId != LeaveQueueEvent)
                .Select(n => new Vector3(n.Position0, n.Position1, n.Position2))
                .ToList();
            Vector3[] exit = SplineNodes(ExitSpline);
            Vector3 gate = toGate[^1];
            int afterGate = Array.FindIndex(exit, n => n.Z > gate.Z);   // the exit runs north (+Z) from the gate
            outside = [gate, .. exit.Skip(afterGate < 0 ? exit.Length : afterGate)];

            publicEvent.SetObjectiveLocations(HackSecuritybot, CheckpointLocation);
            publicEvent.SetObjectiveLocations(ScanFarmers, CheckpointLocation);
            publicEvent.SetObjectiveLocations(MeetAyita, FarmhouseLocation);
            publicEvent.ActivateObjective(ScanFarmers);
            SpawnFarmers();
            SpawnPatrol();
            foreach (Vector2 centre in BotClusters)
            {
                for (int i = 0; i < 3; i++)
                {
                    float angle = MathF.Tau * i / 3f + 0.4f;
                    var position = new Vector3(centre.X + MathF.Cos(angle) * ClusterRadius, 0f, centre.Y + MathF.Sin(angle) * ClusterRadius);
                    var spot = new BotSpot
                    {
                        Position = Grounded(position),
                        Rotation = MathF.Atan2(-(PenCentre.X - position.X), -(PenCentre.Y - position.Z))
                    };
                    botSpots.Add(spot);
                    SpawnBot(spot);
                }
            }

            actionQueue.Enqueue(HintDelay, () => Communicator(AyitaHint, AyitaSinnatus));
            barkTimer = BarkInterval.TotalSeconds;
            log.LogInformation($"Hycrest: Clearance, {FarmerCount} farmers in the queue, {botSpots.Count} dormant Securitybots.");
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public override void Update(double lastTick)
        {
            actionQueue.Update(lastTick);
            UpdateWalks(lastTick);
            UpdateBotHits();

            botAggroTimer -= lastTick;
            if (botAggroTimer <= 0d)
            {
                botAggroTimer = BotAggroInterval.TotalSeconds;
                UpdateBotAggro();
            }

            foreach (BotSpot spot in botSpots.Where(s => s.Bot == null))
            {
                spot.RespawnIn -= lastTick;
                if (spot.RespawnIn <= 0d && !publicEvent.HasFinished)
                    SpawnBot(spot);
            }

            barkTimer -= lastTick;
            if (barkTimer <= 0d)
            {
                barkTimer = BarkInterval.TotalSeconds * (0.6 + Random.Shared.NextDouble() * 0.8);
                IUnitEntity farmer = queue.Count > 0 ? queue[Random.Shared.Next(queue.Count)] : null;
                if (farmer is { InWorld: true })
                    dialogue.Bark(farmer, QueueBarks[Random.Shared.Next(QueueBarks.Length)]);
            }
        }

        /// <summary>
        /// Invoked when a <see cref="IGridEntity"/> is added to the map the public event is on.
        /// </summary>
        public override void OnAddToMap(IGridEntity entity)
        {
            // every farmer is one to scan: the tracker shows "N Remaining" (count 0 Exterminate) and counts down
            if (entity is IUnitEntity unit && farmers.Contains(unit))
                publicEvent.AddObjectiveTarget(ScanFarmers, unit);

            if (entity is IUnitEntity { CreatureId: DominionGatekeeper } keeper)
                gatekeeper = keeper;

            if (entity is IUnitEntity patroller && patrollers.Remove(patroller, out var patrol))
                StartPatrol(patroller, patrol.Route, patrol.Speed);
        }

        /// <summary>
        /// Invoked when the status of an objective changes.
        /// </summary>
        public override void OnPublicEventObjectiveStatus(IPublicEventObjective objective)
        {
            if (objective.Status != PublicEventStatus.Succeeded)
                return;

            switch (objective.Entry.Id)
            {
                case ScanFarmers:
                    log.LogInformation("Hycrest: Clearance, every farmer is through.");
                    Communicator(AyitaPayoff, AyitaSinnatus);
                    actionQueue.Enqueue(TimeSpan.FromSeconds(1), StartMeetAyita);
                    break;
                case MeetAyita:
                    if (trigger?.InWorld == true)
                        trigger.RemoveFromMap();
                    CompleteMission();
                    break;
            }
        }

        // --- the queue ------------------------------------------------------------------------------------------------

        private void SpawnFarmers()
        {
            for (int i = 0; i < FarmerCount && i < places.Length; i++)
            {
                // facing the place in front (the front one faces where the queue leaves)
                Vector3 ahead = i > 0 ? places[i - 1] : toGate[0];
                // friendly (green, can't be attacked) like the rebels
                IUnitEntity farmer = Spawn(FleeingFarmers[i % FleeingFarmers.Length], places[i], ahead, grounded: false, faction: FriendlyFaction);
                if (farmer == null)
                    continue;

                queue.Add(farmer);
                farmers.Add(farmer);
            }
        }

        /// <summary>
        /// <paramref name="farmer"/> is cleared: he runs to the front and out along the exit spline (walking from the gate on)
        /// and despawns at its end; everyone behind him moves up a place.
        /// </summary>
        private void ClearFarmer(IUnitEntity farmer)
        {
            int slot = queue.IndexOf(farmer);
            if (slot < 0)
                return;

            queue.RemoveAt(slot);
            publicEvent.RemoveObjectiveTarget(ScanFarmers, farmer.Guid, defeated: true);

            for (int i = slot; i < queue.Count; i++)
                Walk(queue[i], [places[i + 1], places[i]], QueueWalkSpeed);

            // along the queue to the front and through the corridor to the gate running, from the gate on walking
            List<Vector3> run = [];
            for (int i = slot; i >= 0; i--)
                run.Add(places[i]);
            run.AddRange(toGate);
            List<Vector3> walk = outside;

            float runSpeed = RunSpeedOf(farmer);
            float runTime  = Walk(farmer, run, runSpeed);

            // the gatekeeper sends him on as he reaches the front
            float toFront = PathLength(run.Take(slot + 1).ToList()) / runSpeed;
            uint keeperLine = GatekeeperLines[Random.Shared.Next(GatekeeperLines.Length)];
            actionQueue.Enqueue(TimeSpan.FromSeconds(toFront), () =>
            {
                if (gatekeeper is { InWorld: true, IsAlive: true })
                    dialogue.Bark(gatekeeper, keeperLine);
            });
            uint line = FarmerCleared[Random.Shared.Next(FarmerCleared.Length)];
            actionQueue.Enqueue(TimeSpan.FromSeconds(0.8), () =>
            {
                if (farmer.InWorld && Random.Shared.NextDouble() < 0.5)
                    dialogue.Bark(farmer, line);
            });
            actionQueue.Enqueue(TimeSpan.FromSeconds(runTime), () =>
            {
                if (!farmer.InWorld)
                    return;

                float walkTime = Walk(farmer, walk, ExitWalkSpeed);
                actionQueue.Enqueue(TimeSpan.FromSeconds(walkTime), () =>
                {
                    if (farmer.InWorld)
                        farmer.RemoveFromMap();
                });
            });
        }

        // --- the patrol ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Three shocktroopers side by side on <see cref="PatrolSpline"/>: each walks the spline shifted sideways, at a speed
        /// that makes its round take as long as the middle one's, so they stay abreast.
        /// </summary>
        private void SpawnPatrol()
        {
            Vector3[] nodes = SplineNodes(PatrolSpline);
            if (nodes.Length < 2)
                return;

            float middle = PathLength(nodes);
            foreach (float offset in PatrolOffsets)
            {
                List<Vector3> route = [];
                for (int i = 0; i < nodes.Length; i++)
                {
                    Vector3 along = nodes[Math.Min(i + 1, nodes.Length - 1)] - nodes[Math.Max(i - 1, 0)];
                    var side = Vector3.Normalize(new Vector3(-along.Z, 0f, along.X));
                    route.Add(Grounded(nodes[i] + side * offset));
                }

                IUnitEntity unit = Spawn(DominionShocktrooper, route[0], route[1], faction: DominionFaction);
                if (unit != null)
                    patrollers[unit] = (route, PatrolSpeed * PathLength(route) / middle);
            }
        }

        private static float PathLength(IReadOnlyList<Vector3> path)
        {
            float length = 0f;
            for (int i = 1; i < path.Count; i++)
                length += Vector3.Distance(path[i - 1], path[i]);
            return length;
        }

        // --- the Securitybots -------------------------------------------------------------------------------------------

        private void SpawnBot(BotSpot spot)
        {
            Vector3 facing = spot.Position + new Vector3(-MathF.Sin(spot.Rotation), 0f, -MathF.Cos(spot.Rotation));
            spot.Bot = Spawn(DormantSecuritybot, spot.Position, facing);
        }

        /// <summary>
        /// <paramref name="player"/> hacked a dormant bot: it goes (back in a while) and the player becomes the bot.
        /// </summary>
        public void OnSecuritybotHacked(IWorldEntity bot, IPlayer player)
        {
            BotSpot spot = botSpots.FirstOrDefault(s => s.Bot == bot);
            if (spot == null || publicEvent.HasFinished || pilots.Values.Any(p => p.PlayerGuid == player.Guid) || player.PlatformGuid != null)
                return;

            ICreatureInfo creatureInfo = creatureInfoManager.GetCreatureInfo(SecuritybotVehicle);
            if (creatureInfo == null)
                return;

            bot.RemoveFromMap();
            spot.Bot       = null;
            spot.RespawnIn = BotRespawn.TotalSeconds;

            var vehicle = publicEvent.CreateEntity<IVehicleEntity>();
            vehicle.Initialise(creatureInfo, VehicleId, RepairingSpell);
            vehicle.Rotation = new Vector3(spot.Rotation, 0f, 0f);
            vehicle.AddToMap(mapInstance, spot.Position);
            vehicle.EnqueuePassengerAdd(player, VehicleSeatType.Pilot, 0);
            pilots[vehicle] = new Pilot { PlayerGuid = player.Guid, Body = player.Position, Health = player.Health, Shield = player.Shield };

            // its bar, once the vehicle is on the client
            uint playerGuid = player.Guid;
            actionQueue.Enqueue(TimeSpan.FromSeconds(0.5), () =>
            {
                if (vehicle.InWorld && mapInstance.GetEntity<IPlayer>(playerGuid) is IPlayer pilot)
                    pilot.Session.EnqueueMessageEncrypted(new ServerShowActionBar
                    {
                        ShortcutSet            = ShortcutSet.VehicleBar,
                        ActionBarShortcutSetId = VehicleBar,
                        AssociatedUnitId       = vehicle.Guid
                    });
            });

            if (!hacked)
            {
                hacked = true;
                publicEvent.UpdateObjective(HackSecuritybot, 1);
            }
            log.LogInformation($"Hycrest: Clearance, {player.Name} hacked a Securitybot.");
        }

        /// <summary>
        /// A button of the bot's bar: button 1 scans (the one farmer in the cone in front), then the pilot is back in their body.
        /// </summary>
        public void OnSecuritybotAbility(IVehicleEntity vehicle, IPlayer pilot, ushort index, bool pressed)
        {
            log.LogInformation($"Hycrest: Clearance, vehicle bar button {index} (pressed {pressed}) from {pilot.Name}.");
            // the bar's message (0x96) isn't mapped yet, its index may not be the button's: until it is, any button scans
            // (the exit button sends ClientVehicleDisembark instead)
            if (!pilots.TryGetValue(vehicle, out Pilot state) || state.Scanning)
                return;

            state.Scanning = true;
            actionQueue.Enqueue(ScanCastTime, () =>
            {
                if (!vehicle.InWorld)
                    return;

                IUnitEntity farmer = FarmerInCone(vehicle);
                if (farmer != null)
                {
                    dialogue.Bark(farmer, FarmerScanned);
                    ClearFarmer(farmer);
                }

                // the scan is done: back to the body (OnSecuritybotGone)
                actionQueue.Enqueue(TimeSpan.FromSeconds(0.5), () =>
                {
                    if (vehicle.InWorld && mapInstance.GetEntity<IPlayer>(state.PlayerGuid) is IPlayer player && vehicle.GetPassenger(player.Guid) != null)
                        vehicle.PassengerRemove(player);
                });
            });
        }

        /// <summary>
        /// The bot is gone (scan done, exit button, destroyed): its pilot is back in their body, where they took control.
        /// </summary>
        public void OnSecuritybotGone(IVehicleEntity vehicle)
        {
            if (!pilots.Remove(vehicle, out Pilot state))
                return;

            if (mapInstance.GetEntity<IPlayer>(state.PlayerGuid) is not IPlayer player)
                return;

            // the bot is gone: whoever was after it has nothing to fight. CombatAI's reset walks them back to their spawn
            // point; the patrol goes straight back to its route instead (same tick, overrides the reset's walk)
            foreach (IUnitEntity enemy in Enemies())
            {
                enemy.ThreatManager.RemoveHostile(player.Guid);
                ResumeWalk(enemy);
            }

            player.Session.EnqueueMessageEncrypted(new ServerShowActionBar { ShortcutSet = ShortcutSet.VehicleBar });
            // at once, in the tick the client hears the passenger leave: leaving a vehicle plays the client's jump-off
            // animation (Teun, 1 Oct 2026; retail: the player just stands there again), the teleport cuts it short
            if (player.CanTeleport())
                player.TeleportToLocal(state.Body, false);
        }

        /// <summary>
        /// Dominion units of the mission that aren't fighting yet go for a bot within their aggro range.
        /// </summary>
        private void UpdateBotAggro()
        {
            foreach ((IVehicleEntity vehicle, Pilot state) in pilots)
            {
                if (!vehicle.InWorld || mapInstance.GetEntity<IPlayer>(state.PlayerGuid) is not IPlayer player)
                    continue;

                Vector3 position = vehicle.MovementManager.GetPosition();
                foreach (IUnitEntity enemy in Enemies())
                    if (!enemy.InCombat && Vector3.Distance(enemy.Position, position) <= BotAggroRange)
                        enemy.ThreatManager.UpdateThreat(player, 1);
            }
        }

        /// <summary>
        /// A pilot who took damage: the hit was the bot's. The damage is undone and the bot is destroyed.
        /// </summary>
        private void UpdateBotHits()
        {
            foreach ((IVehicleEntity vehicle, Pilot state) in pilots.ToList())
            {
                if (mapInstance.GetEntity<IPlayer>(state.PlayerGuid) is not IPlayer player)
                    continue;

                if (player.Health + player.Shield >= state.Health + state.Shield)
                {
                    // regeneration
                    state.Health = player.Health;
                    state.Shield = player.Shield;
                    continue;
                }

                if (player.Health < state.Health)
                    player.ModifyHealth(state.Health - player.Health, DamageType.Heal, null);
                player.Shield = state.Shield;

                log.LogInformation($"Hycrest: Clearance, {player.Name}'s Securitybot was hit and destroyed.");
                if (vehicle.InWorld && vehicle.GetPassenger(player.Guid) != null)
                    vehicle.PassengerRemove(player);
            }
        }

        private IEnumerable<IUnitEntity> Enemies()
        {
            return publicEvent.GetEntities()
                .OfType<IUnitEntity>()
                .Where(u => u.InWorld && u.IsAlive && u.Faction1 == DominionFaction);
        }

        private IUnitEntity FarmerInCone(IVehicleEntity vehicle)
        {
            Vector3 position = vehicle.MovementManager.GetPosition();
            float rotation   = vehicle.MovementManager.GetRotation().X;
            var forward = new Vector2(-MathF.Sin(rotation), -MathF.Cos(rotation));

            IUnitEntity best = null;
            float bestDistance = float.MaxValue;
            foreach (IUnitEntity farmer in queue.Where(f => f.InWorld))
            {
                var offset = new Vector2(farmer.Position.X - position.X, farmer.Position.Z - position.Z);
                float distance = offset.Length();
                if (distance > ScanRange || distance >= bestDistance)
                    continue;
                if (distance > 0.5f && MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(offset), forward), -1f, 1f)) > ScanHalfAngle)
                    continue;

                best = farmer;
                bestDistance = distance;
            }
            return best;
        }

        // --- the end ----------------------------------------------------------------------------------------------------

        /// <summary>
        /// Every farmer is through: meet Ayita in the Bell Farmhouse (the whole party, a trigger inside the house).
        /// </summary>
        private void StartMeetAyita()
        {
            if (publicEvent.HasFinished)
                return;

            // Ayita goes ahead to the Bell Farmhouse
            mapInstance.PublicEventManager.GetEvent(HycrestPublicEvent.Main)?
                .InvokeScriptCollection<IHycrestMainEventScript>(s => s.PrepareHideout(publicEvent.Id));

            PublicEventObjectiveEntry entry = gameTableManager.PublicEventObjective.GetEntry(MeetAyita);
            publicEvent.ActivateObjective(MeetAyita, (uint)Math.Max(1, mapInstance.PlayerCount));

            trigger = publicEvent.CreateEntity<IVolumeGridTriggerEntity>();
            trigger.Initialise(TriggerId, TriggerRange, entry?.ObjectId ?? 0u);
            trigger.AddToMap(mapInstance, AyitaFarmhouse);
        }

        // --- helpers ----------------------------------------------------------------------------------------------------

        private void Communicator(uint textId, uint creatureId)
        {
            foreach (IPlayer player in mapInstance.GetPlayers())
                storyBuilder.SendStoryCommunicator(textId, creatureId, player);
        }

        private float Walk(IUnitEntity unit, List<Vector3> path, float speed)
        {
            if (path.Count < 2 || !unit.InWorld)
                return 0f;

            float length = 0f;
            for (int i = 1; i < path.Count; i++)
                length += Vector3.Distance(path[i - 1], path[i]);
            if (length < 0.05f)
                return 0f;

            unit.MovementManager.SetMode(ModeType.Walk);
            unit.MovementManager.LaunchSpline(path, SplineType.Linear, SplineMode.OneShot, speed);
            return length / speed;
        }

        /// <summary>
        /// A speed the client shows as a run: move speed x 8 x model scale (HYCREST.md "How to: make a scripted NPC run").
        /// </summary>
        private static float RunSpeedOf(IUnitEntity unit)
        {
            float scale = unit.CreatureInfo?.Entry.ModelScale ?? 1f;
            if (scale <= 0f)
                scale = 1f;
            float speed = unit.GetPropertyValue(Property.MoveSpeedMultiplier) * 8f * scale;
            return speed > 0f ? speed : 8f;
        }

        private List<Spline2NodeEntry> SplineNodeEntries(ushort splineId)
        {
            return gameTableManager.Spline2Node.Entries
                .Where(n => n.SplineId == splineId)
                .OrderBy(n => n.Ordinal)
                .ToList();
        }

        private Vector3[] SplineNodes(ushort splineId)
        {
            var nodes = new List<Vector3>();
            foreach (Spline2NodeEntry node in SplineNodeEntries(splineId))
            {
                var position = new Vector3(node.Position0, node.Position1, node.Position2);
                if (nodes.Count == 0 || Vector3.Distance(nodes[^1], position) > 0.05f)
                    nodes.Add(position);
            }
            return nodes.ToArray();
        }

        private IUnitEntity Spawn(uint creatureId, Vector3 position, Vector3 facing, bool grounded = true, Faction? faction = null)
        {
            ICreatureInfo creatureInfo = creatureInfoManager.GetCreatureInfo(creatureId);
            if (creatureInfo == null)
                return null;

            var entity = publicEvent.CreateEntity<INonPlayerEntity>();
            entity.Initialise(creatureInfo);
            if (faction.HasValue)
            {
                entity.Faction1 = faction.Value;
                entity.Faction2 = faction.Value;
            }
            var direction = new Vector3(facing.X - position.X, 0f, facing.Z - position.Z);
            if (direction.LengthSquared() > 0.0001f)
                entity.Rotation = new Vector3(MathF.Atan2(-direction.X, -direction.Z), 0f, 0f);

            entity.AddToMap(mapInstance, grounded ? Grounded(position) : position);
            return entity;
        }

        private Vector3 Grounded(Vector3 position)
        {
            float? height = mapInstance.GetTerrainHeight(position.X, position.Z);
            if (height.HasValue)
                position.Y = height.Value;
            return position;
        }
    }
}
