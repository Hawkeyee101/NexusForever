using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.PublicEvent;
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

        // captives call out when a player comes this close
        private const float CaptiveCallRange = 20f;

        private const float PatrolSpeed = 2f;

        // spotlight targets (Automated Machine Gun - Spotlight Target): they move slowly along a lane and the machine gun
        // fires at a player caught in them (Automated Machine Gun Fire - Hycrest Adventure - Spotlights)
        private const float SpotlightSpeed = 1.5f;
        private const float SpotlightRange = 4f;
        private const uint SpotlightFireSpell = 46970u;
        private static readonly TimeSpan SpotlightFireInterval = TimeSpan.FromSeconds(1.5);

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
        private readonly Dictionary<uint, double> spotlightCooldowns = [];

        private bool millitheaCalled;
        private bool millitheaFree;
        private bool millitheaThanked;
        private bool premaCalled;
        private bool premaFree;
        private bool premaThanked;
        private bool ending;

        private readonly TimedActionQueue actionQueue = new();

        #region Dependency Injection

        private readonly ILogger<TheFarmersDaughterMissionScript> log;
        private readonly IStoryBuilder storyBuilder;
        private readonly IFactory<ISpellParameters> spellParametersFactory;
        private readonly HycrestDialogue dialogue;

        public TheFarmersDaughterMissionScript(
            ILogger<TheFarmersDaughterMissionScript> log,
            IGameTableManager gameTableManager,
            IStoryBuilder storyBuilder,
            IFactory<ISpellParameters> spellParametersFactory)
        {
            this.log                    = log;
            this.storyBuilder           = storyBuilder;
            this.spellParametersFactory = spellParametersFactory;
            dialogue = new HycrestDialogue(gameTableManager, actionQueue);
        }

        #endregion

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
                    spotlightCooldowns[worldEntity.Guid] = 0d;
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

            entity.MovementManager.SetMode(ModeType.Walk);
            entity.MovementManager.SetPositionPath([.. route], SplineType.Linear, SplineMode.BackAndForth, speed);
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

            if (millitheaFree || millitheaGuards.Any(IsAlive))
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

            if (premaFree || IsAlive(responsebotGuid))
                return;

            premaFree = true;
            prema.InteractionBlocked = false;
            dialogue.Say(prema, PremaEeep, false);
            log.LogInformation("Hycrest: the Responsebot is down, Prema can be freed.");
        }

        private void UpdateSpotlights(double lastTick)
        {
            foreach (uint guid in spotlightCooldowns.Keys.ToList())
            {
                spotlightCooldowns[guid] -= lastTick;
                if (spotlightCooldowns[guid] > 0d)
                    continue;

                if (mapInstance.GetEntity<IWorldEntity>(guid) is not IUnitEntity spotlight)
                    continue;

                IPlayer caught = mapInstance.GetPlayers()
                    .FirstOrDefault(p => p.IsAlive && Vector3.Distance(p.Position, spotlight.Position) <= SpotlightRange);
                if (caught == null)
                    continue;

                spotlightCooldowns[guid] = SpotlightFireInterval.TotalSeconds;

                ISpellParameters parameters = spellParametersFactory.Resolve();
                parameters.PrimaryTargetId        = caught.Guid;
                parameters.UserInitiatedSpellCast = false;
                spotlight.CastSpell(SpotlightFireSpell, parameters);
            }
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
            }
            else if (entity.Guid == premaGuid && premaFree && !premaThanked)
            {
                premaThanked = true;
                dialogue.Say(entity, PremaThanks, false);
            }
        }
    }
}
