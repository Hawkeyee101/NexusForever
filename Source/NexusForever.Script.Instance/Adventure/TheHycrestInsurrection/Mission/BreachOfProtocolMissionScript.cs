using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Entity.Movement.Command.Mode;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.Game.Static.Reputation;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;
using NexusForever.Script.Template.Filter;
using NexusForever.Shared;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// Breach of Protocol (public event 426, tier 3 interlude, Merciful): Ayita calls the players to a secret meeting
    /// outside the city; it is a trap set by General Arsenax Severus.
    /// </summary>
    /// <remarks>
    /// Retail video (Teun, 1 Oct 2026) and archived Jabbithole positions, docs research/breach-of-protocol.md. Night,
    /// the fields' scouts and spotlights again (shared with The Farmer's Daughter), no alarm. Ayita cries on the floor at
    /// the meeting point; talking to her (207) brings Arsenax right behind her, out of reach, and "Survive the Ambush"
    /// (1767): four waves of six Ambushers (two groups of three at random spots of the fight), then Arsenax himself, who
    /// runs off into the city before he can be beaten (he counts as the 25th). Then the regroup at Arcwulff Farm.
    /// </remarks>
    [ScriptFilterOwnerId(426u)]
    public class BreachOfProtocolMissionScript : HycrestFieldMissionScript
    {
        private const uint MeetAyita      = 207u;
        private const uint SurviveAmbush  = 1767u;

        private const uint AyitaMeeting   = 56411u; // Ayita Sinnatus - T3 Merciful
        private const uint AyitaSinnatus  = 48032u; // communicator portrait
        private const uint VesnaTaranoft  = 17778u;
        private const uint ArsenaxSeverus = 48373u; // Arsenax Severus - T3 Ambush
        private const uint Ambusher       = 17954u; // Dominion Ambusher - T3 Ambush

        private const uint VesnaWarning    = 454706u; // communicator: "I don't need to tell you how suspicious that call sounded..."
        private const uint AyitaSorry      = 458771u; // "I'm so sorry. They made me call you here!" (chat + communicator)
        private const uint ArsenaxSilence  = 458781u; // "Silence! You played your part. Now watch your fellow conspirators die!"
        private const uint ArsenaxLeaves   = 440673u; // "You win this round, but mark my words, Exile. Next time you won't be so lucky."
        private const uint VesnaSafehouse  = 461016u; // communicator: "Ayita Sinnatus is home safe with her father now..."

        // Ayita on the floor (emote "crying": stand state Emote, model sequence 5544), during the whole fight
        private const uint CryingEmote = 292u;
        private static readonly TimeSpan CryingResend = TimeSpan.FromSeconds(5);

        // Arsenax appears just behind her (retail video: 1-2 m)
        private const float ArsenaxBehind = 1.5f;
        // out of reach until the last wave is dead: a faction friendly to the players, the Dominion one when he joins
        private const Faction OutOfReachFaction = (Faction)219;
        private const Faction DominionFaction = (Faction)1452;

        // waves: four of six Ambushers, each as two groups of three at two different spots of the fight; the spots are the
        // retail clusters of Ambusher sightings (Jabbithole), picked at random per wave (they may well have been random)
        private const int WaveCount = 4;
        private const int GroupSize = 3;
        private static readonly Vector2[] AmbushSpots =
        [
            new(-2379f, -1758f), new(-2391f, -1753f), new(-2378f, -1769f), new(-2372f, -1780f), new(-2410f, -1761f)
        ];
        private static readonly TimeSpan FirstWaveDelay = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan WaveDelay = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ArsenaxJoinDelay = TimeSpan.FromSeconds(4);

        // Arsenax leaves after this long in the fight or at this much health, whichever comes first, and runs into the city
        private static readonly TimeSpan ArsenaxFightTime = TimeSpan.FromSeconds(30);
        private const float ArsenaxLeaveHealth = 0.5f;
        private const float RunSpeed = 8f;
        private static readonly Vector3[] ArsenaxRunPath =
        [
            new(-2392f, -904f, -1800f), new(-2385f, -904f, -1830f), new(-2378f, -904f, -1860f)
        ];

        private static readonly TimeSpan AyitaStandDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan MissionEndDelay = TimeSpan.FromSeconds(6);

        private enum Stage
        {
            Waiting,
            Waves,
            ArsenaxJoining,
            ArsenaxFighting,
            Ending
        }

        private Stage stage;
        private uint ayitaGuid;
        private IUnitEntity arsenax;
        private readonly List<IUnitEntity> wave = [];
        private int waveNumber;
        private double stageTimer;
        private double cryingTimer;
        private bool crying;

        private readonly TimedActionQueue actionQueue = new();

        #region Dependency Injection

        private readonly ILogger<BreachOfProtocolMissionScript> log;
        private readonly IGameTableManager gameTableManager;
        private readonly IStoryBuilder storyBuilder;
        private readonly ICreatureInfoManager creatureInfoManager;
        private readonly HycrestDialogue dialogue;

        public BreachOfProtocolMissionScript(
            ILogger<BreachOfProtocolMissionScript> log,
            IGameTableManager gameTableManager,
            IStoryBuilder storyBuilder,
            IFactory<ISpellParameters> spellParametersFactory,
            ICreatureInfoManager creatureInfoManager)
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
            actionQueue.Enqueue(TimeSpan.FromSeconds(2), () => Communicator(VesnaWarning, VesnaTaranoft));
        }

        /// <summary>
        /// Invoked when a <see cref="IGridEntity"/> is added to the map the public event is on.
        /// </summary>
        public override void OnAddToMap(IGridEntity entity)
        {
            if (entity is not IWorldEntity worldEntity || !IsOwnEntity(entity))
                return;

            if (worldEntity is IUnitEntity { InCombat: false } && worldEntity.CreatureId != SpotlightTarget)
                worldEntity.Sheathed = true;

            if (OnAddFieldUnit(worldEntity))
                return;

            if (worldEntity.CreatureId == AyitaMeeting)
            {
                ayitaGuid = worldEntity.Guid;
                crying    = true;
                cryingTimer = 0d;
            }
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public override void Update(double lastTick)
        {
            actionQueue.Update(lastTick);

            // the enemies stay in the fields after the mission (KeepAfterMission) and go on patrolling and watching
            UpdateWalks(lastTick);
            UpdateSpotlights(lastTick);
            UpdateCrying(lastTick);

            if (publicEvent.HasFinished)
                return;

            switch (stage)
            {
                case Stage.Waves:
                    UpdateWaves(lastTick);
                    break;
                case Stage.ArsenaxJoining:
                    stageTimer -= lastTick;
                    if (stageTimer <= 0d)
                        ArsenaxJoins();
                    break;
                case Stage.ArsenaxFighting:
                    UpdateArsenax(lastTick);
                    break;
            }
        }

        /// <summary>
        /// Keep Ayita's crying pose going; resent now and then so players coming into view see it too.
        /// </summary>
        private void UpdateCrying(double lastTick)
        {
            if (!crying)
                return;

            cryingTimer -= lastTick;
            if (cryingTimer > 0d)
                return;

            cryingTimer = CryingResend.TotalSeconds;
            if (mapInstance.GetEntity<IWorldEntity>(ayitaGuid) is { InWorld: true } ayita)
                ayita.EnqueueToVisible(new ServerEmote
                {
                    Guid       = ayita.Guid,
                    StandState = StandState.Emote,
                    EmoteId    = CryingEmote
                });
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
                case MeetAyita:
                    OnAyitaSpokenTo();
                    break;
                case SurviveAmbush:
                    OnAmbushSurvived();
                    break;
            }
        }

        /// <summary>
        /// The trap springs: Ayita's apology, Arsenax appears behind her, then the waves.
        /// </summary>
        private void OnAyitaSpokenTo()
        {
            if (stage != Stage.Waiting)
                return;

            IWorldEntity ayita = mapInstance.GetEntity<IWorldEntity>(ayitaGuid);
            if (ayita == null)
                return;

            dialogue.Say(ayita, AyitaSorry, false);
            Communicator(AyitaSorry, AyitaSinnatus);

            // just behind her, seen from the players
            IPlayer player = mapInstance.GetPlayers().OrderBy(p => Vector3.Distance(p.Position, ayita.Position)).FirstOrDefault();
            Vector3 away = Flat(ayita.Position - (player?.Position ?? ayita.Position + Vector3.UnitZ));
            arsenax = Spawn(ArsenaxSeverus, ayita.Position + away * ArsenaxBehind, ayita.Position, OutOfReachFaction);

            actionQueue.Enqueue(TimeSpan.FromSeconds(1.5), () =>
            {
                if (arsenax is { InWorld: true })
                {
                    dialogue.Say(arsenax, ArsenaxSilence, false);
                    Communicator(ArsenaxSilence, ArsenaxSeverus);
                }
            });

            publicEvent.ActivateObjective(SurviveAmbush);
            // only the ambush counts: the fields' scouts and spotlights close by aren't part of it
            foreach (IGridEntity entity in publicEvent.GetEntities().ToList())
                if (entity is IUnitEntity unit && unit.CreatureId != Ambusher)
                    publicEvent.RemoveObjectiveTarget(SurviveAmbush, unit.Guid);
            // the tracker shows all of them from the start (retail video: 25), the waves don't raise it
            publicEvent.SetObjectiveDynamicMax(SurviveAmbush, (uint)(WaveCount * 2 * GroupSize + 1));

            stage      = Stage.Waves;
            stageTimer = FirstWaveDelay.TotalSeconds;
            log.LogInformation("Hycrest: Breach of Protocol, the trap springs.");
        }

        private void UpdateWaves(double lastTick)
        {
            if (stageTimer > 0d)
            {
                stageTimer -= lastTick;
                if (stageTimer <= 0d)
                    SpawnWave();
                return;
            }

            // a created unit is only added to the map (and gets its guid) on a later tick: until then it counts as alive
            if (wave.Any(e => e.InWorld ? e.IsAlive : e.Guid == 0u))
                return;

            wave.Clear();
            if (waveNumber < WaveCount)
            {
                stageTimer = WaveDelay.TotalSeconds;
                return;
            }

            stage      = Stage.ArsenaxJoining;
            stageTimer = ArsenaxJoinDelay.TotalSeconds;
        }

        private void SpawnWave()
        {
            waveNumber++;

            List<Vector2> spots = [.. AmbushSpots.OrderBy(_ => Random.Shared.Next()).Take(2)];
            foreach (Vector2 spot in spots)
            {
                for (int i = 0; i < GroupSize; i++)
                {
                    float angle = MathF.Tau * i / GroupSize;
                    Vector3 position = Grounded(new Vector3(spot.X + MathF.Cos(angle) * 2f, 0f, spot.Y + MathF.Sin(angle) * 2f));
                    IUnitEntity ambusher = Spawn(Ambusher, position, ArsenaxPosition(), null);
                    if (ambusher != null)
                        wave.Add(ambusher);
                }
            }

            // they aggro at once, with their combat barks as bubbles
            List<IUnitEntity> spawned = [.. wave];
            actionQueue.Enqueue(TimeSpan.FromMilliseconds(500), () =>
            {
                foreach (IUnitEntity ambusher in spawned.Where(u => u.InWorld && u.IsAlive))
                {
                    Engage(ambusher);
                    Bark(ambusher, enterCombat: true);
                }
            });

            log.LogInformation($"Hycrest: Breach of Protocol, wave {waveNumber}/{WaveCount} at {string.Join(", ", spots)}.");
        }

        /// <summary>
        /// The last wave is dead: Arsenax comes within reach and joins the fight.
        /// </summary>
        private void ArsenaxJoins()
        {
            if (arsenax is not { InWorld: true, IsAlive: true })
            {
                stage = Stage.Ending;
                return;
            }

            arsenax.SetFaction(DominionFaction);
            publicEvent.AddObjectiveTarget(SurviveAmbush, arsenax);
            Engage(arsenax);
            Bark(arsenax, enterCombat: true);

            stage      = Stage.ArsenaxFighting;
            stageTimer = ArsenaxFightTime.TotalSeconds;
            log.LogInformation("Hycrest: Breach of Protocol, Arsenax joins the fight.");
        }

        private void UpdateArsenax(double lastTick)
        {
            stageTimer -= lastTick;
            bool hurt = arsenax.MaxHealth > 0 && arsenax.Health <= arsenax.MaxHealth * ArsenaxLeaveHealth;
            if (stageTimer > 0d && !hurt && arsenax.IsAlive)
                return;

            // he leaves before he can be beaten and counts as defeated
            stage = Stage.Ending;
            dialogue.Say(arsenax, ArsenaxLeaves, false);
            arsenax.SetFaction(OutOfReachFaction);
            arsenax.ThreatManager.ClearThreatList();

            List<Vector3> path = [arsenax.Position, .. ArsenaxRunPath.Select(Grounded)];
            float length = 0f;
            for (int i = 1; i < path.Count; i++)
                length += Vector3.Distance(path[i - 1], path[i]);

            IUnitEntity runner = arsenax;
            actionQueue.Enqueue(TimeSpan.FromSeconds(1), () =>
            {
                if (!runner.InWorld)
                    return;

                runner.MovementManager.SetMode(ModeType.Walk);
                runner.MovementManager.LaunchSpline(path, SplineType.Linear, SplineMode.OneShot, RunSpeed);
            });
            actionQueue.Enqueue(TimeSpan.FromSeconds(1 + length / RunSpeed), () =>
            {
                if (runner.InWorld)
                    runner.RemoveFromMap();
            });

            publicEvent.RemoveObjectiveTarget(SurviveAmbush, arsenax.Guid, defeated: true);
            log.LogInformation("Hycrest: Breach of Protocol, Arsenax runs off into the city.");
        }

        /// <summary>
        /// Invoked when a unit on the map has been killed.
        /// </summary>
        public override void OnEntityKilled(IUnitEntity unit)
        {
            if (unit.CreatureId == Ambusher && IsOwnEntity(unit))
                Bark(unit, enterCombat: false);
        }

        /// <summary>
        /// All 25 down: Ayita stands up (she goes with the mission), Vesna's communicator, then the regroup at Arcwulff Farm.
        /// </summary>
        private void OnAmbushSurvived()
        {
            stage = Stage.Ending;
            actionQueue.Enqueue(AyitaStandDelay, () =>
            {
                crying = false;
                if (mapInstance.GetEntity<IWorldEntity>(ayitaGuid) is { InWorld: true } ayita)
                    ayita.EnqueueToVisible(new ServerEmote { Guid = ayita.Guid, StandState = StandState.Stand });
            });
            actionQueue.Enqueue(TimeSpan.FromSeconds(3), () => Communicator(VesnaSafehouse, VesnaTaranoft));
            actionQueue.Enqueue(MissionEndDelay, CompleteMission);
            log.LogInformation("Hycrest: Breach of Protocol, the ambush is survived.");
        }

        // the fields' Dominion units stay until the hideout's door closes, like in The Farmer's Daughter
        private const uint HostileFaction = 1452u;

        /// <summary>
        /// Return true if <paramref name="entity"/> stays in the world after the mission.
        /// </summary>
        protected override bool KeepAfterMission(IGridEntity entity)
        {
            // Arsenax stays until he has run off (removed at the end of his path)
            return entity is IUnitEntity unit && ((uint)unit.Faction1 == HostileFaction || unit.CreatureId == ArsenaxSeverus);
        }

        private Vector3 ArsenaxPosition()
        {
            return arsenax?.Position ?? Vector3.Zero;
        }

        private void Engage(IUnitEntity unit)
        {
            IPlayer target = mapInstance.GetPlayers()
                .Where(p => p.IsAlive)
                .OrderBy(p => Vector3.Distance(p.Position, unit.Position))
                .FirstOrDefault();
            if (target != null)
                unit.ThreatManager.UpdateThreat(target, 1);
        }

        /// <summary>
        /// One of the creature's combat barks (Creature2ActionText), with the table's chance, as a speech bubble.
        /// </summary>
        private void Bark(IUnitEntity unit, bool enterCombat)
        {
            Creature2Entry creature = gameTableManager.Creature2.GetEntry(unit.CreatureId);
            Creature2ActionTextEntry barks = creature != null ? gameTableManager.Creature2ActionText.GetEntry(creature.Creature2ActionTextId) : null;
            if (barks == null)
                return;

            uint[] lines = enterCombat
                ? [barks.LocalizedTextIdOnEnterCombat00, barks.LocalizedTextIdOnEnterCombat01, barks.LocalizedTextIdOnEnterCombat02, barks.LocalizedTextIdOnEnterCombat03]
                : [barks.LocalizedTextIdOnDeath00, barks.LocalizedTextIdOnDeath01, barks.LocalizedTextIdOnDeath02, barks.LocalizedTextIdOnDeath03];
            lines = [.. lines.Where(l => l != 0u)];
            float chance = enterCombat ? barks.ChanceToSayOnEnterCombat : barks.ChanceToSayOnDeath;
            if (lines.Length == 0 || Random.Shared.NextDouble() * 100d >= chance)
                return;

            dialogue.Bark(unit, lines[Random.Shared.Next(lines.Length)]);
        }

        private void Communicator(uint textId, uint creatureId)
        {
            foreach (IPlayer player in mapInstance.GetPlayers())
                storyBuilder.SendStoryCommunicator(textId, creatureId, player);
        }

        private IUnitEntity Spawn(uint creatureId, Vector3 position, Vector3 facing, Faction? faction)
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

            Vector3 direction = Flat(facing - position);
            entity.Rotation = new Vector3(MathF.Atan2(-direction.X, -direction.Z), 0f, 0f);

            entity.AddToMap(mapInstance, Grounded(position));
            return entity;
        }

        private Vector3 Grounded(Vector3 position)
        {
            float? height = mapInstance.GetTerrainHeight(position.X, position.Z);
            if (height.HasValue)
                position.Y = height.Value;
            return position;
        }

        private static Vector3 Flat(Vector3 direction)
        {
            direction.Y = 0f;
            return direction.LengthSquared() > 0.0001f ? Vector3.Normalize(direction) : Vector3.UnitZ;
        }
    }
}
