using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity.Movement.Command.Mode;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Shared;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// Base for the missions in the fields that share the Dominion's spotlights and patrol walks: The Farmer's Daughter
    /// and Breach of Protocol (the same night layout).
    /// </summary>
    /// <remarks>
    /// Moved unchanged out of <see cref="TheFarmersDaughterMissionScript"/>; a mission calls <see cref="OnAddFieldUnit"/>
    /// from its OnAddToMap and <see cref="UpdateWalks"/> and <see cref="UpdateSpotlights"/> from its Update.
    /// </remarks>
    public abstract class HycrestFieldMissionScript : HycrestMissionScript
    {
        protected const uint DominionScout  = 17856u;
        protected const uint SpotlightTarget = 17763u;

        private readonly ILogger log;
        private readonly IGameTableManager gameTableManager;
        private readonly IFactory<ISpellParameters> spellParametersFactory;

        protected HycrestFieldMissionScript(ILogger log, IGameTableManager gameTableManager, IFactory<ISpellParameters> spellParametersFactory)
        {
            this.log                    = log;
            this.gameTableManager       = gameTableManager;
            this.spellParametersFactory = spellParametersFactory;
        }

        /// <summary>
        /// Invoked when the hideout's door closes after the mission: the units go, and so do their walks and lanes (a
        /// finished mission's script keeps updating, and their guids get reused).
        /// </summary>
        public override void OnHideoutClosed()
        {
            base.OnHideoutClosed();
            walks.Clear();
            spotlights.Clear();
        }

        /// <summary>
        /// Start a scout's patrol or a spotlight's lane; returns true if <paramref name="worldEntity"/> was one of them.
        /// </summary>
        protected bool OnAddFieldUnit(IWorldEntity worldEntity)
        {
            switch (worldEntity.CreatureId)
            {
            case DominionScout:
            {
                List<Vector3> loop = GetLoop(worldEntity.Position, Patrols);
                if (loop != null)
                    LaunchLoop(worldEntity, loop, 0, PatrolSpeed);
                return true;
            }
            case SpotlightTarget:
            {
                var spotlight = new Spotlight
                {
                    Loop = GetLoop(worldEntity.Position, SpotlightLanes)
                        ?? GetLoop(worldEntity.Position, GetSplineLanes())
                        ?? [worldEntity.Position]
                };
                spotlights[worldEntity.Guid] = spotlight;
                if (spotlight.Loop.Count > 1)
                    LaunchLoop(worldEntity, spotlight.Loop, 0, SpotlightSpeed);
                return true;
            }
            }

            return false;
        }

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
        // hand-made routes for units without a table spline: spawn position (first node) and the route walked there and
        // back. Empty for now: the scouts stand at their retail spots and every spotlight follows a table spline; the
        // earlier scout patrols and spotlight #1's lane are kept in docs/hycrest/legacy/field-routes.md
        private static readonly Vector3[][] Patrols = [];
        private static readonly Vector3[][] SpotlightLanes = [];

        // the spotlights patrol table splines around the three captive areas (retail: archived Jabbithole spotlight
        // positions lie around all three, whichever layout; spline groups of five small loops each)
        private static readonly ushort[] SpotlightSplines =
        [
            7960, 7961, 7962, 7963, 7964,  // layout A area
            7950, 7952, 7954, 7956, 7959,  // layout B area
            7932, 7933, 7934, 7935,        // layout C area
            4652                           // spotlight #1 (144 m, through both measured ends of its lane)
        ];
        private readonly Dictionary<uint, Spotlight> spotlights = [];
        private Vector3[][] splineLanes;

        /// <summary>
        /// The nodes of the <see cref="SpotlightSplines"/>.
        /// </summary>
        private Vector3[][] GetSplineLanes()
        {
            return splineLanes ??= SpotlightSplines
                .Select(GetSplineNodes)
                .Where(n => n.Length > 1)
                .ToArray();
        }

        /// <summary>
        /// Return the nodes of table spline <paramref name="splineId"/> in order, without repeated control points.
        /// </summary>
        protected Vector3[] GetSplineNodes(ushort splineId)
        {
            var nodes = new List<Vector3>();
            foreach (Spline2NodeEntry node in gameTableManager.Spline2Node.Entries
                .Where(n => n.SplineId == splineId)
                .OrderBy(n => n.Ordinal))
            {
                var position = new Vector3(node.Position0, node.Position1, node.Position2);
                if (nodes.Count == 0 || Vector3.Distance(nodes[^1], position) > 0.05f)
                    nodes.Add(position);
            }
            return nodes.ToArray();
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
            public uint CreatureId;
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
            log.LogDebug($"Hycrest: walk round for {entity.Guid} (creature {entity.CreatureId}) from node {start}, {nodes.Count} nodes, {length:0.0} m, {length / speed:0.0} s, first {nodes[0]} last {nodes[^1]}.");

            walks[entity.Guid] = new Walk
            {
                CreatureId = entity.CreatureId,
                Loop      = loop,
                Nodes     = nodes,
                Speed     = speed,
                Duration  = length / speed,
                Remaining = length / speed + 0.25d
            };
        }

        protected void UpdateWalks(double lastTick)
        {
            foreach ((uint guid, Walk walk) in walks.ToList())
            {
                // the walker may be gone and its guid reused: only the unit that started the walk
                IWorldEntity entity = mapInstance.GetEntity<IWorldEntity>(guid);
                if (entity == null || entity.CreatureId != walk.CreatureId || entity is IUnitEntity { IsAlive: false })
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

        protected void UpdateSpotlights(double lastTick)
        {
            foreach ((uint guid, Spotlight state) in spotlights)
            {
                // a removed spotlight's guid can belong to another unit by now (it moved Vesna in a later hideout)
                if (mapInstance.GetEntity<IWorldEntity>(guid) is not IUnitEntity { CreatureId: SpotlightTarget } spotlight)
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
    }
}
