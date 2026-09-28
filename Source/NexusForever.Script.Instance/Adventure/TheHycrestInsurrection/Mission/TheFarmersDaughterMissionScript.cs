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
using NexusForever.GameTable.Model;
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

        private const uint TarquimLocation   = 13041u;
        private const uint MillitheaLocation = 39383u;
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

        // tracking: a player within the aggro range pulls the spotlight off its lane towards them, slower than a running
        // player; it gives up (hard cap) when the player is further than the drop range from it or it would leave its
        // lane by more than the leash, then returns to the nearest point of its lane and patrols on
        // aggro reaches past the light itself (like an NPC's aggro, shorter)
        private const float SpotlightAggroRange = 15f;
        private const float SpotlightDropRange = 20f;
        private const float SpotlightLeash = 15f;
        private const float SpotlightTrackSpeed = 3.5f;
        private const float SpotlightReturnSpeed = 4f;
        private static readonly TimeSpan SpotlightRetrackInterval = TimeSpan.FromSeconds(0.5);

        // the alarm: crossing this line calls a Recon Specialist this far from the party, towards Prema. Retail's trigger
        // isn't in the tables; the specialist was measured at -2413, -1587, between Millithea and Prema. Measured in game
        // (28 Sep 2026): 165 m north-south between Millithea and Prema (splines 4648 and 4588 were too early)
        private static readonly List<Vector2> AlarmLine =
        [
            new(-2405.2197f, -1536.3839f),
            new(-2406.7625f, -1700.9954f)
        ];
        private const float AlarmLineRange = 3f;
        private const float AlarmSpawnDistance = 20f;
        private const float PartyRange = 30f;

        // dev: show the alarm line in game with ground light circles (the spotlight's light, display 23754, on the
        // harmless Visual Fluff Spotlight creature) every few metres; set to false once the trigger is settled
        private const bool ShowAlarmLine = false;
        private const uint AlarmLineMarker = 28723u;
        private const uint AlarmLineMarkerDisplay = 23754u;
        private const float AlarmLineMarkerSpacing = 4f;

        private static readonly Vector3 PremaPosition = new(-2377.9238f, -929.3451f, -1641.9752f);

        private static readonly TimeSpan TarquimPleaDelay = TimeSpan.FromSeconds(1.5);

        private static readonly TimeSpan MissionEndDelay = TimeSpan.FromSeconds(4);

        // patrolling scouts: spawn position (first node) and the route they walk back and forth
        private static readonly Vector3[][] Patrols =
        [
            [new(-2451.276f, -927.9198f, -1214.2766f), new(-2463.5671f, -927.2098f, -1194.4918f), new(-2452.616f, -925.6125f, -1181.2755f)],
            [new(-2452.7524f, -928.83093f, -1555.9928f), new(-2438.631f, -929.44073f, -1579.1681f)],
            [new(-2381.129f, -923.09406f, -1698.4329f), new(-2348.2769f, -923.348f, -1698.5614f)]
        ];

        // spotlight lanes: spawn position and the other end
        private static readonly Vector3[][] SpotlightLanes =
        [
            [new(-2521.6794f, -927.764f, -1365.4867f), new(-2469.8152f, -924.95306f, -1307.9954f)],
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

            // map area and minimap marker: the objectives have no WorldLocation2 in the tables; these are the table points
            // next to Tarquim and Millithea (layout A), Prema's is the nearest point (about 20 m)
            publicEvent.SetObjectiveLocations(SpeakWithTarquim, TarquimLocation);
            publicEvent.SetObjectiveLocations(RescueCaptives, MillitheaLocation);

            alarm = new HycrestAlarm(log, publicEvent, mapInstance, creatureInfoManager, spellParametersFactory,
                globalQuestManager, dialogue);
            alarmLine = AlarmLine;

            if (ShowAlarmLine)
                ShowLine(alarmLine);
        }

        /// <summary>
        /// Dev: mark <paramref name="line"/> on the ground; the markers are the mission's, removed when it ends.
        /// </summary>
        private void ShowLine(List<Vector2> line)
        {
            ICreatureInfo creatureInfo = creatureInfoManager.GetCreatureInfo(AlarmLineMarker);
            Creature2DisplayInfoEntry display = gameTableManager.Creature2DisplayInfo.GetEntry(AlarmLineMarkerDisplay);
            if (creatureInfo == null)
                return;

            for (int i = 0; i < line.Count - 1; i++)
            {
                float length = Vector2.Distance(line[i], line[i + 1]);
                for (float d = 0f; d < length; d += AlarmLineMarkerSpacing)
                    AddMarker(Vector2.Lerp(line[i], line[i + 1], d / length));
            }
            AddMarker(line[^1]);

            void AddMarker(Vector2 point)
            {
                float y = mapInstance.GetTerrainHeight(point.X, point.Y) ?? -929f;

                var marker = publicEvent.CreateEntity<INonPlayerEntity>();
                marker.Initialise(creatureInfo);
                if (display != null)
                    marker.CreatureDisplayEntry = display;
                marker.AddToMap(mapInstance, new Vector3(point.X, y, point.Y));
            }
        }

        /// <summary>
        /// Invoked when a <see cref="IGridEntity"/> is added to the map the public event is on.
        /// </summary>
        public override void OnAddToMap(IGridEntity entity)
        {
            if (entity is not IWorldEntity worldEntity || !IsOwnEntity(entity))
                return;

            // weapons away until they fight: a unit's Sheathed stat is only set when its combat state changes, so units
            // that haven't fought yet held their rifles drawn
            if (worldEntity is IUnitEntity { InCombat: false } && worldEntity.CreatureId != SpotlightTarget)
                worldEntity.Sheathed = true;

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
                {
                    List<Vector3> loop = GetLoop(worldEntity.Position, Patrols);
                    if (loop != null)
                        LaunchLoop(worldEntity, loop, 0, PatrolSpeed);
                    break;
                }
                case SpotlightTarget:
                {
                    var spotlight = new Spotlight
                    {
                        Loop = GetLoop(worldEntity.Position, SpotlightLanes) ?? [worldEntity.Position]
                    };
                    spotlights[worldEntity.Guid] = spotlight;
                    if (spotlight.Loop.Count > 1)
                        LaunchLoop(worldEntity, spotlight.Loop, 0, SpotlightSpeed);
                    break;
                }
            }
        }

        /// <summary>
        /// Return the route starting where the entity spawned as a closed loop, walked there and back (A, B, C, B, A).
        /// </summary>
        /// <remarks>
        /// The client jumped back to the start with BackAndForth, and a Cyclic spline has no closing segment: it wraps from
        /// the last node to the first (A, B, C, B jumped at B on the way back), so the loop ends on its start.
        /// </remarks>
        private static List<Vector3> GetLoop(Vector3 spawn, Vector3[][] routes)
        {
            Vector3[] route = routes.FirstOrDefault(r => Vector3.Distance(r[0], spawn) < 1f);
            if (route == null)
                return null;

            List<Vector3> loop = [.. route];
            for (int i = route.Length - 2; i >= 0; i--)
                loop.Add(route[i]);
            return loop;
        }

        private class Walk
        {
            public List<Vector3> Loop;
            public List<Vector3> Nodes;
            public float Speed;
            public double Duration;
            public double Remaining;
        }

        // patrols and spotlight lanes being walked, restarted by UpdateWalks when a round ends
        private readonly Dictionary<uint, Walk> walks = [];

        /// <summary>
        /// Walk <paramref name="loop"/> once from node <paramref name="start"/> back to it; <see cref="UpdateWalks"/> starts
        /// the next round.
        /// </summary>
        /// <remarks>
        /// One round per spline (OneShot) rather than a Cyclic spline: the server's Cyclic spline has no closing segment
        /// (it jumped back to the start). MovementManager.SetPositionPath silently ignores a path whose first and last
        /// nodes are the same (every patrol stood still), so a round that would end where it started ends just short of
        /// it; the next round starts from there. LaunchSpline also sets the moving state and faces the entity where it
        /// walks.
        /// </remarks>
        private void LaunchLoop(IWorldEntity entity, List<Vector3> loop, int start, float speed)
        {
            List<Vector3> open = loop.Take(loop.Count - 1).ToList();
            List<Vector3> nodes = [.. open.Skip(start), .. open.Take(start)];
            nodes.Add(nodes[0]);
            if (Vector3.Distance(entity.Position, nodes[0]) > 0.5f)
                nodes.Insert(0, entity.Position);
            else
                nodes[0] = entity.Position;

            if (Vector3.Distance(nodes[0], nodes[^1]) < RoundEndGap / 2f)
                nodes[^1] = MoveTowards(nodes[^1], nodes[^2], RoundEndGap);

            float length = 0f;
            for (int i = 1; i < nodes.Count; i++)
                length += Vector3.Distance(nodes[i - 1], nodes[i]);

            entity.MovementManager.SetMode(ModeType.Walk);
            entity.MovementManager.LaunchSpline(nodes, SplineType.Linear, SplineMode.OneShot, speed);

            walks[entity.Guid] = new Walk
            {
                Loop      = loop,
                Nodes     = nodes,
                Speed     = speed,
                Duration  = length / speed,
                Remaining = length / speed + 0.25d
            };
        }

        private void UpdateWalks(double lastTick)
        {
            foreach ((uint guid, Walk walk) in walks.ToList())
            {
                IWorldEntity entity = mapInstance.GetEntity<IWorldEntity>(guid);
                if (entity == null || entity is IUnitEntity { IsAlive: false })
                {
                    walks.Remove(guid);
                    continue;
                }

                // combat moves the unit, the patrol goes on from the nearest node afterwards
                if (entity is IUnitEntity { InCombat: true })
                {
                    walk.Remaining = 0d;
                    continue;
                }

                walk.Remaining -= lastTick;
                if (walk.Remaining > 0d)
                    continue;

                LaunchLoop(entity, walk.Loop, NearestNode(walk.Loop, entity.Position), walk.Speed);
            }
        }

        // a round ends this far short of its start point, see LaunchLoop
        private const float RoundEndGap = 0.1f;

        private static Vector3 MoveTowards(Vector3 from, Vector3 to, float distance)
        {
            Vector3 direction = to - from;
            return direction.LengthSquared() > 0f ? from + Vector3.Normalize(direction) * distance : from;
        }

        private static int NearestNode(List<Vector3> loop, Vector3 position)
        {
            int count = Math.Max(1, loop.Count - 1);
            return Enumerable.Range(0, count)
                .OrderBy(i => Vector3.Distance(loop[i], position))
                .First();
        }

        private static void MoveTo(IWorldEntity entity, Vector3 destination, float speed)
        {
            entity.MovementManager.LaunchSpline([entity.Position, destination], SplineType.Linear, SplineMode.OneShot, speed);
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public override void Update(double lastTick)
        {
            actionQueue.Update(lastTick);

            if (publicEvent.HasFinished)
                return;

            LinkAggro(millitheaGuards);
            UpdateWalks(lastTick);
            UpdateMillithea();
            UpdatePrema();
            UpdateSpotlights(lastTick);
            UpdateAlarm(lastTick);
        }

        /// <summary>
        /// A guard group fights together: when one of them is in combat, the others join in against the same players.
        /// </summary>
        private void LinkAggro(IEnumerable<uint> group)
        {
            List<IUnitEntity> guards = group
                .Select(guid => mapInstance.GetEntity<IWorldEntity>(guid))
                .OfType<IUnitEntity>()
                .Where(u => u.IsAlive)
                .ToList();

            List<IUnitEntity> hated = guards
                .Where(u => u.InCombat)
                .SelectMany(u => u.ThreatManager)
                .Select(h => mapInstance.GetEntity<IWorldEntity>(h.HatedUnitId))
                .OfType<IUnitEntity>()
                .Where(u => u.IsAlive)
                .Distinct()
                .ToList();
            if (hated.Count == 0)
                return;

            foreach (IUnitEntity guard in guards.Where(u => !u.InCombat))
                foreach (IUnitEntity target in hated)
                    guard.ThreatManager.UpdateThreat(target, 1);
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
            return DistanceToPolyline(alarmLine, point);
        }

        private static float DistanceToPolyline(List<Vector2> line, Vector2 point)
        {
            if (line.Count == 1)
                return Vector2.Distance(point, line[0]);

            float distance = float.MaxValue;
            for (int i = 0; i < line.Count - 1; i++)
            {
                Vector2 a = line[i];
                Vector2 ab = line[i + 1] - a;
                float t = ab.LengthSquared() > 0f ? Math.Clamp(Vector2.Dot(point - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
                distance = MathF.Min(distance, Vector2.Distance(point, a + ab * t));
            }

            return distance;
        }

        private enum SpotlightState
        {
            Patrol,
            Track,
            Return
        }

        private class Spotlight
        {
            public List<Vector3> Loop; // one node: no lane, it stands there
            public SpotlightState State;
            public uint TargetGuid;
            public double Retrack;
            public int ReturnNode;
            public Vector3? ResumePosition;

            public double Cooldown;
            public double Firing;
            public double NextPulse;
        }

        private void UpdateSpotlightMovement(IUnitEntity entity, Spotlight spotlight, double lastTick)
        {
            switch (spotlight.State)
            {
                case SpotlightState.Patrol:
                {
                    IPlayer target = mapInstance.GetPlayers()
                        .Where(p => p.IsAlive
                            && Vector3.Distance(p.Position, entity.Position) <= SpotlightAggroRange
                            && DistanceToLoop(spotlight.Loop, p.Position) <= SpotlightLeash)
                        .OrderBy(p => Vector3.Distance(p.Position, entity.Position))
                        .FirstOrDefault();
                    if (target == null)
                        break;

                    RememberLanePosition(entity, spotlight);
                    walks.Remove(entity.Guid);
                    spotlight.State      = SpotlightState.Track;
                    spotlight.TargetGuid = target.Guid;
                    spotlight.Retrack    = 0d;
                    break;
                }
                case SpotlightState.Track:
                {
                    IPlayer target = mapInstance.GetEntity<IPlayer>(spotlight.TargetGuid);
                    if (target == null
                        || !target.IsAlive
                        || Vector3.Distance(target.Position, entity.Position) > SpotlightDropRange
                        || DistanceToLoop(spotlight.Loop, target.Position) > SpotlightLeash)
                    {
                        ReturnToLane(entity, spotlight);
                        break;
                    }

                    spotlight.Retrack -= lastTick;
                    if (spotlight.Retrack > 0d)
                        break;

                    spotlight.Retrack = SpotlightRetrackInterval.TotalSeconds;
                    if (Vector3.Distance(target.Position, entity.Position) > 0.5f)
                        MoveTo(entity, target.Position, SpotlightTrackSpeed);
                    break;
                }
                case SpotlightState.Return:
                {
                    Vector3 node = spotlight.ResumePosition ?? spotlight.Loop[spotlight.ReturnNode];
                    if (Vector3.Distance(entity.Position, node) > 0.5f)
                        break;

                    spotlight.State          = SpotlightState.Patrol;
                    spotlight.ResumePosition = null;
                    if (spotlight.Loop.Count > 1)
                        LaunchLoop(entity, spotlight.Loop, spotlight.ReturnNode, SpotlightSpeed);
                    break;
                }
            }
        }

        /// <summary>
        /// Remember where on its lane the spotlight was and which node it was heading to, it resumes there after tracking.
        /// </summary>
        private void RememberLanePosition(IUnitEntity entity, Spotlight spotlight)
        {
            spotlight.ResumePosition = null;
            if (!walks.TryGetValue(entity.Guid, out Walk walk))
                return;

            // distance walked in this round, then the segment it is on
            double walked = Math.Clamp(walk.Duration - walk.Remaining + 0.25d, 0d, walk.Duration) * walk.Speed;
            int next = walk.Nodes.Count - 1;
            for (int i = 1; i < walk.Nodes.Count; i++)
            {
                walked -= Vector3.Distance(walk.Nodes[i - 1], walk.Nodes[i]);
                if (walked <= 0d)
                {
                    next = i;
                    break;
                }
            }

            spotlight.ResumePosition = entity.Position;
            spotlight.ReturnNode     = NearestNode(spotlight.Loop, walk.Nodes[next]);
        }

        private static void ReturnToLane(IUnitEntity entity, Spotlight spotlight)
        {
            // back to where it left its lane, then on towards the node it was heading to; without that, the nearest node
            if (!spotlight.ResumePosition.HasValue)
                spotlight.ReturnNode = NearestNode(spotlight.Loop, entity.Position);
            spotlight.State = SpotlightState.Return;

            Vector3 node = spotlight.ResumePosition ?? spotlight.Loop[spotlight.ReturnNode];
            if (Vector3.Distance(entity.Position, node) > 0.5f)
                MoveTo(entity, node, SpotlightReturnSpeed);
        }

        private static float DistanceToLoop(List<Vector3> loop, Vector3 position)
        {
            return DistanceToPolyline(loop.Select(n => new Vector2(n.X, n.Z)).ToList(), new Vector2(position.X, position.Z));
        }

        private void UpdateSpotlights(double lastTick)
        {
            foreach ((uint guid, Spotlight state) in spotlights)
            {
                if (mapInstance.GetEntity<IWorldEntity>(guid) is not IUnitEntity spotlight)
                    continue;

                UpdateSpotlightMovement(spotlight, state, lastTick);

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
            // a moment after the talk: said in the same instant as the dialog window of the interaction, his line didn't
            // show
            actionQueue.Enqueue(TarquimPleaDelay, () =>
            {
                IWorldEntity tarquim = mapInstance.GetEntity<IWorldEntity>(tarquimGuid);
                if (tarquim != null)
                    dialogue.Say(tarquim, TarquimPlea, true);
            });

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

                // one captive at a time: Prema and her guard appear now, and the map points to her
                publicEvent.SetPhase(PremaPhase);
                publicEvent.SetObjectiveLocations(RescueCaptives, PremaLocation);

                // the rebels go ahead to Sinnatus's Barn for the regroup
                mapInstance.PublicEventManager.GetEvent(HycrestPublicEvent.Main)?
                    .InvokeScriptCollection<IHycrestMainEventScript>(s => s.PrepareHideout(publicEvent.Id));
            }
            else if (entity.Guid == premaGuid && premaFree && !premaThanked)
            {
                premaThanked = true;
                dialogue.Say(entity, PremaThanks, false);
            }
        }
    }
}
