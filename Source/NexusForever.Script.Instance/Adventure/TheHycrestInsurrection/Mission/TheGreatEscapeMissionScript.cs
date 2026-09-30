using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Abstract.Entity.Trigger;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Static.Entity.Movement.Command.Mode;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.GameTable;
using NexusForever.Script.Template.Filter;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// The Great Escape (public event 423, tier 2 Merciful): help three families out of Hycrest past the guards at the gates,
    /// then return to the Abandoned Barn.
    /// </summary>
    /// <remarks>
    /// Retail videos (measured 29-30 Sep 2026, docs research/great-escape.md): the families wait in their houses; walking into
    /// a house starts that family, in any order and several at once. They run their route to the gate (Spline2, leg 1), where
    /// Dominion waves spawn: one wave per family already out plus one (1st family out 1 wave, 2nd 2, 3rd 3). When the last
    /// wave is dead Ayita's communicator comes in with the tracker's +1, and the family runs out through the gate (leg 2) and
    /// disappears. After the third family: "Return to the Barn".
    /// </remarks>
    [ScriptFilterOwnerId(423u)]
    public class TheGreatEscapeMissionScript : HycrestMissionScript
    {
        private const uint HelpFamilies = 203u;
        private const uint ReturnToBarn = 2171u;

        private const uint DominionSoldier   = 17857u;
        private const uint DominionTechsmith = 17859u;
        private const uint AyitaSinnatus     = 48032u;
        private const uint DominionGatekeeper = 26853u;

        // the gatekeeper at Highfeather's gate turns farmers away, as speech bubbles only (retail video): he starts when a
        // player comes near and goes on every few seconds while someone is near
        private static readonly uint[] GatekeeperLines = [465729u, 465730u, 465731u, 465732u, 465733u];
        private const float GatekeeperRange = 25f;
        private const double GatekeeperMinInterval = 10d;
        private const double GatekeeperMaxInterval = 16d;
        private uint gatekeeperGuid;
        private double gatekeeperTimer;

        private const uint FatherGuards        = 459878u; // "Guards! I knew this would happen!"
        private const uint FatherProtect       = 459879u; // "Protect my family!"
        private const uint ChildCaught         = 466691u; // "What'll happen if we're caught?"
        private const uint TechsmithPapers     = 462187u; // "Let me see your papers!"
        private static readonly uint[] GuardLines =
        [
            462183u, // "Where are you headed, citizen?"
            462184u, // "Fraternizing with Exile filth? You should know better."
            462187u, // "Let me see your papers!"
            462189u  // "Where do you think you're off to?!"
        ];

        // Ayita's communicator per family out, in order (confirmed by video), and the payoff at the barn
        private static readonly uint[] AyitaFamilySaved = [462424u, 462995u, 465434u];
        private const uint AyitaPayoff = 444060u; // "After they left, the Bell family said we are free to use their home..."

        // "Return to the Barn": the objective's trigger volume (object 1994) in the Abandoned Barn, the same small trigger as
        // the barn regroup (see HycrestRegroupEventScript)
        private const uint BarnTriggerId = 114910u;
        private const float BarnTriggerRange = 4f;
        private static readonly Vector3 BarnTriggerPosition = new(-2525.34f, -925.82f, -1190.13f);

        // a family starts when a player is this close to the middle of their house
        private const float HouseTriggerRange = 6f;

        // families run; the table routes carry no speed. At 6 m/s the client still showed a walk; 8 m/s is what chasing
        // units use (move speed x 8) and runs
        private const float RunSpeed = 8f;
        // the father speaks first, then they set off (moving right away seemed to swallow his speech bubble)
        private static readonly TimeSpan SetOffDelay = TimeSpan.FromSeconds(2);
        // single file: each member starts this much after the one in front
        private static readonly TimeSpan FileSpacing = TimeSpan.FromSeconds(0.6);
        // side by side: the members' lanes are this far apart
        private const float LaneWidth = 1.2f;

        private static readonly TimeSpan WaveDelay = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan EngageDelay = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan MissionEndDelay = TimeSpan.FromSeconds(4);

        private enum Role
        {
            Father,
            Mother,
            Child1,
            Child2
        }

        private enum Stage
        {
            Waiting,
            ToGate,
            Fighting,
            Leaving,
            Gone
        }

        /// <summary>
        /// A family's measured data: members, their house (trigger and marker), their gate, the routes and what they say.
        /// </summary>
        private record FamilyData(
            string Name,
            Dictionary<Role, uint> Members,
            uint HouseLocation,
            Vector3 House,
            uint GateLocation,
            ushort LegIn,
            ushort LegOut,
            uint HouseLine,
            Role[] RunOrder,
            bool SideBySide,
            (Role Speaker, uint Text, double Seconds)[] Chatter,
            Vector3[] EnemySlots,
            (Role Role, float X, float Z)[] GateSpots);

        private static readonly FamilyData[] Families =
        [
            new("Thatcher",
                new() { [Role.Father] = 17884u, [Role.Mother] = 17886u, [Role.Child1] = 17887u, [Role.Child2] = 17885u },
                45851u, new(-2400.6f, -925.3f, -1219.4f), 45919u, 14702, 14703,
                439228u, // "Have you seen the city exits? They may as well just drop in a Warbot! It's hopeless!"
                [Role.Child1, Role.Father, Role.Child2, Role.Mother], false,
                [(Role.Child1, 466695u, 3d), (Role.Mother, 466694u, 6d)], // "I'm scared." / "Just keep moving..."
                [new(-2353.1416f, -920.2465f, -1189.9047f), new(-2350.281f, -919.673f, -1185.1029f), new(-2355.8066f, -919.5453f, -1185.137f)],
                [(Role.Mother, -2357f, -1205f), (Role.Child1, -2351f, -1209f), (Role.Child2, -2353f, -1205f)]),
            new("Bell",
                new() { [Role.Father] = 50931u, [Role.Mother] = 50933u, [Role.Child1] = 50934u, [Role.Child2] = 50932u },
                45850u, new(-2263.5f, -925.0f, -1331.2f), 45920u, 14704, 14705,
                439225u, // "The guards have doubled within the last hour..."
                [Role.Child1, Role.Father, Role.Child2, Role.Mother], false,
                [(Role.Child2, 466692u, 4d), (Role.Child1, 466693u, 12d), (Role.Mother, 466698u, 15d)], // trust them? / Dominion catches us? / okay
                [new(-2222.9888f, -929.37646f, -1261.5889f), new(-2221.6638f, -929.2328f, -1257.918f), new(-2217.944f, -929.41488f, -1257.3063f)],
                [(Role.Mother, -2235f, -1269f), (Role.Child1, -2233f, -1276f), (Role.Child2, -2232f, -1272f)]),
            new("Miller",
                new() { [Role.Father] = 50935u, [Role.Mother] = 50937u, [Role.Child1] = 50938u, [Role.Child2] = 50936u },
                45852u, new(-2340.8f, -928.0f, -1501.4f), 45921u, 14706, 14707,
                439224u, // "Thank goodness you're here. We were going to leave, but the guards are blocking all the exits."
                [Role.Child2, Role.Child1, Role.Father, Role.Mother], true,
                [(Role.Child1, 466689u, 4d), (Role.Mother, 466690u, 7d)], // "Can we trust Exiles?" / "Ayita said they'd help us."
                [new(-2245.3706f, -929.4354f, -1479.0824f), new(-2243.6775f, -928.9516f, -1483.457f), new(-2237.5989f, -929.13055f, -1482.1085f)],
                [(Role.Mother, -2252f, -1473f), (Role.Child1, -2258f, -1476f), (Role.Child2, -2254f, -1476f)])
        ];

        // wave make-ups seen in the videos, picked at random per wave and shuffled over the gate's three slots
        private static readonly uint[][] WaveMakeUps =
        [
            [DominionSoldier, DominionSoldier, DominionTechsmith],
            [DominionSoldier, DominionSoldier, DominionSoldier]
        ];

        private class Family
        {
            public FamilyData Data;
            public readonly Dictionary<Role, uint> Guids = [];
            public Stage Stage;
            public int Waves;
            public int Wave;
            public readonly List<IUnitEntity> Enemies = [];
            public double Timer;
        }

        private readonly List<Family> families = [.. Families.Select(d => new Family { Data = d })];
        private int saved;
        private bool ending;
        private IVolumeGridTriggerEntity barnTrigger;

        private readonly TimedActionQueue actionQueue = new();

        #region Dependency Injection

        private readonly ILogger<TheGreatEscapeMissionScript> log;
        private readonly IGameTableManager gameTableManager;
        private readonly IStoryBuilder storyBuilder;
        private readonly ICreatureInfoManager creatureInfoManager;
        private readonly HycrestDialogue dialogue;

        public TheGreatEscapeMissionScript(
            ILogger<TheGreatEscapeMissionScript> log,
            IGameTableManager gameTableManager,
            IStoryBuilder storyBuilder,
            ICreatureInfoManager creatureInfoManager)
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
            UpdateMarkers();
        }

        /// <summary>
        /// Invoked when a <see cref="IGridEntity"/> is added to the map the public event is on.
        /// </summary>
        public override void OnAddToMap(IGridEntity entity)
        {
            if (entity is not IWorldEntity worldEntity || !IsOwnEntity(entity))
                return;

            foreach (Family family in families)
                foreach ((Role role, uint creatureId) in family.Data.Members)
                    if (worldEntity.CreatureId == creatureId)
                        family.Guids[role] = worldEntity.Guid;

            if (worldEntity.CreatureId == DominionGatekeeper)
                gatekeeperGuid = worldEntity.Guid;

            // weapons away until they fight (see The Farmer's Daughter)
            if (worldEntity is IUnitEntity { InCombat: false })
                worldEntity.Sheathed = true;
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public override void Update(double lastTick)
        {
            actionQueue.Update(lastTick);

            if (publicEvent.HasFinished)
                return;

            foreach (Family family in families)
                UpdateFamily(family, lastTick);

            UpdateGatekeeper(lastTick);
        }

        private void UpdateGatekeeper(double lastTick)
        {
            if (mapInstance.GetEntity<IWorldEntity>(gatekeeperGuid) is not IUnitEntity { IsAlive: true, InCombat: false } gatekeeper)
                return;

            // quiet while nobody is near; the next player to come close hears him right away
            if (!mapInstance.GetPlayers().Any(p => p.IsAlive && Vector3.Distance(p.Position, gatekeeper.Position) <= GatekeeperRange))
            {
                gatekeeperTimer = 0d;
                return;
            }

            gatekeeperTimer -= lastTick;
            if (gatekeeperTimer > 0d)
                return;

            gatekeeperTimer = GatekeeperMinInterval + Random.Shared.NextDouble() * (GatekeeperMaxInterval - GatekeeperMinInterval);
            dialogue.Bark(gatekeeper, GatekeeperLines[Random.Shared.Next(GatekeeperLines.Length)]);
        }

        private void UpdateFamily(Family family, double lastTick)
        {
            switch (family.Stage)
            {
                case Stage.Waiting:
                    if (!ending && family.Guids.Count > 0 && mapInstance.GetPlayers()
                        .Any(p => p.IsAlive && Vector3.Distance(p.Position, family.Data.House) <= HouseTriggerRange))
                        StartFamily(family);
                    break;
                case Stage.ToGate:
                    family.Timer -= lastTick;
                    if (family.Timer <= 0d)
                        StartFight(family);
                    break;
                case Stage.Fighting:
                    UpdateFight(family, lastTick);
                    break;
                case Stage.Leaving:
                    family.Timer -= lastTick;
                    if (family.Timer <= 0d)
                        RemoveFamily(family);
                    break;
            }
        }

        /// <summary>
        /// A player walked into the house: the father speaks and the family runs to their gate, chatting on the way.
        /// </summary>
        private void StartFamily(Family family)
        {
            FamilyData data = family.Data;
            family.Stage = Stage.ToGate;
            log.LogInformation($"Hycrest: the {data.Name} family is on its way to the gate.");

            dialogue.Say(GetMember(family, Role.Father), data.HouseLine, false);

            List<Vector3> route = GetRoute(data.LegIn);
            Vector3 end = route[^1];
            Vector3 forward = Flat(route[^1] - route[^2]);
            Vector3 side = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

            // at the gate the father stops at the end of the route, the others a few metres behind him: retail's spots
            // (archived Jabbithole sightings of each family member near the gate); a triangle behind him otherwise
            var spots = new Dictionary<Role, Vector3>
            {
                [Role.Father] = end,
                [Role.Mother] = end - forward * 1.5f + side * 1.5f,
                [Role.Child1] = end - forward * 1.5f - side * 1.5f,
                [Role.Child2] = end - forward * 3f
            };
            foreach ((Role role, float x, float z) in data.GateSpots)
                spots[role] = new Vector3(x, end.Y, z);

            double arrival = 0d;
            for (int i = 0; i < data.RunOrder.Length; i++)
            {
                Role role = data.RunOrder[i];
                IWorldEntity member = GetMember(family, role);
                if (member == null)
                    continue;

                List<Vector3> path;
                double delay;
                if (data.SideBySide)
                {
                    float lane = (i - (data.RunOrder.Length - 1) / 2f) * LaneWidth;
                    path  = Offset(route, lane);
                    delay = 0d;
                }
                else
                {
                    path  = [.. route];
                    delay = FileSpacing.TotalSeconds * i;
                }

                // stop short for a spot behind the father, rather than running to the end of the route and back
                Vector3 spot = Grounded(spots[role]);
                float back = Vector3.Distance(spot, end);
                while (path.Count > 1 && Vector3.Distance(path[^1], end) < back)
                    path.RemoveAt(path.Count - 1);
                path.Add(spot);
                delay += SetOffDelay.TotalSeconds;
                arrival = Math.Max(arrival, delay + Run(member, path, delay));
            }

            family.Timer = arrival;

            foreach ((Role speaker, uint text, double seconds) in data.Chatter)
                actionQueue.Enqueue(TimeSpan.FromSeconds(seconds) + SetOffDelay, () => dialogue.Bark(GetMember(family, speaker), text));

            UpdateMarkers();
        }

        /// <summary>
        /// The family reached the end of their route: the first wave comes out of the gate.
        /// </summary>
        private void StartFight(Family family)
        {
            family.Stage = Stage.Fighting;
            family.Waves = saved + 1;
            family.Wave  = 0;
            log.LogInformation($"Hycrest: the {family.Data.Name} family is at the gate, {family.Waves} wave(s).");

            dialogue.Bark(GetMember(family, Role.Child2), ChildCaught);
            SpawnWave(family);
        }

        private void UpdateFight(Family family, double lastTick)
        {
            if (family.Timer > 0d)
            {
                family.Timer -= lastTick;
                if (family.Timer <= 0d)
                    SpawnWave(family);
                return;
            }

            // a created unit is only added to the map (and gets its guid) on a later tick: until then it counts as alive
            if (family.Enemies.Any(e => e.InWorld ? e.IsAlive : e.Guid == 0u))
                return;

            family.Enemies.Clear();
            if (family.Wave < family.Waves)
            {
                family.Timer = WaveDelay.TotalSeconds;
                return;
            }

            OnFamilySaved(family);
        }

        private void SpawnWave(Family family)
        {
            family.Wave++;

            List<uint> makeUp = [.. WaveMakeUps[Random.Shared.Next(WaveMakeUps.Length)]];
            Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(makeUp));

            IWorldEntity father = GetMember(family, Role.Father);
            Vector3 facing = father?.Position ?? family.Data.EnemySlots[0];

            var wave = new List<IUnitEntity>();
            for (int i = 0; i < makeUp.Count && i < family.Data.EnemySlots.Length; i++)
            {
                IUnitEntity enemy = Spawn(makeUp[i], family.Data.EnemySlots[i], facing);
                if (enemy == null)
                    continue;

                wave.Add(enemy);
                family.Enemies.Add(enemy);
            }

            // guards announce themselves, the father calls for help: every wave, as retail
            List<IUnitEntity> speakers = [.. wave.OrderBy(_ => Random.Shared.Next()).Take(2)];
            List<uint> lines = [.. GuardLines.OrderBy(_ => Random.Shared.Next())];
            actionQueue.Enqueue(EngageDelay, () =>
            {
                for (int i = 0; i < speakers.Count; i++)
                {
                    uint line = speakers[i].CreatureId == DominionTechsmith ? TechsmithPapers : lines[i];
                    dialogue.Say(speakers[i], line, false);
                }

                if (family.Wave == 1)
                    dialogue.Say(GetMember(family, Role.Father), FatherGuards, false);
                actionQueue.Enqueue(TimeSpan.FromSeconds(1.5), () => dialogue.Say(GetMember(family, Role.Father), FatherProtect, false));

                foreach (IUnitEntity enemy in wave.Where(u => u.InWorld && u.IsAlive))
                {
                    IPlayer target = mapInstance.GetPlayers()
                        .Where(p => p.IsAlive)
                        .OrderBy(p => Vector3.Distance(p.Position, enemy.Position))
                        .FirstOrDefault();
                    if (target != null)
                        enemy.ThreatManager.UpdateThreat(target, 1);
                }
            });

            log.LogInformation($"Hycrest: {family.Data.Name} gate, wave {family.Wave}/{family.Waves}: {string.Join(", ", makeUp)}.");
        }

        /// <summary>
        /// The last wave is dead: Ayita's communicator with the tracker's +1, and the family runs out through the gate.
        /// </summary>
        private void OnFamilySaved(Family family)
        {
            FamilyData data = family.Data;
            uint communicator = AyitaFamilySaved[Math.Min(saved, AyitaFamilySaved.Length - 1)];
            saved++;
            log.LogInformation($"Hycrest: the {data.Name} family is safe ({saved}/{Families.Length}).");

            foreach (IPlayer player in mapInstance.GetPlayers())
                storyBuilder.SendStoryCommunicator(communicator, AyitaSinnatus, player);
            publicEvent.UpdateObjective(HelpFamilies, 1);

            // out through the gate in single file, gone at the end of the route
            family.Stage = Stage.Leaving;
            List<Vector3> route = GetRoute(data.LegOut);
            double gone = 0d;
            for (int i = 0; i < data.RunOrder.Length; i++)
            {
                IWorldEntity member = GetMember(family, data.RunOrder[i]);
                if (member == null)
                    continue;

                // each member disappears as they reach the end of the route
                double delay = FileSpacing.TotalSeconds * i;
                double end = delay + Run(member, [.. route], delay);
                uint guid = member.Guid;
                actionQueue.Enqueue(TimeSpan.FromSeconds(end + 0.2d), () =>
                {
                    if (mapInstance.GetEntity<IWorldEntity>(guid) is { InWorld: true } leaving)
                        leaving.RemoveFromMap();
                });
                gone = Math.Max(gone, end);
            }
            family.Timer = gone + 0.5d;

            UpdateMarkers();

            if (saved == Families.Length)
                StartReturnToBarn();
        }

        private void RemoveFamily(Family family)
        {
            family.Stage = Stage.Gone;
            foreach (uint guid in family.Guids.Values)
                if (mapInstance.GetEntity<IWorldEntity>(guid) is { InWorld: true } member)
                    member.RemoveFromMap();
            log.LogInformation($"Hycrest: the {family.Data.Name} family has left Hycrest.");
        }

        private void StartReturnToBarn()
        {
            ending = true;
            publicEvent.ActivateObjective(ReturnToBarn, (uint)Math.Max(1, mapInstance.PlayerCount));

            // players already in the barn are counted when the trigger is added
            barnTrigger = publicEvent.CreateEntity<IVolumeGridTriggerEntity>();
            barnTrigger.Initialise(BarnTriggerId, BarnTriggerRange, gameTableManager.PublicEventObjective.GetEntry(ReturnToBarn).ObjectId);
            barnTrigger.AddToMap(mapInstance, BarnTriggerPosition);
            log.LogInformation("Hycrest: all families are out, return to the barn.");
        }

        /// <summary>
        /// Invoked when the status of an objective changes.
        /// </summary>
        public override void OnPublicEventObjectiveStatus(IPublicEventObjective objective)
        {
            if (objective.Entry.Id != ReturnToBarn || objective.Status != PublicEventStatus.Succeeded)
                return;

            if (barnTrigger?.InWorld == true)
                barnTrigger.RemoveFromMap();

            foreach (IPlayer player in mapInstance.GetPlayers())
                storyBuilder.SendStoryCommunicator(AyitaPayoff, AyitaSinnatus, player);

            actionQueue.Enqueue(MissionEndDelay, CompleteMission);
        }

        /// <summary>
        /// Map markers of 203: the houses of the families that haven't started and the gates of the families on their way.
        /// </summary>
        private void UpdateMarkers()
        {
            uint[] locations = families
                .Where(f => f.Stage is Stage.Waiting or Stage.ToGate or Stage.Fighting)
                .Select(f => f.Stage == Stage.Waiting ? f.Data.HouseLocation : f.Data.GateLocation)
                .ToArray();
            publicEvent.SetObjectiveLocations(HelpFamilies, locations);
        }

        private IWorldEntity GetMember(Family family, Role role)
        {
            return family.Guids.TryGetValue(role, out uint guid) ? mapInstance.GetEntity<IWorldEntity>(guid) : null;
        }

        /// <summary>
        /// The table route's nodes, without the repeated control points at its ends.
        /// </summary>
        private List<Vector3> GetRoute(ushort splineId)
        {
            List<Vector3> route = [];
            foreach (var node in gameTableManager.Spline2Node.Entries
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
        /// Run <paramref name="member"/> along <paramref name="path"/> from where it stands, after <paramref name="delay"/>
        /// seconds; returns the running time.
        /// </summary>
        private double Run(IWorldEntity member, List<Vector3> path, double delay)
        {
            float length = Vector3.Distance(member.Position, path[0]);
            for (int i = 1; i < path.Count; i++)
                length += Vector3.Distance(path[i - 1], path[i]);

            uint guid = member.Guid;
            actionQueue.Enqueue(TimeSpan.FromSeconds(delay), () =>
            {
                if (mapInstance.GetEntity<IWorldEntity>(guid) is not { InWorld: true } entity)
                    return;

                List<Vector3> nodes = [entity.Position, .. path];
                entity.MovementManager.SetMode(ModeType.Walk);
                entity.MovementManager.LaunchSpline(nodes, SplineType.Linear, SplineMode.OneShot, RunSpeed);
            });

            return length / RunSpeed;
        }

        /// <summary>
        /// The route shifted sideways by <paramref name="lane"/> metres (positive: right of the walking direction).
        /// </summary>
        private List<Vector3> Offset(List<Vector3> route, float lane)
        {
            var result = new List<Vector3>(route.Count);
            for (int i = 0; i < route.Count; i++)
            {
                Vector3 forward = Flat(route[Math.Min(i + 1, route.Count - 1)] - route[Math.Max(i - 1, 0)]);
                Vector3 side = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
                result.Add(Grounded(route[i] + side * lane));
            }

            return result;
        }

        private static Vector3 Flat(Vector3 direction)
        {
            direction.Y = 0f;
            return direction.LengthSquared() > 0.0001f ? Vector3.Normalize(direction) : Vector3.UnitZ;
        }

        private Vector3 Grounded(Vector3 position)
        {
            float? height = mapInstance.GetTerrainHeight(position.X, position.Z);
            if (height.HasValue)
                position.Y = height.Value;
            return position;
        }

        private IUnitEntity Spawn(uint creatureId, Vector3 position, Vector3 facing)
        {
            ICreatureInfo creatureInfo = creatureInfoManager.GetCreatureInfo(creatureId);
            if (creatureInfo == null)
                return null;

            var entity = publicEvent.CreateEntity<INonPlayerEntity>();
            entity.Initialise(creatureInfo);

            Vector3 direction = Flat(facing - position);
            entity.Rotation = new Vector3(MathF.Atan2(-direction.X, -direction.Z), 0f, 0f);

            entity.AddToMap(mapInstance, position);
            return entity;
        }
    }
}
