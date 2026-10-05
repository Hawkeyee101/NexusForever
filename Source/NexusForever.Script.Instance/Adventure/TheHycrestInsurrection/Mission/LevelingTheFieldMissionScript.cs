using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.GameTable;
using NexusForever.Script.Template.Filter;
using NexusForever.Shared;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// Leveling the Field (public event 422, tier 1 Militant): steal shield parts from the Dominion's supply spots in the
    /// fields and carry them, one at a time, to Lysion's men in the grain silos. Ten deliveries.
    /// </summary>
    /// <remarks>
    /// Research and sources: docs research/leveling-the-field.md. The crates (48056) are spawned here at the 13 archived
    /// Jabbithole spots; stealing one (49499, 3 s) gives the player Carrying Shield Parts (57748, kept while in the event:
    /// caster persistence 23646, prerequisite type 117). A carrier who walks into a silo delivers (objective 1732). Teun's
    /// retail video check (3 Oct 2026): 10 deliveries; walking over a spot in the silo delivers (no talking); stolen crates
    /// open, go and don't come back; markers on the crate clusters and the silos; a Recon Specialist always stands in the
    /// fields (not the kill-count alarm) and Vesna warns about the sharpshooters when he snipes; Lysion's lines 465552,
    /// 444107, 454976, 444054; the regroup at Sinnatus's Barn. Inferred: keeping the parts on death, which farmer line
    /// plays when, the open state (State1).
    /// </remarks>
    [ScriptFilterOwnerId(422u)]
    public class LevelingTheFieldMissionScript : HycrestFieldMissionScript, ILevelingTheFieldMissionScript
    {
        private const uint StealShieldParts = 191u;
        private const uint ReturnParts      = 1732u;

        private const uint ShieldParts    = 48056u;
        private const uint RebelFarmer    = 50713u;
        private const uint LysionSinnatus = 17777u;
        private const uint VesnaTaranoft  = 17778u;
        private const uint ReconSpecialist = 17823u;

        private const uint CarryingShieldParts = 57748u;

        private const uint LysionHint   = 454976u; // communicator: "If you can raid their supplies and haul enough shield parts..."
        private const uint LysionPayoff = 444054u; // communicator: "You struck a blow for the rebellion today..."
        private const uint VesnaSnipers = 161849u; // communicator: "Watch out for the sharpshooters nested in that watchtower..."

        // the standing Recon Specialist: Vesna's warning when he opens fire, his call for reinforcements a moment later
        private static readonly TimeSpan SpecialistCallDelay = TimeSpan.FromSeconds(4);

        // the Rebel Farmers' gossip (set 1257, only to a player carrying parts, 5 s cooldown): the first three when a
        // carrier comes near, "just add it to the pile" at the delivery
        private static readonly uint[] FarmerGreetings = [459873u, 459871u, 459870u]; // "No one followed you, right?" / "...when their shields fail." / "This'll show the military..."
        private const uint FarmerDelivery = 459869u; // "That's perfect. Just add it to the pile."
        private const double FarmerGossipCooldown = 5d;
        private const float FarmerGreetRange = 20f;

        // the supply spots (13114 south-west, 13152 middle, 13115 east) and the silos (38681 Checkpoint Gamma, 38682 Abandoned
        // Orchards, 38683 Sunripe Fields)
        private static readonly uint[] SupplyLocations = [13114u, 13152u, 13115u];
        private static readonly uint[] SiloLocations   = [38681u, 38682u, 38683u];

        // silo floors (the r10 points of 38681-38683): a carrier this close delivers
        private static readonly Vector3[] Silos =
        [
            new(-2286.6f, -923.2f, -1267.5f),
            new(-2442.1f, -925.2f, -1224.3f),
            new(-2483.2f, -925.7f, -1440.8f)
        ];
        private const float SiloRadius = 10f;
        private const float SiloHeight = 6f;

        // crate spots: the archived Jabbithole Shield Parts sightings (13 fixed spots in three tight clusters, merged over
        // many runs; nothing points to random spots), floor heights from surface.py. The sighting at (-2305, -1357) is on
        // a medical station whose uneven top left the crate floating (Teun's test, 4 Oct 2026): moved 3 m north to the
        // ground
        private static readonly Vector3[] CrateSpots =
        [
            // south-west (13114)
            new(-2441f, -923.0f, -1403f), new(-2436f, -922.0f, -1388f), new(-2435f, -922.75f, -1405f), new(-2430f, -922.0f, -1402f),
            // middle (13152)
            new(-2382f, -922.5f, -1302f), new(-2376f, -924.0f, -1279f), new(-2374f, -923.5f, -1293f), new(-2362f, -924.0f, -1290f),
            // east (13115)
            new(-2330f, -923.0f, -1338f), new(-2317f, -925.75f, -1312f), new(-2305f, -923.0f, -1354f), new(-2299f, -923.75f, -1345f),
            new(-2299f, -925.125f, -1327f)
        ];

        // a stolen crate opens (Teun's video: closed before, open after; none came back) and goes 4 s later, as Stealing's
        // DespawnUnit says (no handler in the engine). It can't stay: InteractionBlocked doesn't stop an activate cast, so
        // the client kept offering the open crate and a second steal on it gave nothing (Teun's test, 3 Oct 2026). The open
        // state is a guess
        private const StandState CrateOpen = StandState.State1;
        private static readonly TimeSpan CrateRemoveDelay = TimeSpan.FromSeconds(4);

        // which crate spots belong to which supply spot (SupplyLocations): a spot's marker goes once its crates are all taken
        private static readonly int[] CrateCluster = [0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 2];

        private static readonly TimeSpan HintDelay = TimeSpan.FromSeconds(20);
        private const int HideoutAtDelivery = 5;

        private const uint HostileFaction = 1452u;

        private static readonly TimeSpan MissionEndDelay = TimeSpan.FromSeconds(4);

        // tracked by reference: an entity's guid is only assigned once it is actually on the map
        private readonly Dictionary<IWorldEntity, int> crates = [];
        private IUnitEntity specialist;
        private bool specialistEngaged;
        private readonly List<IWorldEntity> farmers = [];
        private readonly double[] siloGossip = new double[3];
        private readonly HashSet<ulong> greeted = [];
        private int deliveries;
        private bool siloMarkers;
        private bool hideoutPrepared;
        private bool ending;

        private readonly TimedActionQueue actionQueue = new();
        private HycrestAlarm alarm;

        #region Dependency Injection

        private readonly ILogger<LevelingTheFieldMissionScript> log;
        private readonly IStoryBuilder storyBuilder;
        private readonly IFactory<ISpellParameters> spellParametersFactory;
        private readonly ICreatureInfoManager creatureInfoManager;
        private readonly HycrestDialogue dialogue;

        public LevelingTheFieldMissionScript(
            ILogger<LevelingTheFieldMissionScript> log,
            IGameTableManager gameTableManager,
            IStoryBuilder storyBuilder,
            IFactory<ISpellParameters> spellParametersFactory,
            ICreatureInfoManager creatureInfoManager)
            : base(log, gameTableManager, spellParametersFactory)
        {
            this.log                    = log;
            this.storyBuilder           = storyBuilder;
            this.spellParametersFactory = spellParametersFactory;
            this.creatureInfoManager    = creatureInfoManager;
            dialogue = new HycrestDialogue(gameTableManager, actionQueue);
        }

        #endregion

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public override void OnLoad(IPublicEvent owner)
        {
            base.OnLoad(owner);

            publicEvent.SetObjectiveLocations(StealShieldParts, SupplyLocations);
            // the silos' markers only while someone carries parts (Teun, 4 Oct 2026)
            publicEvent.SetObjectiveLocations(ReturnParts);
            alarm = new HycrestAlarm(log, publicEvent, mapInstance, creatureInfoManager, spellParametersFactory,
                storyBuilder, dialogue);

            for (int i = 0; i < CrateSpots.Length; i++)
                SpawnCrate(i);

            actionQueue.Enqueue(HintDelay, () => Communicator(LysionHint, LysionSinnatus));
            log.LogInformation("Hycrest: Leveling the Field started.");
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

            if (worldEntity.CreatureId == RebelFarmer)
                farmers.Add(worldEntity);
            else if (worldEntity.CreatureId == ReconSpecialist && worldEntity is IUnitEntity recon && specialist == null)
                specialist = recon;
        }

        /// <summary>
        /// Invoked when a creature's AI picks a script-handled spell: a Recon Specialist's Call Reinforcements from the
        /// creature spell list goes to this mission's alarm (once per specialist, not while the alarm is running).
        /// </summary>
        public override void OnCreatureSpell(IUnitEntity caster, IUnitEntity target, uint spell4Id)
        {
            if (!publicEvent.HasFinished)
                alarm.OnCreatureSpell(caster, target, spell4Id);
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public override void Update(double lastTick)
        {
            actionQueue.Update(lastTick);
            UpdateWalks(lastTick);
            UpdateSpotlights(lastTick);

            if (publicEvent.HasFinished)
                return;

            for (int i = 0; i < siloGossip.Length; i++)
                siloGossip[i] -= lastTick;

            UpdateCarriers();
            UpdateSpecialist();
            alarm.Update(lastTick);
        }

        // --- crates -----------------------------------------------------------------------------------------------------

        private void SpawnCrate(int spot)
        {
            if (publicEvent.HasFinished)
                return;

            ICreatureInfo creatureInfo = creatureInfoManager.GetCreatureInfo(ShieldParts);
            if (creatureInfo == null)
                return;

            var crate = publicEvent.CreateEntity<INonPlayerEntity>();
            crate.Initialise(creatureInfo);
            crate.Rotation = new Vector3((float)(Random.Shared.NextDouble() * 2d * Math.PI - Math.PI), 0f, 0f);
            crate.AddToMap(mapInstance, CrateSpots[spot]);
            crates[crate] = spot;
        }

        /// <summary>
        /// <paramref name="player"/> stole the parts in <paramref name="crate"/>.
        /// </summary>
        public void OnPartsStolen(IWorldEntity crate, IPlayer player)
        {
            if (ending || publicEvent.HasFinished)
                return;

            if (!crates.TryGetValue(crate, out int spot))
            {
                log.LogInformation($"Hycrest: Leveling the Field, {player.Name} stole from a crate that was already taken, nothing.");
                return;
            }

            // one part at a time: retail's crates only glow for a player without parts (anim-state prerequisite 22044, which
            // the server doesn't check), so a carrier's steal does nothing and the crate stays
            if (player.HasSpell(CarryingShieldParts, out ISpell _))
            {
                log.LogInformation($"Hycrest: Leveling the Field, {player.Name} already carries shield parts, the crate stays.");
                return;
            }

            crate.InteractionBlocked = true;
            crates.Remove(crate);
            HycrestDropShip.SetState(crate, CrateOpen);
            actionQueue.Enqueue(CrateRemoveDelay, () =>
            {
                if (crate.InWorld)
                    crate.RemoveFromMap();
            });

            // the supply spot's marker goes once all its crates are taken, the last one too
            publicEvent.SetObjectiveLocations(StealShieldParts,
                [.. crates.Values.Select(i => CrateCluster[i]).Distinct().OrderBy(i => i).Select(i => SupplyLocations[i])]);

            ISpellParameters parameters = spellParametersFactory.Resolve();
            parameters.PrimaryTargetId        = player.Guid;
            parameters.UserInitiatedSpellCast = false;
            player.CastSpell(CarryingShieldParts, parameters);
            greeted.Remove(player.CharacterId);
            log.LogInformation($"Hycrest: Leveling the Field, {player.Name} stole shield parts (spot {spot}).");
        }

        // --- deliveries -------------------------------------------------------------------------------------------------

        private void UpdateCarriers()
        {
            bool anyCarrier = false;
            foreach (IPlayer player in mapInstance.GetPlayers())
            {
                if (!player.IsAlive || !player.HasSpell(CarryingShieldParts, out ISpell _))
                    continue;

                anyCarrier = true;

                int silo = NearestSilo(player.Position, out float distance);
                if (silo < 0)
                    continue;

                if (distance <= SiloRadius && MathF.Abs(player.Position.Y - Silos[silo].Y) <= SiloHeight)
                    Deliver(player, silo);
                else if (distance <= FarmerGreetRange && greeted.Add(player.CharacterId))
                    FarmerSays(silo, FarmerGreetings[Random.Shared.Next(FarmerGreetings.Length)]);
            }

            if (anyCarrier != siloMarkers && !ending)
            {
                siloMarkers = anyCarrier;
                publicEvent.SetObjectiveLocations(ReturnParts, anyCarrier ? SiloLocations : []);
            }
        }

        private void Deliver(IPlayer player, int silo)
        {
            if (!player.HasSpell(CarryingShieldParts, out ISpell carrying))
                return;

            carrying.Finish();
            deliveries++;
            siloGossip[silo] = 0d;
            FarmerSays(silo, FarmerDelivery);
            publicEvent.UpdateObjective(ReturnParts, 1);
            log.LogInformation($"Hycrest: Leveling the Field, {player.Name} delivered shield parts ({deliveries}).");

            // the rebels go ahead to Sinnatus's Barn for the regroup
            if (!hideoutPrepared && deliveries >= HideoutAtDelivery)
            {
                hideoutPrepared = true;
                mapInstance.PublicEventManager.GetEvent(HycrestPublicEvent.Main)?
                    .InvokeScriptCollection<IHycrestMainEventScript>(s => s.PrepareHideout(publicEvent.Id));
            }
        }

        /// <summary>
        /// One of the Rebel Farmers in <paramref name="silo"/> says <paramref name="textId"/>, unless one spoke there just now.
        /// </summary>
        private void FarmerSays(int silo, uint textId)
        {
            if (siloGossip[silo] > 0d)
                return;

            IWorldEntity farmer = farmers
                .Where(f => f.InWorld && Vector3.Distance(f.Position, Silos[silo]) <= SiloRadius)
                .OrderBy(_ => Random.Shared.Next())
                .FirstOrDefault();
            if (farmer == null)
                return;

            siloGossip[silo] = FarmerGossipCooldown;
            dialogue.Bark(farmer, textId);
        }

        private static int NearestSilo(Vector3 position, out float distance)
        {
            int nearest = -1;
            distance = float.MaxValue;
            for (int i = 0; i < Silos.Length; i++)
            {
                float d = Vector2.Distance(new Vector2(position.X, position.Z), new Vector2(Silos[i].X, Silos[i].Z));
                if (d < distance)
                {
                    distance = d;
                    nearest  = i;
                }
            }
            return nearest;
        }

        /// <summary>
        /// The Recon Specialist always stands in the fields here: when he opens fire Vesna warns about the sharpshooters,
        /// then he calls reinforcements (no alarm every few kills in this mission).
        /// </summary>
        private void UpdateSpecialist()
        {
            if (specialistEngaged || specialist is not { InWorld: true, IsAlive: true, InCombat: true })
                return;

            specialistEngaged = true;
            Communicator(VesnaSnipers, VesnaTaranoft);

            IUnitEntity recon = specialist;
            actionQueue.Enqueue(SpecialistCallDelay, () =>
            {
                if (!recon.InWorld || !recon.IsAlive || publicEvent.HasFinished)
                    return;

                IPlayer target = (recon.TargetGuid.HasValue ? mapInstance.GetEntity<IPlayer>(recon.TargetGuid.Value) : null)
                    ?? mapInstance.GetPlayers().Where(p => p.IsAlive).OrderBy(p => Vector3.Distance(p.Position, recon.Position)).FirstOrDefault();
                if (target != null)
                    alarm.TriggerWith(recon, target);
            });
        }

        // --- kills, objectives and the end ------------------------------------------------------------------------------

        /// <summary>
        /// Invoked when the status of an objective changes.
        /// </summary>
        public override void OnPublicEventObjectiveStatus(IPublicEventObjective objective)
        {
            if (objective.Status != PublicEventStatus.Succeeded || objective.Entry.Id != ReturnParts || ending)
                return;

            ending = true;
            foreach (IPlayer player in mapInstance.GetPlayers())
                if (player.HasSpell(CarryingShieldParts, out ISpell carrying))
                    carrying.Finish();

            Communicator(LysionPayoff, LysionSinnatus);
            actionQueue.Enqueue(MissionEndDelay, CompleteMission);
            log.LogInformation("Hycrest: Leveling the Field, all shield parts delivered.");
        }

        private void Communicator(uint textId, uint creatureId)
        {
            foreach (IPlayer player in mapInstance.GetPlayers())
                storyBuilder.SendStoryCommunicator(textId, creatureId, player);
        }

        /// <summary>
        /// Invoked when the hideout's door closes after the mission: the units are gone, forget them.
        /// </summary>
        public override void OnHideoutClosed()
        {
            base.OnHideoutClosed();
            crates.Clear();
            farmers.Clear();
            specialist = null;
        }

        /// <summary>
        /// Return true if <paramref name="entity"/> stays in the world after the mission: the Dominion's field units, until
        /// the hideout's barn closes. Crates and farmers go at once.
        /// </summary>
        protected override bool KeepAfterMission(IGridEntity entity)
        {
            return entity is IUnitEntity unit && (uint)unit.Faction1 == HostileFaction;
        }
    }
}
