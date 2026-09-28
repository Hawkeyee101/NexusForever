using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Abstract.Quest;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Entity.Movement.Command.Mode;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.GameTable;
using NexusForever.Script.Template.Filter;
using NexusForever.Shared;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// The Farmer's Daughter (public event 420, tier 1 Merciful): speak with Farmer Tarquim Arcwulff, then free his daughter
    /// Prema and his milk maid Millithea from their Dominion guards.
    /// </summary>
    /// <remarks>
    /// Layout A of retail (Millithea near the southern fields); retail has a second layout with Millithea elsewhere. The
    /// captives can only be talked to (objective 201) once their guards are dead: two Predator Drones for Millithea, the
    /// Shatterforce Responsebot for Prema. Positions: HYCREST.md, "The Farmer's Daughter".
    /// </remarks>
    [ScriptFilterOwnerId(420u)]
    public class TheFarmersDaughterMissionScript : HycrestMissionScript
    {
        private const uint SpeakWithTarquim = 1731u;
        private const uint RescueCaptives   = 201u;

        private const uint Tarquim        = 17773u;
        private const uint Millithea      = 49490u;
        private const uint Prema          = 17772u;
        private const uint PredatorDrone  = 51026u;
        private const uint Responsebot    = 18509u;
        private const uint DominionScout  = 17856u;
        private const uint SpotlightTarget = 17763u;
        private const uint AyitaSinnatus  = 48032u;

        private const uint TarquimPlea         = 438721u; // "Help! Please, you must! My daughter, Prema, and our milk maid..."
        private const uint AyitaHurry          = 454699u; // communicator: "Something tells me that if you don't hurry..."
        private const uint MillitheaAfraid     = 464006u; // "I don't want to be eaten!"
        private const uint MillitheaThanks     = 446755u; // "Thank you, but Prema's still in danger!..."
        private const uint PremaStopThis       = 450655u; // "Stop this!"
        private const uint PremaEeep           = 464067u; // "Eeep!"
        private const uint PremaThanks         = 447119u; // "Oh, thank you so much!..."
        private const uint AyitaMissionPayoff  = 444044u; // communicator: "Prema's home safe and sound..."

        // map markers and highlighted regions (WorldLocation2, WorldSocket): the objectives have none in the tables, these
        // are the table points next to Tarquim and Millithea (layout A) in the sockets around them; Prema's is the nearest
        // point (about 20 m), outside any socket
        private const uint TarquimLocation   = 13041u;
        private const uint TarquimSocket     = 1439u;
        private const uint MillitheaLocation = 39383u;
        private const uint MillitheaSocket   = 1414u;
        private const uint PremaLocation     = 40050u;

        // captives call out when a player comes this close
        private const float CaptiveCallRange = 20f;

        // Prema and the Responsebot spawn in this phase, once Millithea is freed
        private const uint PremaPhase = 1u;

        private const float PatrolSpeed = 2f;

        // spotlight targets (Automated Machine Gun - Spotlight Target): they move slowly along a lane and the machine gun
        // opens fire when a player walks into one. Retail: 46970 (Automated Machine Gun Fire, 6 m range, 5.5 s cooldown)
        // pulses its damage proxy 46971 (10 m red telegraph, 3% health) for 5 s. The engine drops the proxy ticks (the
        // multiphase spell finishes right away), so the script casts the proxy itself, once per channel pulse.
        private const float SpotlightSpeed = 1.5f;
        private const float SpotlightRange = 6f;
        private const uint SpotlightFireSpell = 46970u;
        private const uint SpotlightDamageSpell = 46971u;
        private static readonly TimeSpan SpotlightFireDuration = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan SpotlightPulseInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan SpotlightCooldown = TimeSpan.FromSeconds(5.5);

        // the alarm: crossing this line (spline 4648, 77 m across the field north of Millithea) calls a Recon Specialist
        // this far from the party, towards Prema. Retail's trigger isn't in the tables; the specialist was measured at
        // -2413, -1587, between Millithea and Prema
        private const uint AlarmSpline = 4648u;
        private const float AlarmLineRange = 3f;
        private const float AlarmSpawnDistance = 20f;
        private const float PartyRange = 30f;
        private static readonly Vector3 PremaPosition = new(-2377.9238f, -929.3451f, -1641.9752f);

        private static readonly TimeSpan MissionEndDelay = TimeSpan.FromSeconds(4);

        // patrolling scouts: spawn position (first node) and the route they walk back and forth
        private static readonly Vector3[][] Patrols =
        [
            [new(-2451.276f, -927.9198f, -1214.2766f), new(-2463.5671f, -927.2098f, -1194.4918f), new(-2452.616f, -925.6125f, -1181.2755f)],
            [new(-2452.7524f, -928.83093f, -1555.9928f), new(-2438.631f, -929.44073f, -1579.1681f)],
            [new(-2381.129f, -923.09406f, -1698.4329f), new(-2348.2769f, -923.348f, -1698.5614f)]
        ];

        // spotlight lanes: spawn position and the other end (the first spotlight's route isn't known yet, it stands still)
        private static readonly Vector3[][] SpotlightLanes =
        [
            [new(-2468.567f, -929.0733f, -1593.2743f), new(-2470.2705f, -925.0637f, -1659.7626f)],
            [new(-2490.1282f, -920.8113f, -1672.5258f), new(-2492.663f, -929.0888f, -1607.7689f)],
            [new(-2402.416f, -928.3815f, -1673.6968f), new(-2401.0205f, -927.10114f, -1683.4856f)]
        ];

        private uint tarquimGuid;
        private uint millitheaGuid;
        private uint premaGuid;
        private uint responsebotGuid;
        private readonly HashSet<uint> millitheaGuards = [];
        private readonly Dictionary<uint, Spotlight> spotlights = [];

        private bool millitheaCalled;
        private bool millitheaFree;
        private bool millitheaThanked;
        private bool premaCalled;
        private bool premaFree;
        private bool premaThanked;
        private bool rescueStarted;
        private bool ending;

        private readonly TimedActionQueue actionQueue = new();

        private HycrestAlarm alarm;
        private List<Vector2> alarmLine;

        #region Dependency Injection

        private readonly ILogger<TheFarmersDaughterMissionScript> log;
        private readonly IStoryBuilder storyBuilder;
        private readonly IFactory<ISpellParameters> spellParametersFactory;
        private readonly IGameTableManager gameTableManager;
        private readonly ICreatureInfoManager creatureInfoManager;
        private readonly IGlobalQuestManager globalQuestManager;
        private readonly HycrestDialogue dialogue;

        public TheFarmersDaughterMissionScript(
            ILogger<TheFarmersDaughterMissionScript> log,
            IGameTableManager gameTableManager,
            IStoryBuilder storyBuilder,
            IFactory<ISpellParameters> spellParametersFactory,
            ICreatureInfoManager creatureInfoManager,
            IGlobalQuestManager globalQuestManager)
        {
            this.log                    = log;
            this.storyBuilder           = storyBuilder;
            this.spellParametersFactory = spellParametersFactory;
            this.gameTableManager       = gameTableManager;
            this.creatureInfoManager    = creatureInfoManager;
            this.globalQuestManager     = globalQuestManager;
            dialogue = new HycrestDialogue(gameTableManager, actionQueue);
        }

        #endregion

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public override void OnLoad(IPublicEvent owner)
        {
            base.OnLoad(owner);

            publicEvent.SetObjectiveLocations(SpeakWithTarquim, TarquimLocation);
            publicEvent.SetObjectiveMapRegions(SpeakWithTarquim, (TarquimSocket, TarquimLocation));
            publicEvent.SetObjectiveLocations(RescueCaptives, MillitheaLocation);
            publicEvent.SetObjectiveMapRegions(RescueCaptives, (MillitheaSocket, MillitheaLocation));

            alarm = new HycrestAlarm(log, publicEvent, mapInstance, creatureInfoManager, spellParametersFactory,
                globalQuestManager, dialogue);
            alarmLine = gameTableManager.Spline2Node.Entries
                .Where(n => n.SplineId == AlarmSpline)
                .OrderBy(n => n.Ordinal)
                .Select(n => new Vector2(n.Position0, n.Position2))
                .ToList();
        }

        /// <summary>
        /// Invoked when a <see cref="IGridEntity"/> is added to the map the public event is on.
        /// </summary>
        public override void OnAddToMap(IGridEntity entity)
        {
            if (entity is not IWorldEntity worldEntity || !IsOwnEntity(entity))
                return;

            switch (worldEntity.CreatureId)
            {
                case Tarquim:
                    tarquimGuid = worldEntity.Guid;
                    worldEntity.StandState = StandState.Sit;
                    break;
                case Millithea:
                    millitheaGuid = worldEntity.Guid;
                    worldEntity.StandState = StandState.Sit;
                    worldEntity.InteractionBlocked = true;
                    break;
                case Prema:
                    premaGuid = worldEntity.Guid;
                    worldEntity.StandState = StandState.Sit;
                    worldEntity.InteractionBlocked = true;
                    break;
                case PredatorDrone:
                    millitheaGuards.Add(worldEntity.Guid);
                    break;
                case Responsebot:
                    responsebotGuid = worldEntity.Guid;
                    break;
                case DominionScout:
                    StartRoute(worldEntity, Patrols, PatrolSpeed);
                    break;
                case SpotlightTarget:
                    spotlights[worldEntity.Guid] = new Spotlight();
                    StartRoute(worldEntity, SpotlightLanes, SpotlightSpeed);
                    break;
            }
        }

        private static void StartRoute(IWorldEntity entity, Vector3[][] routes, float speed)
        {
            // the route whose first node is where the entity spawned
            Vector3[] route = routes.FirstOrDefault(r => Vector3.Distance(r[0], entity.Position) < 1f);
            if (route == null)
                return;

            // walked as a loop over the route and back (A, B, C, B), the client jumped back to the start with BackAndForth;
            // LaunchSpline also sets the moving state and faces the entity where it walks
            List<Vector3> nodes = [.. route];
            for (int i = route.Length - 2; i > 0; i--)
                nodes.Add(route[i]);

            entity.MovementManager.SetMode(ModeType.Walk);
            entity.MovementManager.LaunchSpline(nodes, SplineType.Linear, SplineMode.Cyclic, speed);
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public override void Update(double lastTick)
        {
            actionQueue.Update(lastTick);

            if (publicEvent.HasFinished)
                return;

            UpdateMillithea();
            UpdatePrema();
            UpdateSpotlights(lastTick);
            UpdateAlarm(lastTick);
        }

        private void UpdateMillithea()
        {
            IWorldEntity millithea = mapInstance.GetEntity<IWorldEntity>(millitheaGuid);
            if (millithea == null)
                return;

            if (!millitheaCalled && IsPlayerNear(millithea.Position, CaptiveCallRange))
            {
                millitheaCalled = true;
                dialogue.Say(millithea, MillitheaAfraid, false);
            }

            // the guards can be added to the map a tick after her
            if (millitheaFree || millitheaGuards.Count == 0 || millitheaGuards.Any(IsAlive))
                return;

            millitheaFree = true;
            millithea.InteractionBlocked = false;
            log.LogInformation("Hycrest: Millithea's guards are down, she can be freed.");
        }

        private void UpdatePrema()
        {
            IWorldEntity prema = mapInstance.GetEntity<IWorldEntity>(premaGuid);
            if (prema == null)
                return;

            if (!premaCalled && IsPlayerNear(prema.Position, CaptiveCallRange))
            {
                premaCalled = true;
                dialogue.Say(prema, PremaStopThis, false);
            }

            if (premaFree || responsebotGuid == 0u || IsAlive(responsebotGuid))
                return;

            premaFree = true;
            prema.InteractionBlocked = false;
            dialogue.Say(prema, PremaEeep, false);
            log.LogInformation("Hycrest: the Responsebot is down, Prema can be freed.");
        }

        private void UpdateAlarm(double lastTick)
        {
            alarm.Update(lastTick);
            // armed once Tarquim has asked for help
            if (alarm.IsTriggered || !rescueStarted || ending)
                return;

            IPlayer crossing = mapInstance.GetPlayers()
                .FirstOrDefault(p => p.IsAlive && DistanceToLine(new Vector2(p.Position.X, p.Position.Z)) <= AlarmLineRange);
            if (crossing == null)
                return;

            // from the middle of the party, towards Prema
            List<IPlayer> party = mapInstance.GetPlayers()
                .Where(p => p.IsAlive && Vector3.Distance(p.Position, crossing.Position) <= PartyRange)
                .ToList();
            Vector3 centre = party.Aggregate(Vector3.Zero, (sum, p) => sum + p.Position) / party.Count;

            Vector3 direction = PremaPosition - centre;
            direction.Y = 0f;
            Vector3 position = direction.LengthSquared() > 0.01f
                ? centre + Vector3.Normalize(direction) * AlarmSpawnDistance
                : centre;

            alarm.Trigger(position, crossing);
        }

        private float DistanceToLine(Vector2 point)
        {
            float distance = float.MaxValue;
            for (int i = 0; i < alarmLine.Count - 1; i++)
            {
                Vector2 a = alarmLine[i];
                Vector2 ab = alarmLine[i + 1] - a;
                float t = ab.LengthSquared() > 0f ? Math.Clamp(Vector2.Dot(point - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
                distance = MathF.Min(distance, Vector2.Distance(point, a + ab * t));
            }

            return distance;
        }

        private class Spotlight
        {
            public double Cooldown;
            public double Firing;
            public double NextPulse;
        }

        private void UpdateSpotlights(double lastTick)
        {
            foreach ((uint guid, Spotlight state) in spotlights)
            {
                if (mapInstance.GetEntity<IWorldEntity>(guid) is not IUnitEntity spotlight)
                    continue;

                // the cooldown runs from the start of the burst, as the spell's own
                state.Cooldown -= lastTick;
                if (state.Firing > 0d)
                {
                    state.Firing    -= lastTick;
                    state.NextPulse -= lastTick;
                    if (state.NextPulse <= 0d)
                    {
                        state.NextPulse = SpotlightPulseInterval.TotalSeconds;
                        CastSpotlightSpell(spotlight, SpotlightDamageSpell);
                    }
                    continue;
                }

                if (state.Cooldown > 0d)
                    continue;

                if (!mapInstance.GetPlayers().Any(p => p.IsAlive && Vector3.Distance(p.Position, spotlight.Position) <= SpotlightRange))
                    continue;

                // the fire spell plays the machine gun, the pulses bring the telegraph and the damage
                state.Cooldown  = SpotlightCooldown.TotalSeconds;
                state.Firing    = SpotlightFireDuration.TotalSeconds;
                state.NextPulse = 0d;
                CastSpotlightSpell(spotlight, SpotlightFireSpell);
            }
        }

        private void CastSpotlightSpell(IUnitEntity spotlight, uint spell4Id)
        {
            ISpellParameters parameters = spellParametersFactory.Resolve();
            parameters.UserInitiatedSpellCast = false;
            spotlight.CastSpell(spell4Id, parameters);
        }

        private bool IsAlive(uint guid)
        {
            return mapInstance.GetEntity<IWorldEntity>(guid) is IUnitEntity unit && unit.IsAlive;
        }

        private bool IsPlayerNear(Vector3 position, float range)
        {
            return mapInstance.GetPlayers().Any(p => Vector3.Distance(p.Position, position) <= range);
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
                case SpeakWithTarquim:
                    OnTarquimSpokenTo();
                    break;
                case RescueCaptives:
                    OnCaptivesRescued();
                    break;
            }
        }

        private void OnTarquimSpokenTo()
        {
            IWorldEntity tarquim = mapInstance.GetEntity<IWorldEntity>(tarquimGuid);
            if (tarquim != null)
                dialogue.Say(tarquim, TarquimPlea, true);

            foreach (IPlayer player in mapInstance.GetPlayers())
                storyBuilder.SendStoryCommunicator(AyitaHurry, AyitaSinnatus, player);

            publicEvent.ActivateObjective(RescueCaptives);
            rescueStarted = true;
        }

        private void OnCaptivesRescued()
        {
            if (ending)
                return;

            ending = true;
            foreach (IPlayer player in mapInstance.GetPlayers())
                storyBuilder.SendStoryCommunicator(AyitaMissionPayoff, AyitaSinnatus, player);

            actionQueue.Enqueue(MissionEndDelay, CompleteMission);
        }

        /// <summary>
        /// Invoked when a player interacts with an entity, before the objective counts it.
        /// </summary>
        public override void OnEntityInteract(IPlayer player, IWorldEntity entity)
        {
            if (entity.Guid == millitheaGuid && millitheaFree && !millitheaThanked)
            {
                millitheaThanked = true;
                dialogue.Say(entity, MillitheaThanks, false);

                // one captive at a time: Prema and her guard appear now
                publicEvent.SetPhase(PremaPhase);
                publicEvent.SetObjectiveLocations(RescueCaptives, PremaLocation);
                publicEvent.SetObjectiveMapRegions(RescueCaptives, (0u, PremaLocation));
            }
            else if (entity.Guid == premaGuid && premaFree && !premaThanked)
            {
                premaThanked = true;
                dialogue.Say(entity, PremaThanks, false);
            }
        }
    }
}
