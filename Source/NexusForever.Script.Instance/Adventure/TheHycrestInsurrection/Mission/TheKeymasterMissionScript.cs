using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Abstract.Entity.Trigger;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.PublicEvent;
using NexusForever.Script.Template.Filter;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// The Keymaster (public event 424, tier 2 Tactical): steal the Dominion's encryption codes from the Field Intelligence
    /// Agent at the military headquarters in Hycrest City, then hide out in Ayita's Tavern.
    /// </summary>
    /// <remarks>
    /// Research and sources: docs research/the-keymaster.md. Two retail videos (B beNOGirhaqs, Y yyr_ysIdYLk), the tables and
    /// Jabbithole. The Field Intelligence Agent (18445) stands at the headquarters (13106) with two agents; Intelligence
    /// Recovery Agents come in two waves, each with a leader (19115/19130, 50676/50677, actionset 315 teleport-in); the
    /// codes (48129, activate 35607) count 209 on their own (ActivateTargetGroup, TG 6731) and 1971 is the tavern's trigger
    /// volume (5725), the mission's last objective, so the regroup is the tavern (<see cref="HycrestMissions.EndsAtHideout"/>).
    /// Vesna's lines in video order: setup 444091 (with the vote outcome, main event), 454818 (Y, 4 s later), 162923 near the
    /// headquarters, 466528 (B, 10 s later), 465442 once the codes are taken.
    /// Inferred: the escort of two, the wave make-up (leader + two each, B), when the waves come (the agent's death or 25 s
    /// into the fight, whichever is first; B shows them after his death, Y a call about 23 s in), the codes appearing where
    /// the agent fell once the fight is over (Y's drop, MMORPG-Life's box), the approach range for 162923, and the payoff
    /// 444066 in the tavern (seen in neither video).
    /// </remarks>
    [ScriptFilterOwnerId(424u)]
    public class TheKeymasterMissionScript : HycrestMissionScript
    {
        private const uint StealCodes = 209u;
        private const uint HideOut    = 1971u;

        private const uint FieldIntelligenceAgent = 18445u;
        private const uint WaveOneAgent           = 19115u;
        private const uint WaveOneLeader          = 19130u;
        private const uint WaveTwoAgent           = 50676u;
        private const uint WaveTwoLeader          = 50677u;
        private const uint EncryptionCodes        = 48129u;
        private const uint VesnaTaranoft          = 17778u;

        private const uint VesnaHint       = 454818u; // communicator: "...steal the codes from the communications officer."
        private const uint VesnaSignal     = 162923u; // communicator: "I've intercepted a unique frequency from a field intelligence agent..."
        private const uint VesnaCrowds     = 466528u; // communicator: "If you are infiltrating the city, stick near groups of citizens..."
        private const uint VesnaGetOut     = 465442u; // communicator: "Good work. Now get out of there before anyone else shows up."
        private const uint VesnaPayoff     = 444066u; // communicator: "With these codes, we can destroy those pesky watchtowers..."

        // Y: the hint 4 s after the setup; B: the crowds line 10 s after the signal
        private static readonly TimeSpan HintDelay   = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan CrowdsDelay = TimeSpan.FromSeconds(10);

        // the military headquarters (13106, r12); the signal comes once a player is this close (Y: 10 s before the fight,
        // B: 24 s; a guess)
        private static readonly Vector3 Headquarters = new(-2400.7f, -868.0f, -1991.0f);
        private const float ApproachRange = 90f;

        // the fight starts when a player comes this close to the agent or one of the enemies is in combat; city enemies
        // don't always notice players on their own (Creature AI caveats), so the script pulls every enemy of the fight in
        private const float AggroRange = 15f;
        private const float PullRange = 45f;

        // the waves: in at the foot of the headquarters' stairs (Y: "reinforcements run down the stairs"; points on the
        // yard's floor, surface.py), leader first. B: wave 2 right after the agent's death, wave 3 later
        private static readonly Vector3[] WaveSpawns =
        [
            new(-2390f, -868.242f, -1966f),
            new(-2386f, -868.242f, -1966f),
            new(-2394f, -868.242f, -1966f)
        ];
        private static readonly uint[][] Waves =
        [
            [WaveOneLeader, WaveOneAgent, WaveOneAgent],
            [WaveTwoLeader, WaveTwoAgent, WaveTwoAgent]
        ];
        private static readonly TimeSpan ReinforcementDelay = TimeSpan.FromSeconds(25);
        private static readonly TimeSpan NextWaveDelay = TimeSpan.FromSeconds(3);

        // the codes: where the agent fell (Y), or the Jabbithole spot of the box (one sighting, on the yard in front of a
        // crate; floor height a guess) if he fell somewhere odd
        private static readonly Vector3 CodesSpot = new(-2398f, -868.242f, -1982f);
        private const float CodesFromHeadquarters = 30f;
        private static readonly TimeSpan CodesRemoveDelay = TimeSpan.FromSeconds(1);

        // Ayita's Tavern (40149, r4, the tavern floor; 24804 is the objective's own point by the door)
        private static readonly Vector3 Tavern = new(-2475.78f, -877.59f, -1968.17f);
        private const uint TriggerId = 114910u;
        private const float TriggerRange = 4f;

        private const double TrackedUnitInterval = 1d;
        private static readonly TimeSpan MissionEndDelay = TimeSpan.FromSeconds(4);

        // tracked by reference: an entity's guid is only assigned once it is actually on the map
        private IUnitEntity agent;
        private readonly List<IUnitEntity> enemies = [];
        private readonly HashSet<IUnitEntity> inCombat = [];
        private IWorldEntity codes;
        private IVolumeGridTriggerEntity trigger;
        private readonly Dictionary<uint, uint> tracked = [];   // guid -> objective, the units marked on the map
        private double trackedTimer;

        private bool signalSent;
        private bool fightStarted;
        private double reinforcementTimer;
        private int wavesCalled;
        private bool waveQueued;
        private Vector3? agentFell;
        private bool codesTaken;
        private bool ending;

        private readonly TimedActionQueue actionQueue = new();

        #region Dependency Injection

        private readonly ILogger<TheKeymasterMissionScript> log;
        private readonly IGameTableManager gameTableManager;
        private readonly IStoryBuilder storyBuilder;
        private readonly ICreatureInfoManager creatureInfoManager;
        private readonly HycrestDialogue dialogue;

        public TheKeymasterMissionScript(
            ILogger<TheKeymasterMissionScript> log,
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

            actionQueue.Enqueue(HintDelay, () => Communicator(VesnaHint));
            log.LogInformation("Hycrest: The Keymaster started.");
        }

        /// <summary>
        /// Invoked when a <see cref="IGridEntity"/> is added to the map the public event is on.
        /// </summary>
        public override void OnAddToMap(IGridEntity entity)
        {
            if (entity is not IUnitEntity unit || !IsOwnEntity(entity))
                return;

            switch (unit.CreatureId)
            {
                case FieldIntelligenceAgent:
                    agent ??= unit;
                    AddEnemy(unit);
                    break;
                case WaveOneAgent:
                case WaveOneLeader:
                case WaveTwoAgent:
                case WaveTwoLeader:
                    AddEnemy(unit);
                    break;
            }
        }

        private void AddEnemy(IUnitEntity unit)
        {
            if (!unit.InCombat)
                unit.Sheathed = true;
            if (!enemies.Contains(unit))
                enemies.Add(unit);
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public override void Update(double lastTick)
        {
            actionQueue.Update(lastTick);

            if (publicEvent.HasFinished)
                return;

            UpdateApproach();
            UpdateFight(lastTick);
            UpdateTrackedUnits(lastTick);
        }

        // --- the way in -------------------------------------------------------------------------------------------------

        /// <summary>
        /// Vesna picks up the agent's signal once a player is near the headquarters (or the fight has started).
        /// </summary>
        private void UpdateApproach()
        {
            if (signalSent)
                return;

            if (!fightStarted && !mapInstance.GetPlayers().Any(p => Vector3.Distance(p.Position, Headquarters) <= ApproachRange))
                return;

            signalSent = true;
            Communicator(VesnaSignal);
            actionQueue.Enqueue(CrowdsDelay, () => Communicator(VesnaCrowds));
        }

        // --- the fight at the headquarters ------------------------------------------------------------------------------

        private void UpdateFight(double lastTick)
        {
            if (codes != null || codesTaken)
                return;

            List<IUnitEntity> alive = enemies.Where(e => e.InWorld && e.IsAlive).ToList();

            foreach (IUnitEntity enemy in alive)
            {
                if (enemy.InCombat && inCombat.Add(enemy))
                    dialogue.BarkCombat(enemy, CombatBark.EnterCombat);
                else if (!enemy.InCombat)
                    inCombat.Remove(enemy);
            }

            if (!fightStarted)
            {
                bool playerNear = agent is { InWorld: true }
                    && mapInstance.GetPlayers().Any(p => p.IsAlive && Vector3.Distance(p.Position, agent.Position) <= AggroRange);
                if (!playerNear && !alive.Any(e => e.InCombat))
                    return;

                fightStarted = true;
                reinforcementTimer = ReinforcementDelay.TotalSeconds;
                log.LogInformation("Hycrest: The Keymaster, the fight at the headquarters started.");
            }

            PullIn(alive);

            // the first wave: the agent's death or the call some time into the fight; the next one once a wave is down
            reinforcementTimer -= lastTick;
            bool agentDown = agent == null || !agent.InWorld || !agent.IsAlive;
            if (wavesCalled == 0 && (agentDown || reinforcementTimer <= 0d))
                CallWave();
            else if (wavesCalled > 0 && wavesCalled < Waves.Length && !waveQueued && alive.All(e => e == agent))
                QueueWave();

            // all of them down: the codes are there to take
            if (agentDown && wavesCalled == Waves.Length && !waveQueued && alive.Count == 0)
                SpawnCodes();
        }

        /// <summary>
        /// Every enemy of the fight that isn't fighting goes for the nearest player in range.
        /// </summary>
        private void PullIn(List<IUnitEntity> alive)
        {
            foreach (IUnitEntity enemy in alive.Where(e => !e.InCombat))
            {
                IPlayer target = mapInstance.GetPlayers()
                    .Where(p => p.IsAlive && Vector3.Distance(p.Position, enemy.Position) <= PullRange)
                    .OrderBy(p => Vector3.Distance(p.Position, enemy.Position))
                    .FirstOrDefault();
                if (target == null)
                    continue;

                enemy.Sheathed = false;
                enemy.ThreatManager.UpdateThreat(target, 1);
            }
        }

        private void QueueWave()
        {
            waveQueued = true;
            actionQueue.Enqueue(NextWaveDelay, () =>
            {
                waveQueued = false;
                CallWave();
            });
        }

        private void CallWave()
        {
            if (publicEvent.HasFinished || wavesCalled >= Waves.Length)
                return;

            uint[] wave = Waves[wavesCalled++];
            for (int i = 0; i < wave.Length; i++)
                SpawnEnemy(wave[i], WaveSpawns[i % WaveSpawns.Length]);

            log.LogInformation($"Hycrest: The Keymaster, wave {wavesCalled} of {Waves.Length} called.");
        }

        private void SpawnEnemy(uint creatureId, Vector3 position)
        {
            ICreatureInfo creatureInfo = creatureInfoManager.GetCreatureInfo(creatureId);
            if (creatureInfo == null)
                return;

            var entity = publicEvent.CreateEntity<INonPlayerEntity>();
            entity.Initialise(creatureInfo);
            var direction = new Vector3(Headquarters.X - position.X, 0f, Headquarters.Z - position.Z);
            entity.Rotation = new Vector3(MathF.Atan2(-direction.X, -direction.Z), 0f, 0f);
            entity.AddToMap(mapInstance, position);
            enemies.Add(entity);
        }

        // --- the codes --------------------------------------------------------------------------------------------------

        private void SpawnCodes()
        {
            ICreatureInfo creatureInfo = creatureInfoManager.GetCreatureInfo(EncryptionCodes);
            if (creatureInfo == null)
                return;

            Vector3 position = agentFell is Vector3 fell && Vector3.Distance(fell, Headquarters) <= CodesFromHeadquarters
                ? fell : CodesSpot;

            var entity = publicEvent.CreateEntity<INonPlayerEntity>();
            entity.Initialise(creatureInfo);
            entity.AddToMap(mapInstance, position);
            codes = entity;
            log.LogInformation("Hycrest: The Keymaster, the fight is over, the codes are there.");
        }

        private void OnCodesTaken()
        {
            if (codesTaken)
                return;

            codesTaken = true;

            // 35607's Despawn (3 s) has no handler in the engine
            IWorldEntity taken = codes;
            codes = null;
            if (taken != null)
                actionQueue.Enqueue(CodesRemoveDelay, () =>
                {
                    if (taken.InWorld)
                        taken.RemoveFromMap();
                });

            Communicator(VesnaGetOut);
            StartHideOut();
            log.LogInformation("Hycrest: The Keymaster, the codes are taken.");
        }

        // --- the tavern -------------------------------------------------------------------------------------------------

        /// <summary>
        /// Hide out in Ayita's Tavern: the whole party, a trigger on the tavern floor.
        /// </summary>
        private void StartHideOut()
        {
            if (publicEvent.HasFinished)
                return;

            mapInstance.PublicEventManager.GetEvent(HycrestPublicEvent.Main)?
                .InvokeScriptCollection<IHycrestMainEventScript>(s => s.PrepareHideout(publicEvent.Id));

            PublicEventObjectiveEntry entry = gameTableManager.PublicEventObjective.GetEntry(HideOut);
            publicEvent.ActivateObjective(HideOut, (uint)Math.Max(1, mapInstance.PlayerCount));

            trigger = publicEvent.CreateEntity<IVolumeGridTriggerEntity>();
            trigger.Initialise(TriggerId, TriggerRange, entry?.ObjectId ?? 0u);
            trigger.AddToMap(mapInstance, Tavern);
        }

        // --- map markers on the units -----------------------------------------------------------------------------------

        private void UpdateTrackedUnits(double lastTick)
        {
            trackedTimer -= lastTick;
            if (trackedTimer > 0d)
                return;
            trackedTimer = TrackedUnitInterval;

            // the agent first, then whoever is left of the fight, then the codes
            List<IWorldEntity> marked = [];
            if (codes is { InWorld: true })
                marked.Add(codes);
            else if (agent is { InWorld: true, IsAlive: true })
                marked.Add(agent);
            else if (!codesTaken)
                marked.AddRange(enemies.Where(e => e.InWorld && e.IsAlive));

            foreach (uint guid in tracked.Keys.Except(marked.Select(m => m.Guid)).ToList())
                Untrack(guid);

            foreach (IWorldEntity entity in marked)
            {
                tracked[entity.Guid] = StealCodes;
                foreach (IPlayer player in mapInstance.GetPlayers())
                    player.Session.EnqueueMessageEncrypted(new ServerMapTrackedUnitUpdate
                    {
                        TrackedUnitId          = entity.Guid,
                        Position               = entity.Position,
                        PublicEventObjectiveId = StealCodes
                    });
            }
        }

        private void Untrack(uint guid)
        {
            tracked.Remove(guid);
            foreach (IPlayer player in mapInstance.GetPlayers())
                player.Session.EnqueueMessageEncrypted(new ServerMapTrackedUnitDisable { TrackedUnitId = guid });
        }

        // --- kills, objectives and the end ------------------------------------------------------------------------------

        /// <summary>
        /// Invoked when a unit on the map has been killed.
        /// </summary>
        public override void OnEntityKilled(IUnitEntity unit)
        {
            if (ending || publicEvent.HasFinished)
                return;

            if (unit is IPlayer)
            {
                IUnitEntity gloat = enemies.FirstOrDefault(e => e.InWorld && e.IsAlive && e.InCombat);
                if (gloat != null)
                    dialogue.BarkCombat(gloat, CombatBark.Kill);
                return;
            }

            if (!IsOwnEntity(unit) || !enemies.Contains(unit))
                return;

            dialogue.BarkCombat(unit, CombatBark.Death);
            inCombat.Remove(unit);
            if (unit == agent)
            {
                agentFell = unit.Position;
                log.LogInformation("Hycrest: The Keymaster, the Field Intelligence Agent is dead.");
            }
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
                case StealCodes:
                    OnCodesTaken();
                    break;
                case HideOut:
                    OnHiddenOut();
                    break;
            }
        }

        private void OnHiddenOut()
        {
            if (ending)
                return;

            ending = true;
            foreach (uint guid in tracked.Keys.ToList())
                Untrack(guid);

            Communicator(VesnaPayoff);
            actionQueue.Enqueue(MissionEndDelay, CompleteMission);
            log.LogInformation("Hycrest: The Keymaster, everyone is in Ayita's Tavern.");
        }

        private void Communicator(uint textId)
        {
            foreach (IPlayer player in mapInstance.GetPlayers())
                storyBuilder.SendStoryCommunicator(textId, VesnaTaranoft, player);
        }

        /// <summary>
        /// Invoked when the hideout's door closes after the mission: the units are gone, forget them.
        /// </summary>
        public override void OnHideoutClosed()
        {
            base.OnHideoutClosed();
            enemies.Clear();
            inCombat.Clear();
            agent   = null;
            codes   = null;
            trigger = null;
        }
    }
}
