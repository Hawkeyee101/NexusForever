using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Abstract.Entity.Movement.Force;
using NexusForever.Game.Abstract.Entity.Trigger;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Entity.Movement.Command.Mode;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.PublicEvent;
using NexusForever.Network.World.Message.Model.Shared;
using NexusForever.Script.Template.Filter;
using NexusForever.Shared;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// Jailbreak (public event 425, tier 2 Militant): free the farmers held in the Detention Center, get them past the
    /// guards, rocket up to the Chua Warblimp and overload its four Balance Stabilizers, then hide in the Arcwulff Farmhouse.
    /// </summary>
    /// <remarks>
    /// Research and sources: docs research/jailbreak.md (retail videos D and T, tables, Jabbithole). From the tables: the
    /// control panel 48141 at 13108 (208, its target group counts it), 1736 "Get the Farmers past the guards" (count 3, a
    /// script count), 1737 (the blimp, a script count), 1758 (the stabilizers' target group counts them, 11 s fail timer),
    /// 2170 (a trigger in the Arcwulff Farmhouse); the blimp's arrival spline 4586 ends exactly at its point 49634. From the
    /// videos: the setup and hint communicators, Vesna's city hint, the arrival texts, one 11 s countdown for all four
    /// stabilizers started at the Navigation Array, a failure knocks the players off and the array has to be started
    /// again; the hide line and the ride to the farmhouse. Inferred: the pen behind the fence gap at z -1774.8 (the escape
    /// spline 14748 starts inside it and runs through the gap, and the Jabbithole farmer sightings lie on it); three
    /// farmers who wait while guards are near them; the Rocketeers dropping in along splines 15767-15769 when the blimp
    /// arrives; the blimp's deck on top of its balloon (its model, 27-29 m above its origin) and its facing; the jetpack
    /// flight (a script stand-in for Rocketeering 46658, whose forced move the engine can't do); the knock-off.
    /// </remarks>
    [ScriptFilterOwnerId(425u)]
    public class JailbreakMissionScript : HycrestMissionScript, IJailbreakMissionScript
    {
        private const uint FreeFarmers         = 208u;
        private const uint GetFarmersPast      = 1736u;
        private const uint DestroyBlimp        = 1737u;
        private const uint OverloadStabilizers = 1758u;
        private const uint HideInFarmhouse     = 2170u;

        private const uint Barricade        = 48140u;
        private const uint ChuaWarblimp     = 18214u; // the platform version, players stand on it
        private const uint NavigationArray  = 18404u;
        private const uint BalanceStabilizer = 18235u;
        private const uint DominionRocketeer = 48255u;
        private const uint LysionSinnatus   = 17777u;
        private const uint VesnaTaranoft    = 17778u;
        private static readonly HashSet<uint> Farmers = [18236u, 18237u, 18238u, 18239u, 18240u];

        private const uint JetpackPhase = 1u;

        // map markers: the panel, the Detention Center (the farmers are marked themselves while they walk), the blimp and
        // the farmhouse
        private const uint PanelLocation     = 13108u;
        private const uint DetentionLocation = 49629u;
        private const uint BlimpLocation     = 49634u;
        private const uint FarmhouseLocation = 40632u;
        private const double TrackedUnitInterval = 1d;

        private const uint LysionSetup        = 444111u; // communicator: "I was holding a secret gathering with some of the other farmers..."
        private const uint LysionHint         = 454978u; // communicator: "The others were all taken captive. If you want to get more of my people on your side..."
        private const uint VesnaCity          = 466528u; // communicator: "If you are infiltrating the city, stick near groups of citizens..."
        private const uint LysionBigGuns      = 456354u; // communicator: "This ain't good. Looks like the military's called in the big guns!..."
        private const uint BlimpArrived       = 463963u; // centre screen: "A Chua warblimp has arrived!"
        private const uint StabilizerWarning  = 455990u; // centre screen: "Disable the Balance Stabilizers before they restart the Navigation Array!"
        private const uint LysionBlimpFalling = 456355u; // communicator: "That blimp's losing altitude. Get off that thing before it blows!"
        private const uint LysionPayoff       = 444067u; // communicator: "We've got ourselves an army now..." (run T, probable)
        private const uint LysionHide         = 465454u; // communicator: "Better get out of there and find a spot to hide for a while..."
        private static readonly uint[] GuardBarks = [456056u, 456057u, 456058u, 456059u, 456060u]; // "Looks like we've got ourselves a jailbreak." ...

        // run D: the setup with the vote, the hint 11 s later; Vesna's hint when the recorder walked into the city
        private static readonly TimeSpan SetupDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan HintDelay  = TimeSpan.FromSeconds(12);
        private static readonly Vector3 PanelPosition = new(-2671.03f, -881.0f, -1769.21f);
        private const float CityHintRange = 90f;

        // --- the farmers' escape (inferred): along table spline 14748 (Detention Center -> city), one after the other a
        // lane apart, a leg at a time; a farmer waits (cowers) while a living guard is near the leg ahead. Each one that
        // reaches the end counts for 1736, then goes on along 4385 and leaves
        private const ushort EscapeSpline = 14748;
        private const ushort OnwardSpline = 4385;
        private const float EscapeSpeed = 6f;
        private const float LegLength = 12f;
        private const float GuardBlockRange = 15f;
        private const double MaxWait = 120d;          // a guard nobody can reach doesn't stop them for good
        private static readonly double[] EscapeStarts = [1.5d, 2.7d, 3.9d];
        private static readonly float[] EscapeLanes = [0f, -1.2f, 1.2f];
        private const uint CowerEmote = 239u;
        private const double CowerResendInterval = 10d;

        // --- the Chua Warblimp: flies in along 4586 to 49634 and stays there. Its deck is the top of the balloon (model
        // PRP_Balloon_Chua_WarBlimp_000: the top surface lies 27.2-28.9 m above the model's origin; the drop splines
        // 15767-15769 start at that height, -825). Facing: the four Jabbithole stabilizer sightings form a square along the
        // world axes around the point, so the blimp faces a multiple of 90 degrees (inferred: 0)
        private const ushort ArrivalSpline = 4586;
        private const float BlimpSpeed = 25f;
        private static readonly Vector3 BlimpPoint = new(-2639.12f, -852.28f, -1744.27f);
        private const float BlimpYaw = 0f;
        private static readonly Vector3 NavigationArrayLocal = new(0f, 28.74f, 0f);
        private static readonly Vector3[] StabilizerLocals =
        [
            new(-6.5f, 28.23f, 6f),
            new(6.5f, 27.23f, -6f),
            new(-6f, 27.18f, -6f),
            new(6f, 28.29f, 6f)
        ];
        private static readonly Vector3 LandingLocal = new(0f, 28.44f, -3f);
        private static readonly TimeSpan BlimpArrivalLead = TimeSpan.FromSeconds(3);

        // Rocketeers drop in along these splines (from deck height to the jetpack spots) when the blimp arrives
        private static readonly ushort[] DropSplines = [15767, 15768, 15769];
        private const float DropSpeed = 7f;
        private static readonly TimeSpan EngageDelay = TimeSpan.FromSeconds(0.5);

        // the jetpack flight (stand-in for Rocketeering 46658, 3.5 s): a forced move arc up onto the deck; a player who
        // isn't aboard after it (the client didn't take the keys) is put there
        private static readonly TimeSpan LaunchFlight = TimeSpan.FromSeconds(3.5);
        private const float LaunchGravity = 10f;
        private const double LaunchCheck = 4.5d;

        // the knock-off (failure, and the blimp going down): an arc out and down to the ground beside the blimp
        private static readonly TimeSpan KnockFlight = TimeSpan.FromSeconds(3);
        private const float KnockGravity = 2f;
        private const float KnockDistance = 24f;

        // Rocket Fall (the intro's slow-burn jetpack): for players who jump or fall off the deck
        private const uint RocketFallSpell = 47734u;
        private const double RocketFallRecast = 2.9d;
        private const float RocketFallLanded = 3f;

        private static readonly TimeSpan OverloadRemoveDelay = TimeSpan.FromSeconds(0.5);
        private static readonly TimeSpan FailResetDelay = TimeSpan.FromSeconds(1.5);
        private static readonly TimeSpan BlimpEvacuateTime = TimeSpan.FromSeconds(8);
        private const float BlimpSinkDepth = 60f;
        private const float BlimpSinkSpeed = 5f;
        private static readonly TimeSpan PayoffDelay = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan HideDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan MissionEndDelay = TimeSpan.FromSeconds(1);

        // the Arcwulff Farmhouse (2170, trigger volume 5724): a small sphere at its table point inside the house
        private static readonly Vector3 FarmhousePosition = new(-2506.74f, -918.54f, -1683.46f);
        private const uint TriggerId = 114910u;
        private const float TriggerRange = 4f;

        // every 5th Dominion unit killed sets off the alarm (as in The Farmer's Daughter; run T had a flare in the yard)
        private const int AlarmKills = 5;
        private const float AlarmSpawnDistance = 15f;

        private const uint HostileFaction = 1452u;

        private class Escapee
        {
            public IUnitEntity Entity;
            public List<Vector3> Route;
            public int Next;
            public double StartDelay;
            public double Moving;
            public double Waited;
            public bool Cowering;
            public double CowerResend;
            public bool Arrived;
        }

        private class Faller
        {
            public double SinceCast;
        }

        // tracked by reference: an entity's guid is only assigned once it is actually on the map
        private IWorldEntity barricade;
        private readonly List<IUnitEntity> farmers = [];
        private readonly List<Escapee> escapees = [];
        private readonly Dictionary<uint, uint> tracked = [];   // guid -> objective, the units marked on the map
        private double trackedTimer;

        private IWorldEntity blimp;
        private bool blimpParked;
        private bool blimpDown;
        private IWorldEntity navigationArray;
        private readonly Dictionary<int, IWorldEntity> stabilizers = [];
        private readonly Dictionary<uint, double> launches = [];   // player guid -> seconds until the landing check
        private readonly HashSet<uint> aboard = [];
        private readonly Dictionary<uint, Faller> fallers = [];

        private IVolumeGridTriggerEntity trigger;
        private bool cityHintSent;
        private int kills;
        private bool ending;

        private readonly TimedActionQueue actionQueue = new();
        private HycrestAlarm alarm;

        #region Dependency Injection

        private readonly ILogger<JailbreakMissionScript> log;
        private readonly IGameTableManager gameTableManager;
        private readonly IStoryBuilder storyBuilder;
        private readonly IFactory<ISpellParameters> spellParametersFactory;
        private readonly ICreatureInfoManager creatureInfoManager;
        private readonly IForcedMovementGenerator forcedMovementGenerator;
        private readonly HycrestDialogue dialogue;

        public JailbreakMissionScript(
            ILogger<JailbreakMissionScript> log,
            IGameTableManager gameTableManager,
            IStoryBuilder storyBuilder,
            IFactory<ISpellParameters> spellParametersFactory,
            ICreatureInfoManager creatureInfoManager,
            IForcedMovementGenerator forcedMovementGenerator)
        {
            this.log                     = log;
            this.gameTableManager        = gameTableManager;
            this.storyBuilder            = storyBuilder;
            this.spellParametersFactory  = spellParametersFactory;
            this.creatureInfoManager     = creatureInfoManager;
            this.forcedMovementGenerator = forcedMovementGenerator;
            dialogue = new HycrestDialogue(gameTableManager, actionQueue);
        }

        #endregion

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public override void OnLoad(IPublicEvent owner)
        {
            base.OnLoad(owner);

            publicEvent.SetObjectiveLocations(FreeFarmers, PanelLocation);
            publicEvent.SetObjectiveLocations(GetFarmersPast, DetentionLocation);
            publicEvent.SetObjectiveLocations(DestroyBlimp, BlimpLocation);
            publicEvent.SetObjectiveLocations(OverloadStabilizers, BlimpLocation);
            publicEvent.SetObjectiveLocations(HideInFarmhouse, FarmhouseLocation);
            alarm = new HycrestAlarm(log, publicEvent, mapInstance, creatureInfoManager, spellParametersFactory,
                storyBuilder, dialogue);

            actionQueue.Enqueue(SetupDelay, () => Communicator(LysionSetup, LysionSinnatus));
            actionQueue.Enqueue(HintDelay, () => Communicator(LysionHint, LysionSinnatus));
            log.LogInformation("Hycrest: Jailbreak started.");
        }

        /// <summary>
        /// Invoked when a <see cref="IGridEntity"/> is added to the map the public event is on.
        /// </summary>
        public override void OnAddToMap(IGridEntity entity)
        {
            if (entity is not IWorldEntity worldEntity || !IsOwnEntity(entity))
                return;

            if (worldEntity is IUnitEntity { InCombat: false } && (uint)worldEntity.Faction1 == HostileFaction)
                worldEntity.Sheathed = true;

            if (worldEntity.CreatureId == Barricade)
                barricade = worldEntity;
            else if (Farmers.Contains(worldEntity.CreatureId) && worldEntity is IUnitEntity farmer && !farmers.Contains(farmer))
            {
                // the escort can't be lost to a stray hit
                farmer.IsInvulnerable = true;
                farmers.Add(farmer);
            }
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public override void Update(double lastTick)
        {
            actionQueue.Update(lastTick);

            if (publicEvent.HasFinished)
                return;

            UpdateCityHint();
            UpdateEscapees(lastTick);
            UpdateTrackedUnits(lastTick);
            UpdateLaunches(lastTick);
            UpdateAboard(lastTick);
            alarm.Update(lastTick);
        }

        private void UpdateCityHint()
        {
            if (cityHintSent || !mapInstance.GetPlayers().Any(p => Vector3.Distance(p.Position, PanelPosition) <= CityHintRange))
                return;

            cityHintSent = true;
            Communicator(VesnaCity, VesnaTaranoft);
        }

        // --- the Detention Center ---------------------------------------------------------------------------------------

        /// <summary>
        /// The control panel was used (208): the laser barricade goes and the farmers make for the city.
        /// </summary>
        private void OpenPen()
        {
            if (barricade?.InWorld == true)
                barricade.RemoveFromMap();
            barricade = null;

            BarkNearestGuard(PanelPosition, 40f);
            publicEvent.ActivateObjective(GetFarmersPast);

            List<Vector3> route = GetRoute(EscapeSpline);
            if (route.Count < 2)
            {
                log.LogError($"Hycrest: Jailbreak, escape spline {EscapeSpline} not found, the farmers can't leave.");
                return;
            }

            // the three nearest the pen's gate, in that order
            List<IUnitEntity> leaving = farmers
                .Where(f => f.InWorld)
                .OrderBy(f => Vector3.Distance(f.Position, route[0]))
                .Take(EscapeStarts.Length)
                .ToList();
            for (int i = 0; i < leaving.Count; i++)
            {
                escapees.Add(new Escapee
                {
                    Entity     = leaving[i],
                    Route      = Offset(route, EscapeLanes[i]),
                    StartDelay = EscapeStarts[i]
                });
                leaving[i].Sheathed = true;
            }

            log.LogInformation($"Hycrest: Jailbreak, the pen is open, {leaving.Count} farmer(s) on their way.");
        }

        private void UpdateEscapees(double lastTick)
        {
            foreach (Escapee escapee in escapees.Where(e => !e.Arrived))
            {
                IUnitEntity farmer = escapee.Entity;
                if (!farmer.InWorld)
                {
                    // gone without arriving: count it so the mission can't get stuck
                    escapee.Arrived = true;
                    publicEvent.UpdateObjective(GetFarmersPast, 1);
                    continue;
                }

                if (escapee.StartDelay > 0d)
                {
                    escapee.StartDelay -= lastTick;
                    continue;
                }

                if (escapee.Moving > 0d)
                {
                    escapee.Moving -= lastTick;
                    continue;
                }

                if (escapee.Next >= escapee.Route.Count)
                {
                    OnFarmerPast(escapee);
                    continue;
                }

                List<Vector3> leg = NextLeg(escapee);
                IUnitEntity guard = BlockingGuard(farmer.Position, leg);
                if (guard != null && escapee.Waited < MaxWait)
                {
                    escapee.Waited += lastTick;
                    escapee.CowerResend -= lastTick;
                    if (!escapee.Cowering || escapee.CowerResend <= 0d)
                    {
                        if (!escapee.Cowering)
                            BarkNearestGuard(farmer.Position, GuardBlockRange);
                        escapee.Cowering    = true;
                        escapee.CowerResend = CowerResendInterval;
                        SendStandState(farmer, StandState.Emote, CowerEmote);
                    }
                    continue;
                }

                if (escapee.Waited >= MaxWait)
                    log.LogInformation($"Hycrest: Jailbreak, a farmer waited {MaxWait} s for a guard, going on anyway.");

                if (escapee.Cowering)
                {
                    escapee.Cowering = false;
                    SendStandState(farmer, StandState.Stand, 0u);
                }
                escapee.Waited = 0d;

                float length = Vector3.Distance(farmer.Position, leg[0]);
                for (int i = 1; i < leg.Count; i++)
                    length += Vector3.Distance(leg[i - 1], leg[i]);

                List<Vector3> nodes = [farmer.Position, .. leg];
                farmer.MovementManager.SetMode(ModeType.Walk);
                farmer.MovementManager.LaunchSpline(nodes, SplineType.Linear, SplineMode.OneShot, EscapeSpeed);
                escapee.Next  += leg.Count;
                escapee.Moving = length / EscapeSpeed + 0.1d;
            }
        }

        /// <summary>
        /// The next nodes of the farmer's route, about <see cref="LegLength"/> metres.
        /// </summary>
        private static List<Vector3> NextLeg(Escapee escapee)
        {
            List<Vector3> leg = [];
            float length = 0f;
            Vector3 from = escapee.Entity.Position;
            for (int i = escapee.Next; i < escapee.Route.Count; i++)
            {
                length += Vector3.Distance(from, escapee.Route[i]);
                leg.Add(escapee.Route[i]);
                from = escapee.Route[i];
                if (length >= LegLength)
                    break;
            }

            return leg;
        }

        /// <summary>
        /// A living Dominion unit of the mission near <paramref name="position"/> or the leg ahead, if any.
        /// </summary>
        private IUnitEntity BlockingGuard(Vector3 position, List<Vector3> leg)
        {
            return publicEvent.GetEntities()
                .OfType<IUnitEntity>()
                .Where(u => u.InWorld && u.IsAlive && (uint)u.Faction1 == HostileFaction)
                .FirstOrDefault(u => Vector3.Distance(u.Position, position) <= GuardBlockRange
                    || leg.Any(n => Vector3.Distance(u.Position, n) <= GuardBlockRange));
        }

        /// <summary>
        /// A farmer reached the end of the escape route: counts for 1736, then goes on into the city and leaves.
        /// </summary>
        private void OnFarmerPast(Escapee escapee)
        {
            escapee.Arrived = true;
            publicEvent.UpdateObjective(GetFarmersPast, 1);
            log.LogInformation($"Hycrest: Jailbreak, a farmer made it past the guards ({escapees.Count(e => e.Arrived)}/{escapees.Count}).");

            IUnitEntity farmer = escapee.Entity;
            List<Vector3> onward = GetRoute(OnwardSpline);
            if (onward.Count < 2)
            {
                farmer.RemoveFromMap();
                return;
            }

            float length = Vector3.Distance(farmer.Position, onward[0]);
            for (int i = 1; i < onward.Count; i++)
                length += Vector3.Distance(onward[i - 1], onward[i]);

            farmer.MovementManager.SetMode(ModeType.Walk);
            farmer.MovementManager.LaunchSpline([farmer.Position, .. onward], SplineType.Linear, SplineMode.OneShot, EscapeSpeed);
            actionQueue.Enqueue(TimeSpan.FromSeconds(length / EscapeSpeed + 0.5d), () =>
            {
                if (farmer.InWorld)
                    farmer.RemoveFromMap();
            });
        }

        private void BarkNearestGuard(Vector3 position, float range)
        {
            IUnitEntity guard = publicEvent.GetEntities()
                .OfType<IUnitEntity>()
                .Where(u => u.InWorld && u.IsAlive && (uint)u.Faction1 == HostileFaction
                    && !HycrestAlarm.IsAlarmUnit(u.CreatureId)
                    && Vector3.Distance(u.Position, position) <= range)
                .OrderBy(u => Vector3.Distance(u.Position, position))
                .FirstOrDefault();
            if (guard != null)
                dialogue.Bark(guard, GuardBarks[Random.Shared.Next(GuardBarks.Length)]);
        }

        // --- map markers on the farmers ---------------------------------------------------------------------------------

        private void UpdateTrackedUnits(double lastTick)
        {
            trackedTimer -= lastTick;
            if (trackedTimer > 0d)
                return;
            trackedTimer = TrackedUnitInterval;

            List<IUnitEntity> marked = escapees
                .Where(e => !e.Arrived && e.Entity.InWorld)
                .Select(e => e.Entity)
                .ToList();

            foreach (uint guid in tracked.Keys.Except(marked.Select(m => m.Guid)).ToList())
            {
                tracked.Remove(guid);
                foreach (IPlayer player in mapInstance.GetPlayers())
                    player.Session.EnqueueMessageEncrypted(new ServerMapTrackedUnitDisable { TrackedUnitId = guid });
            }

            foreach (IUnitEntity unit in marked)
            {
                tracked[unit.Guid] = GetFarmersPast;
                foreach (IPlayer player in mapInstance.GetPlayers())
                    player.Session.EnqueueMessageEncrypted(new ServerMapTrackedUnitUpdate
                    {
                        TrackedUnitId          = unit.Guid,
                        Position               = unit.Position,
                        PublicEventObjectiveId = GetFarmersPast
                    });
            }
        }

        private void ClearTrackedUnits()
        {
            foreach (uint guid in tracked.Keys)
                foreach (IPlayer player in mapInstance.GetPlayers())
                    player.Session.EnqueueMessageEncrypted(new ServerMapTrackedUnitDisable { TrackedUnitId = guid });
            tracked.Clear();
        }

        // --- the Chua Warblimp ------------------------------------------------------------------------------------------

        /// <summary>
        /// The farmers are out (1736): the military calls in the blimp, it flies in along its arrival spline.
        /// </summary>
        private void StartBlimp()
        {
            ClearTrackedUnits();
            Communicator(LysionBigGuns, LysionSinnatus);

            actionQueue.Enqueue(BlimpArrivalLead, () =>
            {
                if (publicEvent.HasFinished)
                    return;

                List<Vector3> route = GetRoute(ArrivalSpline);
                if (route.Count < 2)
                    route = [BlimpPoint + new Vector3(0f, 150f, 300f), BlimpPoint];
                route[^1] = BlimpPoint;

                blimp = SpawnPlatform(ChuaWarblimp, route[0], BlimpYaw);
                if (blimp == null)
                {
                    log.LogError("Hycrest: Jailbreak, the blimp could not be spawned.");
                    return;
                }

                float length = 0f;
                for (int i = 1; i < route.Count; i++)
                    length += Vector3.Distance(route[i - 1], route[i]);

                blimp.MovementManager.LaunchSpline(route, SplineType.Linear, SplineMode.OneShot, BlimpSpeed);
                actionQueue.Enqueue(TimeSpan.FromSeconds(length / BlimpSpeed + 0.5d), OnBlimpArrived);
                log.LogInformation($"Hycrest: Jailbreak, the blimp flies in ({length:0} m).");
            });
        }

        /// <summary>
        /// The blimp hovers over the Detention Center: its deck gets the Navigation Array and the stabilizers, the jetpacks
        /// appear and the Rocketeers drop in.
        /// </summary>
        private void OnBlimpArrived()
        {
            if (publicEvent.HasFinished || blimp is not { InWorld: true })
                return;

            // the spline left it facing its last leg; the deck layout is for BlimpYaw
            blimp.MovementManager.SetRotation(new Vector3(BlimpYaw, 0f, 0f), true);
            blimpParked = true;

            Broadcast(BlimpArrived);
            publicEvent.ActivateObjective(DestroyBlimp);
            publicEvent.SetPhase(JetpackPhase);

            navigationArray = SpawnUnit(NavigationArray, ToWorld(NavigationArrayLocal), BlimpYaw);
            for (int i = 0; i < StabilizerLocals.Length; i++)
                SpawnStabilizer(i);

            foreach (ushort splineId in DropSplines)
                DropRocketeer(splineId);

            log.LogInformation("Hycrest: Jailbreak, the blimp has arrived.");
        }

        private void SpawnStabilizer(int index)
        {
            if (stabilizers.TryGetValue(index, out IWorldEntity existing) && existing.InWorld)
                return;

            Vector3 local = StabilizerLocals[index];
            float yaw = MathF.Atan2(-local.X, -local.Z) + MathF.PI; // facing out from the array
            IWorldEntity stabilizer = SpawnUnit(BalanceStabilizer, ToWorld(local), yaw);
            if (stabilizer != null)
                stabilizers[index] = stabilizer;
        }

        /// <summary>
        /// A Rocketeer comes down along <paramref name="splineId"/> and joins the fight where it lands.
        /// </summary>
        /// <remarks>
        /// Added at the landing spot (its leash, so it doesn't fly back up after a fight), then launched from the top.
        /// </remarks>
        private void DropRocketeer(ushort splineId)
        {
            List<Vector3> route = GetRoute(splineId);
            if (route.Count < 2)
                return;

            IWorldEntity rocketeer = SpawnUnit(DominionRocketeer, route[^1], 0f);
            if (rocketeer is not IUnitEntity unit)
                return;

            float length = 0f;
            for (int i = 1; i < route.Count; i++)
                length += Vector3.Distance(route[i - 1], route[i]);

            unit.MovementManager.LaunchSpline(route, SplineType.Linear, SplineMode.OneShot, DropSpeed);
            actionQueue.Enqueue(TimeSpan.FromSeconds(length / DropSpeed) + EngageDelay, () =>
            {
                if (!unit.InWorld || !unit.IsAlive || unit.InCombat)
                    return;

                IPlayer target = NearestPlayer(unit.Position);
                if (target != null && Vector3.Distance(target.Position, unit.Position) <= 60f)
                    unit.ThreatManager.UpdateThreat(target, 1);
            });
        }

        /// <summary>
        /// <paramref name="player"/> used a Jetpack: an arc up onto the blimp's deck.
        /// </summary>
        public void OnJetpackUsed(IWorldEntity jetpack, IPlayer player)
        {
            if (ending || publicEvent.HasFinished || !blimpParked || blimpDown)
                return;

            Vector3 landing = ToWorld(LandingLocal + new Vector3((float)(Random.Shared.NextDouble() * 3d - 1.5d), 0f, 0f));
            Vector3 direction = landing - player.Position;
            float yaw = MathF.Atan2(-direction.X, -direction.Z);

            fallers.Remove(player.Guid);
            forcedMovementGenerator.ForceMove(player, landing, new Vector3(yaw, 0f, 0f), LaunchFlight, LaunchGravity, 0f);
            launches[player.Guid] = LaunchCheck;
            log.LogInformation($"Hycrest: Jailbreak, {player.Name} rockets up to the blimp.");
        }

        private void UpdateLaunches(double lastTick)
        {
            foreach (uint guid in launches.Keys.ToList())
            {
                launches[guid] -= lastTick;
                if (launches[guid] > 0d)
                    continue;

                launches.Remove(guid);
                if (mapInstance.GetEntity<IPlayer>(guid) is not { } player || !blimpParked || blimpDown)
                    continue;

                if (IsAboard(player.Position))
                {
                    aboard.Add(guid);
                    continue;
                }

                // the arc didn't take: put them on the deck
                log.LogInformation($"Hycrest: Jailbreak, {player.Name} didn't land on the blimp ({player.Position}), moved onto the deck.");
                aboard.Add(guid);
                player.TeleportToLocal(ToWorld(LandingLocal), false);
            }
        }

        /// <summary>
        /// Track who is on the deck; whoever leaves it in the air gets Rocket Fall until they land.
        /// </summary>
        private void UpdateAboard(double lastTick)
        {
            if (!blimpParked)
                return;

            foreach (IPlayer player in mapInstance.GetPlayers())
            {
                if (launches.ContainsKey(player.Guid))
                    continue;

                if (IsAboard(player.Position))
                {
                    aboard.Add(player.Guid);
                    fallers.Remove(player.Guid);
                    continue;
                }

                if (aboard.Remove(player.Guid) && player.IsAlive)
                {
                    fallers[player.Guid] = new Faller();
                    CastRocketFall(player);
                }
            }

            foreach ((uint guid, Faller faller) in fallers.ToList())
            {
                if (mapInstance.GetEntity<IPlayer>(guid) is not { IsAlive: true } player)
                {
                    fallers.Remove(guid);
                    continue;
                }

                float? ground = mapInstance.GetTerrainHeight(player.Position.X, player.Position.Z);
                if (!ground.HasValue || player.Position.Y - ground.Value <= RocketFallLanded)
                {
                    fallers.Remove(guid);
                    continue;
                }

                faller.SinceCast += lastTick;
                if (faller.SinceCast >= RocketFallRecast)
                {
                    faller.SinceCast = 0d;
                    CastRocketFall(player);
                }
            }
        }

        private void CastRocketFall(IPlayer player)
        {
            // cast by the script, not the player: skips the cooldown checks (as the intro's drop ship)
            ISpellParameters parameters = spellParametersFactory.Resolve();
            parameters.PrimaryTargetId        = player.Guid;
            parameters.UserInitiatedSpellCast = false;
            player.CastSpell(RocketFallSpell, parameters);
        }

        /// <summary>
        /// <paramref name="player"/> started the Navigation Array: one 11 s countdown for all four stabilizers (1758).
        /// </summary>
        public void OnNavigationArrayActivated(IWorldEntity array, IPlayer player)
        {
            if (ending || publicEvent.HasFinished || blimpDown || !IsObjective(OverloadStabilizers, PublicEventStatus.Inactive))
                return;

            for (int i = 0; i < StabilizerLocals.Length; i++)
                SpawnStabilizer(i);

            publicEvent.ActivateObjective(OverloadStabilizers);
            Broadcast(StabilizerWarning);
            log.LogInformation($"Hycrest: Jailbreak, {player.Name} started the Navigation Array, 11 s for the stabilizers.");
        }

        /// <summary>
        /// <paramref name="player"/> overloaded a stabilizer; 1758 counted it already (target group), it goes.
        /// </summary>
        public void OnStabilizerOverloaded(IWorldEntity stabilizer, IPlayer player)
        {
            if (ending || publicEvent.HasFinished)
                return;

            if (!IsObjective(OverloadStabilizers, PublicEventStatus.Active) && !IsObjective(OverloadStabilizers, PublicEventStatus.Succeeded))
            {
                log.LogInformation($"Hycrest: Jailbreak, {player.Name} overloaded a stabilizer before the Navigation Array was started, nothing.");
                return;
            }

            int index = stabilizers.FirstOrDefault(s => s.Value == stabilizer, new KeyValuePair<int, IWorldEntity>(-1, null)).Key;
            if (index >= 0)
                stabilizers.Remove(index);

            stabilizer.InteractionBlocked = true;
            actionQueue.Enqueue(OverloadRemoveDelay, () =>
            {
                if (stabilizer.InWorld)
                    stabilizer.RemoveFromMap();
            });
            log.LogInformation($"Hycrest: Jailbreak, {player.Name} overloaded stabilizer {index}.");
        }

        /// <summary>
        /// The countdown ran out (1758 failed): the array throws everyone off the deck; the stabilizers come back and the
        /// array can be started again.
        /// </summary>
        private void OnOverloadFailed()
        {
            log.LogInformation("Hycrest: Jailbreak, the stabilizers weren't overloaded in time.");
            foreach (IPlayer player in mapInstance.GetPlayers().Where(p => p.IsAlive && IsAboard(p.Position)))
                KnockOff(player);

            actionQueue.Enqueue(FailResetDelay, () =>
            {
                if (publicEvent.HasFinished || blimpDown)
                    return;

                publicEvent.ResetObjective(OverloadStabilizers);
                for (int i = 0; i < StabilizerLocals.Length; i++)
                    SpawnStabilizer(i);
            });
        }

        /// <summary>
        /// An arc out and down to the ground beside the blimp.
        /// </summary>
        private void KnockOff(IPlayer player)
        {
            Vector3 away = player.Position - BlimpPoint;
            away.Y = 0f;
            away = away.LengthSquared() > 0.01f ? Vector3.Normalize(away) : new Vector3(0f, 0f, -1f);

            Vector3 target = BlimpPoint + away * KnockDistance;
            float? ground = mapInstance.GetTerrainHeight(target.X, target.Z);
            target.Y = (ground ?? -881f) + 0.2f;

            aboard.Remove(player.Guid);
            fallers.Remove(player.Guid);
            launches.Remove(player.Guid);
            forcedMovementGenerator.ForceMove(player, target, player.Rotation, KnockFlight, KnockGravity, 0f);
        }

        /// <summary>
        /// All four stabilizers are overloaded: the blimp is done for (1737); it sinks and everyone gets off.
        /// </summary>
        private void OnStabilizersOverloaded()
        {
            if (blimpDown)
                return;

            blimpDown = true;
            publicEvent.UpdateObjective(DestroyBlimp, 1);
            Communicator(LysionBlimpFalling, LysionSinnatus);
            log.LogInformation("Hycrest: Jailbreak, the blimp is going down.");

            if (navigationArray?.InWorld == true)
                navigationArray.RemoveFromMap();
            foreach (IWorldEntity stabilizer in stabilizers.Values.Where(s => s.InWorld))
                stabilizer.RemoveFromMap();
            stabilizers.Clear();

            actionQueue.Enqueue(BlimpEvacuateTime, () =>
            {
                foreach (IPlayer player in mapInstance.GetPlayers().Where(p => IsAboard(p.Position)))
                    KnockOff(player);

                if (blimp is { InWorld: true })
                {
                    IWorldEntity sinking = blimp;
                    sinking.MovementManager.LaunchSpline([sinking.Position, sinking.Position - new Vector3(0f, BlimpSinkDepth, 0f)],
                        SplineType.Linear, SplineMode.OneShot, BlimpSinkSpeed);
                    actionQueue.Enqueue(TimeSpan.FromSeconds(BlimpSinkDepth / BlimpSinkSpeed), () =>
                    {
                        if (sinking.InWorld)
                            sinking.RemoveFromMap();
                    });
                }
                blimpParked = false;
            });

            actionQueue.Enqueue(BlimpEvacuateTime + PayoffDelay, () => Communicator(LysionPayoff, LysionSinnatus));
            actionQueue.Enqueue(BlimpEvacuateTime + HideDelay, StartHide);
        }

        // --- the hideout ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Hide in the Arcwulff Farmhouse (2170): the whole party, a trigger inside the house.
        /// </summary>
        private void StartHide()
        {
            if (publicEvent.HasFinished)
                return;

            Communicator(LysionHide, LysionSinnatus);

            // the rebels wait at the Arcwulff farm
            mapInstance.PublicEventManager.GetEvent(HycrestPublicEvent.Main)?
                .InvokeScriptCollection<IHycrestMainEventScript>(s => s.PrepareHideout(publicEvent.Id));

            PublicEventObjectiveEntry entry = gameTableManager.PublicEventObjective.GetEntry(HideInFarmhouse);
            publicEvent.ActivateObjective(HideInFarmhouse, (uint)Math.Max(1, mapInstance.PlayerCount));

            trigger = publicEvent.CreateEntity<IVolumeGridTriggerEntity>();
            trigger.Initialise(TriggerId, TriggerRange, entry?.ObjectId ?? 0u);
            trigger.AddToMap(mapInstance, FarmhousePosition);
            log.LogInformation("Hycrest: Jailbreak, hide in the Arcwulff Farmhouse.");
        }

        // --- kills, objectives and the end ------------------------------------------------------------------------------

        /// <summary>
        /// Invoked when a unit on the map has been killed: every 5th of the mission's Dominion units sets off the alarm.
        /// </summary>
        public override void OnEntityKilled(IUnitEntity unit)
        {
            if (ending || publicEvent.HasFinished || unit is IPlayer || !IsOwnEntity(unit))
                return;

            if (HycrestAlarm.IsAlarmUnit(unit.CreatureId) || (uint)unit.Faction1 != HostileFaction)
                return;

            kills++;
            if (kills % AlarmKills != 0 || alarm.IsActive)
                return;

            IPlayer target = NearestPlayer(unit.Position);
            if (target == null)
                return;

            Vector3 away = unit.Position - target.Position;
            away.Y = 0f;
            Vector3 position = away.LengthSquared() > 0.01f
                ? unit.Position + Vector3.Normalize(away) * AlarmSpawnDistance
                : unit.Position + new Vector3(AlarmSpawnDistance, 0f, 0f);

            log.LogInformation($"Hycrest: Jailbreak, {kills} kills, alarm.");
            alarm.Trigger(position, target);
        }

        /// <summary>
        /// Invoked when the status of an objective changes.
        /// </summary>
        public override void OnPublicEventObjectiveStatus(IPublicEventObjective objective)
        {
            if (objective.Entry.Id == OverloadStabilizers && objective.Status == PublicEventStatus.Failed)
            {
                OnOverloadFailed();
                return;
            }

            if (objective.Status != PublicEventStatus.Succeeded)
                return;

            switch (objective.Entry.Id)
            {
                case FreeFarmers:
                    OpenPen();
                    break;
                case GetFarmersPast:
                    StartBlimp();
                    break;
                case OverloadStabilizers:
                    OnStabilizersOverloaded();
                    break;
                case HideInFarmhouse:
                    if (ending)
                        break;
                    ending = true;
                    if (trigger?.InWorld == true)
                        trigger.RemoveFromMap();
                    actionQueue.Enqueue(MissionEndDelay, CompleteMission);
                    log.LogInformation("Hycrest: Jailbreak, everyone is hiding in the farmhouse.");
                    break;
            }
        }

        // --- helpers ----------------------------------------------------------------------------------------------------

        private bool IsObjective(uint objectiveId, PublicEventStatus status)
        {
            return publicEvent.GetTeams()
                .SelectMany(t => t.GetObjectives())
                .Any(o => o.Entry.Id == objectiveId && o.Status == status);
        }

        /// <summary>
        /// Return the world position of <paramref name="local"/> (an offset in the parked blimp's frame).
        /// </summary>
        private static Vector3 ToWorld(Vector3 local)
        {
            return HycrestShipLayout.ToWorld(local, BlimpPoint, BlimpYaw);
        }

        /// <summary>
        /// Return true if <paramref name="position"/> is on the parked blimp's deck (the top of the balloon).
        /// </summary>
        private bool IsAboard(Vector3 position)
        {
            if (!blimpParked)
                return false;

            Vector3 local = HycrestShipLayout.ToShip(position, BlimpPoint, BlimpYaw);
            return local.Y > 22f && local.Y < 36f
                && MathF.Abs(local.X) < 14f
                && MathF.Abs(local.Z) < 22f;
        }

        /// <summary>
        /// The table route's nodes, without the repeated control points at its ends.
        /// </summary>
        private List<Vector3> GetRoute(ushort splineId)
        {
            List<Vector3> route = [];
            foreach (Spline2NodeEntry node in gameTableManager.Spline2Node.Entries
                .Where(n => n.SplineId == splineId)
                .OrderBy(n => n.Ordinal))
            {
                var position = new Vector3(node.Position0, node.Position1, node.Position2);
                if (route.Count == 0 || Vector3.Distance(route[^1], position) > 0.05f)
                    route.Add(position);
            }

            return route;
        }

        /// <summary>
        /// The route shifted sideways by <paramref name="lane"/> metres (positive: right of the walking direction), at the
        /// route's own heights.
        /// </summary>
        private static List<Vector3> Offset(List<Vector3> route, float lane)
        {
            if (lane == 0f)
                return [.. route];

            var result = new List<Vector3>(route.Count);
            for (int i = 0; i < route.Count; i++)
            {
                Vector3 forward = route[Math.Min(i + 1, route.Count - 1)] - route[Math.Max(i - 1, 0)];
                forward.Y = 0f;
                forward = forward.LengthSquared() > 0.0001f ? Vector3.Normalize(forward) : Vector3.UnitZ;
                Vector3 side = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
                result.Add(route[i] + side * lane);
            }

            return result;
        }

        private IWorldEntity SpawnUnit(uint creatureId, Vector3 position, float yaw)
        {
            ICreatureInfo creatureInfo = creatureInfoManager.GetCreatureInfo(creatureId);
            if (creatureInfo == null)
            {
                log.LogWarning($"Hycrest: Jailbreak, no creature info for {creatureId}.");
                return null;
            }

            var entity = publicEvent.CreateEntity<INonPlayerEntity>();
            entity.Initialise(creatureInfo);
            entity.Rotation = new Vector3(yaw, 0f, 0f);
            entity.AddToMap(mapInstance, position);
            return entity;
        }

        private IWorldEntity SpawnPlatform(uint creatureId, Vector3 position, float yaw)
        {
            ICreatureInfo creatureInfo = creatureInfoManager.GetCreatureInfo(creatureId);
            if (creatureInfo == null)
            {
                log.LogWarning($"Hycrest: Jailbreak, no creature info for {creatureId}.");
                return null;
            }

            // a platform, like the intro's drop ship: players stand on its model
            var entity = publicEvent.CreateEntity<IPlatformEntity>();
            entity.Initialise(creatureInfo);
            entity.Rotation = new Vector3(yaw, 0f, 0f);
            entity.AddToMap(mapInstance, position);
            return entity;
        }

        private static void SendStandState(IWorldEntity entity, StandState standState, uint emoteId)
        {
            entity.EnqueueToVisible(new ServerEmote
            {
                Guid       = entity.Guid,
                StandState = standState,
                EmoteId    = emoteId
            });
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

        private void Communicator(uint textId, uint creatureId)
        {
            foreach (IPlayer player in mapInstance.GetPlayers())
                storyBuilder.SendStoryCommunicator(textId, creatureId, player);
        }

        private IPlayer NearestPlayer(Vector3 position)
        {
            return mapInstance.GetPlayers()
                .Where(p => p.IsAlive)
                .OrderBy(p => Vector3.Distance(p.Position, position))
                .FirstOrDefault();
        }

        /// <summary>
        /// Invoked when the mission has finished: the escort's markers go too.
        /// </summary>
        public override void OnMissionEnded()
        {
            ClearTrackedUnits();
            base.OnMissionEnded();
        }

        /// <summary>
        /// Invoked when the hideout's door closes after the mission: the units are gone, forget them.
        /// </summary>
        public override void OnHideoutClosed()
        {
            base.OnHideoutClosed();
            farmers.Clear();
            escapees.Clear();
            stabilizers.Clear();
            launches.Clear();
            aboard.Clear();
            fallers.Clear();
            barricade       = null;
            blimp           = null;
            navigationArray = null;
            blimpParked     = false;
        }

        /// <summary>
        /// Return true if <paramref name="entity"/> stays in the world after the mission: the Dominion's units, until the
        /// hideout closes. The blimp, its deck and the farmers go at once.
        /// </summary>
        protected override bool KeepAfterMission(IGridEntity entity)
        {
            return entity is IUnitEntity unit && (uint)unit.Faction1 == HostileFaction;
        }
    }
}
