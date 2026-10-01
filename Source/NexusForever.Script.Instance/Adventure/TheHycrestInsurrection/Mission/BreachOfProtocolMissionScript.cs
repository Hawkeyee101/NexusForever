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
using NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Script;
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

        private const uint AyitaCall       = 444062u; // communicator: "Greetings, friends! I've made contact with someone who can... help..."
        private const uint VesnaWarning    = 454706u; // communicator: "I don't need to tell you how suspicious that call sounded..."
        private const uint AyitaSorry      = 458771u; // "I'm so sorry. They made me call you here!" (chat + communicator)
        private const uint ArsenaxSilence  = 458781u; // "Silence! You played your part. Now watch your fellow conspirators die!"
        private const uint ArsenaxLeaves   = 440673u; // "You win this round, but mark my words, Exile. Next time you won't be so lucky."
        private const uint VesnaSafehouse  = 461016u; // communicator: "Ayita Sinnatus is home safe with her father now..."

        // Ayita on the floor (emote "crying": stand state Emote, model sequence 5544), during the whole fight
        private const uint CryingEmote = 292u;
        private static readonly TimeSpan CryingResend = TimeSpan.FromSeconds(5);

        // Arsenax appears at his spot just behind her, and his facing (Teun, measured in game 1 Oct 2026, 14:30)
        private static readonly Vector3 ArsenaxSpot = new(-2400.1987f, -904.2949f, -1779.9691f);
        private const float ArsenaxFacing = -2.201288f;
        // he is hostile (red, as in retail) from the moment he appears, 4 s after the talk and before the first wave (Teun,
        // 1 Oct 2026); players keep away from him (retail video; his AI attacks whoever comes within 15 m). He joins the
        // fight himself once the last wave is dead. His line comes 1.5 s after he appears, well after Ayita's communicator
        private static readonly TimeSpan ArsenaxAppearDelay = TimeSpan.FromSeconds(4);
        private const float SpawnLift = 0.3f;
        private const float AggroRange = 15f;   // CombatAI's range check
        private const Faction DominionFaction = (Faction)1452;

        // waves: four of six Ambushers, each as two groups of three at two different spots of the fight; the spots are the
        // retail clusters of Ambusher sightings (Jabbithole), picked at random per wave (they may well have been random).
        // All of them are on the ground below the raised meeting platform (builder surface.py: terrain -910.5 / -907,
        // platform -904.1), so the Ambushers are grounded
        private const int WaveCount = 4;
        private const int GroupSize = 3;
        private static readonly Vector2[] AmbushSpots =
        [
            new(-2379f, -1758f), new(-2391f, -1753f), new(-2378f, -1769f), new(-2372f, -1780f),
            new(-2368.87f, -1765.43f)   // moved from (-2410, -1761), Teun in the builder 1 Oct 2026
        ];
        private static readonly TimeSpan FirstWaveDelay = TimeSpan.FromSeconds(6);   // 2 s after Arsenax appears
        private static readonly TimeSpan WaveDelay = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ArsenaxJoinDelay = TimeSpan.FromSeconds(4);

        // Arsenax leaves after this long in the fight or at this much health, whichever comes first, and runs into the city
        private static readonly TimeSpan ArsenaxFightTime = TimeSpan.FromSeconds(30);
        // his health stops there (ArsenaxAmbushEntityScript): he can't be beaten, he leaves
        private const float ArsenaxLeaveHealth = ArsenaxAmbushEntityScript.HealthFloor;
        // down the stairs on the east side of the meeting spot (builder pipeline/surface.py: a ramp from -904.1 to -907.0),
        // then over the ground: a straight line from up there to the spline sank slowly and floated over the ground
        private static readonly Vector3[] ArsenaxStairs = [new(-2396.5f, -904.14f, -1779.5f), new(-2392.5f, -907.0f, -1779.5f)];
        // where the slope below the stairs meets the flat field (surface.py: -910.5 from x -2386 on, plain terrain)
        private static readonly Vector3 ArsenaxFieldSpot = new(-2384f, -910.5f, -1779.5f);
        // a point on the terrain every 1.5 m, also between the spline's nodes (15-20 m apart on a curving slope): 4 m steps
        // and the bare nodes left him up to 0.7 m above or in the ground (builder surface.py check, 1 Oct 2026)
        private const float GroundStep = 1.5f;
        // his escape (Teun, 1 Oct 2026): straight to the start of retail spline 14790 in the field, then along it (136 m)
        // to a gate of the city, where he is gone at its last node
        private const ushort ArsenaxEscapeSpline = 14790;

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
            // Ayita's call that sets up the meeting, then Vesna's warning about it; far enough apart that the client shows
            // both (two communicators 1.5 s apart lost the second)
            actionQueue.Enqueue(TimeSpan.FromSeconds(2), () => Communicator(AyitaCall, AyitaSinnatus));
            actionQueue.Enqueue(TimeSpan.FromSeconds(11), () => Communicator(VesnaWarning, VesnaTaranoft));
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
            if (publicEvent.HasFinished)
                crying = false;
            else if (mapInstance.GetEntity<IWorldEntity>(ayitaGuid) is { InWorld: true, CreatureId: AyitaMeeting } ayita)
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
            actionQueue.Enqueue(ArsenaxAppearDelay, AppearArsenax);

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

        /// <summary>
        /// Arsenax appears at his spot behind Ayita, hostile, and has his say.
        /// </summary>
        private void AppearArsenax()
        {
            if (publicEvent.HasFinished || stage is not (Stage.Waves or Stage.ArsenaxJoining) || arsenax != null)
                return;

            // red from the start (retail), but out of reach until he joins: he can't be attacked and notices nobody (his AI
            // uses a 15 m range check). Then a normal enemy: he fights and chases like the Ambushers (a chase follows the
            // player's own grounded position, so it takes the steps fine). Spawned 0.3 m above his measured spot, to see how
            // the client puts him down (test, Teun 1 Oct 2026)
            arsenax = Spawn(ArsenaxSeverus, ArsenaxSpot + new Vector3(0f, SpawnLift, 0f), Ahead(ArsenaxSpot, ArsenaxFacing),
                DominionFaction, grounded: false);
            SetOutOfReach(arsenax, true);
            actionQueue.Enqueue(TimeSpan.FromSeconds(1.5), () =>
            {
                if (arsenax is { InWorld: true })
                {
                    dialogue.Say(arsenax, ArsenaxSilence, false);
                    Communicator(ArsenaxSilence, ArsenaxSeverus);
                }
            });
            log.LogInformation("Hycrest: Breach of Protocol, Arsenax appears.");
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
                    var position = new Vector3(spot.X + MathF.Cos(angle) * 2f, 0f, spot.Y + MathF.Sin(angle) * 2f);
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

            // the last wave is dead: still out of reach he runs down the stairs and the slope into the field, and only there
            // becomes a normal enemy (Teun, 1 Oct 2026). A chase from up there is a straight line to the player over the
            // slope (air-walking); from the flat field it follows the player's grounded position like the Ambushers
            double engageAfter = 0d;
            if (IsAtMeetingSpot(arsenax))
            {
                List<Vector3> down = [arsenax.Position, .. ArsenaxStairs, .. OverGround(ArsenaxStairs[^1], ArsenaxFieldSpot, 1f),
                    Grounded(ArsenaxFieldSpot)];
                float speed = RunSpeedOf(arsenax);
                arsenax.MovementManager.SetMode(ModeType.Walk);
                arsenax.MovementManager.LaunchSpline(down, SplineType.Linear, SplineMode.OneShot, speed);
                engageAfter = PathLength(down) / speed;
            }

            IUnitEntity joining = arsenax;
            actionQueue.Enqueue(TimeSpan.FromSeconds(engageAfter), () =>
            {
                if (!joining.InWorld || !joining.IsAlive)
                    return;

                SetOutOfReach(joining, false);
                publicEvent.AddObjectiveTarget(SurviveAmbush, joining);
                Engage(joining);
                Bark(joining, enterCombat: true);
            });

            stage      = Stage.ArsenaxFighting;
            stageTimer = ArsenaxFightTime.TotalSeconds + engageAfter;
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
            SetOutOfReach(arsenax, true);

            // at once, in this tick: clearing his threat (SetOutOfReach) makes his combat AI reset, which sends him walking
            // back home to his spawn spot; the path launched last in a tick is the one the client gets (a second later he
            // first walked back to the stairs, 1 Oct 2026)
            IUnitEntity runner = arsenax;
            List<Vector3> path = EscapePath(runner);
            float runSpeed = RunSpeedOf(runner);
            runner.MovementManager.SetMode(ModeType.Walk);
            runner.MovementManager.LaunchSpline(path, SplineType.Linear, SplineMode.OneShot, runSpeed);
            actionQueue.Enqueue(TimeSpan.FromSeconds(PathLength(path) / runSpeed), () =>
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
            return arsenax?.Position ?? ArsenaxSpot;
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

        private IUnitEntity Spawn(uint creatureId, Vector3 position, Vector3 facing, Faction? faction, bool grounded = true)
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

            entity.AddToMap(mapInstance, grounded ? Grounded(position) : position);
            return entity;
        }

        /// <summary>
        /// Points every <see cref="GroundStep"/> metres from <paramref name="from"/> to <paramref name="to"/> (both left out),
        /// on the terrain, so a unit running between them follows the ground.
        /// </summary>
        private IEnumerable<Vector3> OverGround(Vector3 from, Vector3 to, float step = GroundStep)
        {
            int steps = (int)(Vector3.Distance(from, to) / step);
            for (int i = 1; i < steps; i++)
                yield return Grounded(Vector3.Lerp(from, to, (float)i / steps));
        }

        private Vector3 Grounded(Vector3 position)
        {
            float? height = mapInstance.GetTerrainHeight(position.X, position.Z);
            if (height.HasValue)
                position.Y = height.Value;
            return position;
        }

        /// <summary>
        /// Return true if <paramref name="unit"/> is up on the raised meeting spot (above the foot of its stairs, near his spot).
        /// </summary>
        /// <summary>
        /// Arsenax's way out from where he is right now: down the stairs if he is still up at the meeting spot, over the
        /// ground to the nearest node of spline 14790 (not back to its start), then along the rest of it.
        /// </summary>
        /// <remarks>
        /// The movement manager's position, not the entity's: during a chase that lags, and the escape started from where
        /// his descent had ended, so he first walked back to the foot of the stairs (1 Oct 2026).
        /// </remarks>
        private List<Vector3> EscapePath(IUnitEntity unit)
        {
            Vector3 from = unit.MovementManager.GetPosition();
            Vector3[] spline = GetSplineNodes(ArsenaxEscapeSpline);
            List<Vector3> path = [from];
            if (IsAtMeetingSpot(from))
                path.AddRange(ArsenaxStairs);

            int first = 0;
            for (int i = 1; i < spline.Length; i++)
                if (Vector3.Distance(path[^1], spline[i]) < Vector3.Distance(path[^1], spline[first]))
                    first = i;

            path.AddRange(OverGround(path[^1], spline[first]));
            for (int i = first; i < spline.Length; i++)
            {
                path.Add(spline[i]);
                if (i + 1 < spline.Length)
                    path.AddRange(OverGround(spline[i], spline[i + 1]));
            }
            return path;
        }

        /// <summary>
        /// Out of reach: can't be attacked, notices nobody and drops what it was fighting; or back to a normal enemy.
        /// </summary>
        private static void SetOutOfReach(IUnitEntity unit, bool outOfReach)
        {
            if (unit == null)
                return;

            unit.IsInvulnerable = outOfReach;
            unit.SetInRangeCheck(outOfReach ? 0f : AggroRange);
            if (outOfReach)
                unit.ThreatManager.ClearThreatList();
        }

        private static bool IsAtMeetingSpot(IUnitEntity unit)
        {
            return IsAtMeetingSpot(unit.MovementManager.GetPosition());
        }

        private static bool IsAtMeetingSpot(Vector3 position)
        {
            return position.Y > ArsenaxStairs[^1].Y + 1f && Vector3.Distance(position, ArsenaxSpot) < 10f;
        }

        /// <summary>
        /// A speed the client shows as a run: the unit's move speed x 8 (as chasing units) x its model scale. A bigger model
        /// covers more ground per step: Arsenax (scale 1.3) still walked at 8 m/s, the farmers (1.07) run at 8 (HYCREST.md
        /// "How to: make a scripted NPC run").
        /// </summary>
        private static float RunSpeedOf(IUnitEntity unit)
        {
            float scale = unit.CreatureInfo?.Entry.ModelScale ?? 1f;
            if (scale <= 0f)
                scale = 1f;
            float speed = unit.GetPropertyValue(Property.MoveSpeedMultiplier) * 8f * scale;
            return speed > 0f ? speed : 8f;
        }

        private static float PathLength(List<Vector3> path)
        {
            float length = 0f;
            for (int i = 1; i < path.Count; i++)
                length += Vector3.Distance(path[i - 1], path[i]);
            return length;
        }

        /// <summary>
        /// Return a point 1 m in front of <paramref name="position"/> for a unit with <paramref name="rotation"/>, to face it.
        /// </summary>
        private static Vector3 Ahead(Vector3 position, float rotation)
        {
            return position + new Vector3(-MathF.Sin(rotation), 0f, -MathF.Cos(rotation));
        }

        private static Vector3 Flat(Vector3 direction)
        {
            direction.Y = 0f;
            return direction.LengthSquared() > 0.0001f ? Vector3.Normalize(direction) : Vector3.UnitZ;
        }
    }
}
