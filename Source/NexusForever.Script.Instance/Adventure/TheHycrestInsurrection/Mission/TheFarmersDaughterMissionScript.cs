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
    public class TheFarmersDaughterMissionScript : HycrestFieldMissionScript
    {
        private const uint SpeakWithTarquim = 1731u;
        private const uint RescueCaptives   = 201u;

        private const uint Tarquim        = 17773u;
        private const uint Millithea      = 49490u;
        private const uint Prema          = 17772u;
        private const uint PredatorDrone  = 51026u;
        private const uint Responsebot    = 18509u;
        private const uint AyitaSinnatus  = 48032u;

        private const uint TarquimPlea         = 438721u; // "Help! Please, you must! My daughter, Prema, and our milk maid..."
        private const uint AyitaHurry          = 454699u; // communicator: "Something tells me that if you don't hurry..."
        private const uint MillitheaAfraid     = 464006u; // "I don't want to be eaten!"
        private const uint MillitheaThanks     = 446755u; // "Thank you, but Prema's still in danger!..."
        private const uint PremaStopThis       = 450655u; // "Stop this!"
        private const uint PremaEeep           = 464067u; // "Eeep!"
        private const uint PremaThanks         = 447119u; // "Oh, thank you so much!..."
        private const uint AyitaMissionPayoff  = 444044u; // communicator: "Prema's home safe and sound..."

        private const uint TarquimLocation = 13041u;

        /// <summary>
        /// Where the captives are: retail places Millithea (with her drones) and Prema (with the Responsebot) at one of
        /// three spots per run, everything else is shared. Each layout has its own phases (SQL) and map markers (the table
        /// points next to them: Millithea 39382/39383/39384 are consecutive ids at her three spots, retail's own points;
        /// layout C was confirmed by the archived Jabbithole NPC positions).
        /// </summary>
        private record Layout(string Name, uint MillitheaPhase, uint PremaPhase, uint MillitheaLocation, uint PremaLocation);

        private static readonly Layout[] Layouts =
        [
            new("A", 10u, 20u, 39383u, 40050u),
            new("B", 11u, 21u, 39384u, 38652u),
            new("C", 12u, 22u, 39382u, 38650u)
        ];

        private Layout layout;

        // captives call out when a player comes this close
        private const float CaptiveCallRange = 20f;


        // the alarm (retail videos): every 5th kill of the mission's Dominion units summons a Recon Specialist, who calls
        // the Rapid Response Team; he appears this far from the fallen unit, on the side away from the nearest player, and
        // runs in (see HycrestAlarm)
        private const int AlarmKills = 5;
        private const float AlarmSpawnDistance = 35f;

        private static readonly TimeSpan MissionEndDelay = TimeSpan.FromSeconds(4);


        private uint tarquimGuid;
        private uint millitheaGuid;
        private uint premaGuid;
        private uint responsebotGuid;
        private readonly HashSet<uint> millitheaGuards = [];

        private bool millitheaCalled;
        private bool millitheaFree;
        private bool millitheaThanked;
        private bool premaCalled;
        private bool premaFree;
        private bool premaThanked;
        private bool ending;

        private readonly TimedActionQueue actionQueue = new();

        private HycrestAlarm alarm;
        private int kills;

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
            : base(log, gameTableManager, spellParametersFactory)
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

            // the layout: 50/50 per run (retail's deciding factor is unknown, the videos show both); the phase spawns
            // Millithea and her drones
            layout = Layouts[Random.Shared.Next(Layouts.Length)];
            log.LogInformation($"Hycrest: The Farmer's Daughter, layout {layout.Name}.");
            publicEvent.SetPhase(layout.MillitheaPhase);

            // map area and minimap marker: the objectives have no WorldLocation2 in the tables; these are the table points
            // next to Tarquim and the captives
            publicEvent.SetObjectiveLocations(SpeakWithTarquim, TarquimLocation);
            publicEvent.SetObjectiveLocations(RescueCaptives, layout.MillitheaLocation);

            alarm = new HycrestAlarm(log, publicEvent, mapInstance, creatureInfoManager, spellParametersFactory,
                storyBuilder, dialogue);
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

            // scouts and spotlights: shared with Breach of Protocol (HycrestFieldMissionScript)
            if (OnAddFieldUnit(worldEntity))
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
            }
        }


        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public override void Update(double lastTick)
        {
            actionQueue.Update(lastTick);

            // the enemies stay in the fields after the mission (KeepAfterMission) and go on patrolling and watching
            LinkAggro(millitheaGuards);
            UpdateWalks(lastTick);
            UpdateSpotlights(lastTick);

            if (publicEvent.HasFinished)
                return;

            UpdateMillithea();
            UpdatePrema();
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
        }

        /// <summary>
        /// Invoked when a unit on the map has been killed: every 5th of the mission's Dominion units sets off the alarm.
        /// </summary>
        public override void OnEntityKilled(IUnitEntity unit)
        {
            if (ending || publicEvent.HasFinished || !IsOwnEntity(unit) || unit is IPlayer)
                return;

            // the alarm's own units and the spotlights don't count; neither do friendly units
            if (HycrestAlarm.IsAlarmUnit(unit.CreatureId) || unit.CreatureId == SpotlightTarget || (uint)unit.Faction1 != HostileFaction)
                return;

            kills++;
            if (kills % AlarmKills != 0 || alarm.IsActive)
                return;

            IPlayer target = mapInstance.GetPlayers()
                .Where(p => p.IsAlive)
                .OrderBy(p => Vector3.Distance(p.Position, unit.Position))
                .FirstOrDefault();
            if (target == null)
                return;

            Vector3 away = unit.Position - target.Position;
            away.Y = 0f;
            Vector3 position = away.LengthSquared() > 0.01f
                ? unit.Position + Vector3.Normalize(away) * AlarmSpawnDistance
                : unit.Position + new Vector3(AlarmSpawnDistance, 0f, 0f);

            log.LogInformation($"Hycrest: {kills} kills, alarm.");
            alarm.Trigger(position, target);
        }


        private bool IsAlive(uint guid)
        {
            return mapInstance.GetEntity<IWorldEntity>(guid) is IUnitEntity unit && unit.IsAlive;
        }

        private bool IsPlayerNear(Vector3 position, float range)
        {
            return mapInstance.GetPlayers().Any(p => Vector3.Distance(p.Position, position) <= range);
        }

        // enemies of the adventure's hostile faction stay after the mission until the hideout's barn closes; the
        // captives, Tarquim and the alarm's flare and drill go right away
        private const uint HostileFaction = 1452u;

        /// <summary>
        /// Invoked when the hideout's door closes after the mission: the guards are gone, forget their guids (reused).
        /// </summary>
        public override void OnHideoutClosed()
        {
            base.OnHideoutClosed();
            millitheaGuards.Clear();
        }

        /// <summary>
        /// Return true if <paramref name="entity"/> stays in the world after the mission.
        /// </summary>
        protected override bool KeepAfterMission(IGridEntity entity)
        {
            return entity is IUnitEntity unit && (uint)unit.Faction1 == HostileFaction;
        }

        /// <summary>
        /// Invoked when the public event enters a phase.
        /// </summary>
        /// <remarks>
        /// Dev: setting the other layout's Millithea phase (e.g. "map eventphase 420 11") switches the layout; best right
        /// after the mission started, before Millithea is freed.
        /// </remarks>
        public override void OnPublicEventPhase(uint phase)
        {
            Layout requested = Layouts.FirstOrDefault(l => l.MillitheaPhase == phase);
            if (layout == null || requested == null || requested == layout || millitheaFree)
                return;

            // the old layout's captives and guards go; the new phase's entities are only queued to be added yet
            foreach (IGridEntity entity in publicEvent.GetEntities().ToList())
                if (entity is IWorldEntity { InWorld: true } worldEntity
                    && worldEntity.CreatureId is Millithea or PredatorDrone or Prema or Responsebot)
                    worldEntity.RemoveFromMap();

            millitheaGuards.Clear();
            millitheaGuid   = 0u;
            millitheaCalled = false;

            layout = requested;
            publicEvent.SetObjectiveLocations(RescueCaptives, layout.MillitheaLocation);
            log.LogInformation($"Hycrest: The Farmer's Daughter, switched to layout {layout.Name}.");
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

                // one captive at a time: Prema and her guard appear now, and the map points to her
                publicEvent.SetPhase(layout.PremaPhase);
                publicEvent.SetObjectiveLocations(RescueCaptives, layout.PremaLocation);

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
