using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Combat.CrowdControl;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Entity.Movement.Command.Mode;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.Game.Static.Reputation;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;
using NexusForever.Script.Template.Filter;
using NexusForever.Shared;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// All Aboard (public event 432, finale of the Merciful track): stop the train carrying the farmers away, then hold off
    /// the Dominion until Arsenax's warship gives up.
    /// </summary>
    /// <remarks>
    /// Retail video (Teun, 1 Oct 2026), research in docs/hycrest/research/all-aboard.md:
    /// <list type="number">
    /// <item>Talk to Ayita in the Bell Farmhouse (1975). The train sets off: a locomotive with 11 guards (8 Suppression
    /// Specialists, 2 Suppression Snipers and a Tank Gun on its roof) and two cars with cages of farmers.</item>
    /// <item>Plant the bomb (190, 180 s, the timer yellow): kill the guards (2301), pull the lever at the back (the two
    /// force fields around the drive system drop) and plant the bomb at the drive system.</item>
    /// <item>"Wait for Ayita to spring the trap" (1887, the same countdown, green): the train drives on to the end of its
    /// route and explodes there. Without a bomb, the attempt starts over from just after talking to Ayita.</item>
    /// <item>Arsenax from orbit; three waves (1868: two Guardbots, two Guardbots, the Suppressionbot Mk. II) inside a
    /// round force field; then his orbital bombardment for ~30 s, until the ship's fail-safes kick in.</item>
    /// <item>The farmers run in and celebrate; the end scene in Hycrest church comes later (not built yet).</item>
    /// </list>
    /// The train: the locomotive and each car walk their own copy of the route (spline 14981, the longest of the three
    /// identical routes), the cars shifted back, at the speed that brings them to their end point in the same 179.7 s as
    /// the locomotive (4508's own duration). Everything on them rides as a platform passenger (guards, lever, drive system,
    /// force fields, cages, farmers): they stand still on board, and the engine recomputes a passenger's server position
    /// from its platform every tick (a static position command never finishes), so aggro, spell and interaction ranges
    /// hold. First test (Teun, 1 Oct 2026): players can stand on the moving locomotive.
    /// </remarks>
    [ScriptFilterOwnerId(432u)]
    public class AllAboardMissionScript : HycrestMissionScript, IAllAboardMissionScript
    {
        // objectives
        private const uint TalkToAyita   = 1975u;
        private const uint KillGuards    = 2301u;
        private const uint PlantBomb     = 190u;
        private const uint SpringTrap    = 1887u;
        private const uint DefeatWaves   = 1868u;
        private const uint BellFarmhouse = 39450u;   // WorldLocation2, 1975's own point

        // the train's map marker (Teun): 190 has no location, so it follows the locomotive over the WorldLocation2 points
        // that lie along the route (within ~15 m), switching to the nearest every few seconds
        private static readonly (uint Id, Vector2 Position)[] RouteMarkers =
        [
            (40399u, new(-2536f, -1555f)), (39432u, new(-2458f, -1490f)), (13200u, new(-2390f, -1460f)),
            (39416u, new(-2381f, -1444f)), (39433u, new(-2325f, -1444f)), (13143u, new(-2315f, -1453f))
        ];
        private static readonly TimeSpan MarkerInterval = TimeSpan.FromSeconds(5);
        private double markerTimer;
        private uint trainMarker;

        // the finale's theme from the talk with Ayita to the end of the fight (Teun, 2 Oct 2026): "Hycrest Adventure - Mil &
        // Mer Final Music" (56690, a Fluff aura on the player; 56691 is the Tactical finale's, 56693 the Exile victory)
        private const uint FinaleMusic = 56690u;
        // retail plays the WildStar Main Theme (Teun): 56690's zone kit 623 is "Play_Dominion_Brass_ONLY", the main theme
        // (Play_WildStar_Main_Theme 43855, music set 172) is in no spell or visual, only zone kits 619/840. Off until a way
        // to play a sound event is found (!story sound 43855 test)
        private const bool PlayFinaleMusic = false;

        // speakers (communicator portraits)
        private const uint AyitaSinnatus  = 48032u;
        private const uint VesnaTaranoft  = 17778u;
        private const uint ArsenaxSeverus = 17928u;
        private const uint MajorRhadman   = 48613u;

        // lines (order from Teun's screenshots, 1 Oct 2026)
        private const uint AyitaBriefing    = 454715u; // "We've still got a chance to save those folks..."
        private const uint AyitaTrain       = 461071u; // "There's the transport! You'll need to take out the shields..."
        private const uint AyitaShieldsDown = 461072u; // "The shields are down, but it won't last long!..."
        private const uint AyitaBombPlanted = 461073u; // "Get ready. When that transport reaches the fields, I'm gonna blow it to pieces!..."
        private const uint AyitaTooLate     = 447577u; // "No! The transport's passed through the fields! It's too late to save them now."
        private const uint AyitaBombsAway   = 461076u; // "Bombs away! Now's our chance to get those folks outta there!"
        private const uint VesnaWarship     = 461082u; // "I do not mean to dampen your spirits, but there is a large military vessel in orbit..."
        private const uint ArsenaxAzrion    = 461084u; // "This is General Arsenax aboard the Dominion warship, Azrion..."
        private const uint AyitaNoSurrender = 461092u; // "No way are we surrendering!..."
        private const uint ArsenaxUglyOne   = 461061u; // "No, no, no! Aim for the ugly one!"
        private const uint ArsenaxBatteries = 466392u; // "Open all batteries, fire everything! Kill them all! ..."
        private const uint RhadmanFailSafes = 466500u; // "Sir, the ship's fail-safes have initialized..."
        private const uint ArsenaxCannotStop = 466499u; // "No! We cannot stop now! Find me something, anything, to shoot at them!"
        private const uint AyitaWeDidIt     = 466306u; // "We did it! Oh, I can't thank you enough for your help!"
        private const uint AyitaTechnical   = 466307u; // "Looks like General Arsenax has run into some technical difficulties up there..."
        private const uint AyitaHighborns   = 466308u; // "By the time he returns, we'll all be long gone..."
        private static readonly uint[] ArsenaxStrikeLines = [461056u, 461057u, 461058u, 461059u, 461060u, 461062u];

        // the train (models measured with Surveyor's m3 reader: the locomotive 16 x 14 x 42 m, front at -Z; a flatbed
        // 17 x 46 m, deck 2.8 m; a cage 9 x 15 m)
        private const uint Locomotive      = 17741u; // Population Enforcement Carrier (Platform)
        private const uint Flatbed         = 48971u; // Population Relocation Carrier (Platform)
        private const uint Cage            = 48972u; // Population Carrier (Platform)
        private const uint DriveSystem     = 17939u; // PEC Drive System (activate 50068 "Planting Bomb", 2 s)
        private const uint ForceField      = 48890u; // Force Field (Platform)
        private const uint Lever           = 50775u; // Force Field Lever (activate 58497 "Pulling Lever")
        private const uint TankGun         = 19110u;
        private const uint Sniper          = 26360u;
        private const uint Specialist      = 17742u;
        private static readonly uint[] DetainedFarmers = [17992u, 17993u, 17994u, 17995u];

        private const ushort RouteSpline = 14981;            // the route, 102 m longer at its start than 4508
        private const float TrainSpeed = 2f;                 // m/s, the splines' own pace
        private static readonly TimeSpan TrainTime = TimeSpan.FromSeconds(179.7);   // 4508: start to the checkpoint
        private const float CarSpacing = 46.7f;              // centre to centre: half a locomotive + half a flatbed + 2 m
        private const float PathStep = 8f;                   // metres between the sampled points of a car's path (Catmull-Rom)

        // TEMPORARY dev aid (Teun, 1 Oct 2026): the train stands still at its end spot, everything on board, so the places
        // on it can be measured with !entity info (the script converts them to the locomotive's frame). false = normal
        private const bool DevParkTrain = false;

        // places on the locomotive (its own frame: x across, y up from its origin, z along, front at -Z)
        private static readonly Vector3 DriveSystemSpot = new(-0.05f, 3.83f, -9.10f);   // measured, inside at the front
        private const float DriveSystemYaw = -0.131f;   // measured 1.440 was 90 degrees counter-clockwise off (Teun)
        // measured in game on the parked train (Teun, 1 Oct 2026, !entity info), turned into the locomotive's frame
        private static readonly Vector3 LeverSpot       = new(-0.06f, 6.06f, 12.95f);
        private const float LeverYaw = -1.669f;
        // measured (Teun, 1 Oct 2026): across the side doorways in the middle; turned to stand along the train
        private static readonly (Vector3 Spot, float Yaw)[] ForceFieldSpots =
        [
            (new Vector3(-5.16f, 3.83f, -0.17f), MathF.PI / 2f),
            (new Vector3( 5.33f, 3.82f, -0.18f), MathF.PI / 2f)
        ];
        private static readonly (uint Creature, Vector3 Spot, float Yaw)[] Guards =
        [
            // inside, by the force fields (measured), facing out through them
            (Specialist, new Vector3(-4.20f, 3.83f, -1.45f), MathF.PI / 2f), (Specialist, new Vector3(-4.11f, 3.83f, 1.04f), MathF.PI / 2f),
            (Specialist, new Vector3( 4.80f, 3.83f, -1.37f), -MathF.PI / 2f), (Specialist, new Vector3( 4.69f, 3.83f, 1.27f), -MathF.PI / 2f),
            // at the back, around the lever (measured)
            (Specialist, new Vector3(-3.21f, 6.06f, 13.28f), -2.677f), (Specialist, new Vector3(-1.21f, 6.06f, 15.10f), -2.036f),
            (Specialist, new Vector3( 1.07f, 6.06f, 14.97f), -1.005f), (Specialist, new Vector3( 2.98f, 6.06f, 13.40f), -0.434f),
            // on the roof (measured): the Tank Gun at the front, facing ahead (it only fires ahead), the snipers mid-roof
            (TankGun, new Vector3(0.01f, 11.98f, -10.14f), 0f),
            (Sniper, new Vector3(-2.01f, 11.98f, 0.83f), 3.061f), (Sniper, new Vector3(2.26f, 11.97f, 0.42f), -0.136f)
        ];
        // on a flatbed (its own frame): two cages with four Detained Farmers each (archived Jabbithole sightings where the
        // train stops cluster on these four cage spots, up to 5 spots ~6 m along a cage)
        private static readonly Vector3[] CageSpots = [new(0f, 2.8f, -10f), new(0f, 2.8f, 10f)];
        private static readonly Vector3[] CagedFarmerOffsets =
        [
            new(-1.8f, 0.8f, -3f), new(1.8f, 0.8f, -1f), new(-1.8f, 0.8f, 1.5f), new(1.8f, 0.8f, 3f)
        ];

        // the guards' attacks, scripted (Teun, 2 Oct 2026: "adds a lot of soul", to replace with the real spells later): the
        // script casts their own spell for its look and telegraph, and deals the damage itself (percent of max health),
        // after the spell's wind-up, to whoever is still in its shape then
        //   Specialist: Suppressing Flames 32258, a 10 m cone (45 degrees) out of where he faces, 4 pulses 0.75 s apart
        //   Sniper:     Snipe 27324, 2.25 s aim, then the shot, up to 60 m
        //   Tank Gun:   Gatling Blast 58478, a 10-40 m cone (35 degrees) ahead of the train only, 1 s wind-up
        private const uint FlamesSpell = 32258u, SnipeSpell = 27324u, GatlingSpell = 58478u;
        private const float FlamesRange = 10f, FlamesHalfAngle = MathF.PI / 8f;
        private const int FlamesPulses = 4;
        private static readonly TimeSpan FlamesPulse = TimeSpan.FromSeconds(0.75), FlamesCooldown = TimeSpan.FromSeconds(4.5);
        private const float FlamesDamage = 0.02f;
        private const float SnipeRange = 60f, SnipeDamage = 0.02f;   // 6% hit about 10% (Teun: too much)
        private static readonly TimeSpan SnipeAim = TimeSpan.FromSeconds(2.25), SnipeCooldown = TimeSpan.FromSeconds(5);
        private const float GatlingMin = 10f, GatlingMax = 40f, GatlingHalfAngle = MathF.PI * 35f / 360f, GatlingDamage = 0.08f;
        // more wind-up (Teun: the cannon and the lasers fired too quickly): the spell's cast is stretched to it
        private static readonly TimeSpan GatlingWindUp = TimeSpan.FromSeconds(2.5), GatlingCooldown = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan OrbitalWindUp = TimeSpan.FromSeconds(3.5);

        // the explosion at the end of the route: Severus' mine explosion (26842) from an invisible point on the locomotive;
        // the train stays where it stopped for the rest of the mission (retail)
        private const uint ExplosionSpell = 26842u;

        // the fight after the explosion
        private const uint Guardbot = 49118u;
        private const uint Suppressionbot = 50758u;
        private const uint ArenaForceField = 50900u;     // Dome Force Field (blocks from the inside)
        private static readonly uint[][] Waves = [[Guardbot, Guardbot], [Guardbot, Guardbot], [Suppressionbot]];
        private static readonly TimeSpan NextWaveDelay = TimeSpan.FromSeconds(4);
        private const float WaveSpawnMin = 10f, WaveSpawnMax = 16f;   // from a player

        // the orbital bombardment (Orbital Strike Cannon 49944: 2 s cast, 6 m circle, knockdown), cast from short-lived
        // invisible units (the spell despawns its caster; that effect is unhandled, the script removes them)
        private const uint OrbitalStrikeSpell = 49944u;
        private const uint OrbitalPulseSpell = 49969u;   // lasers: 8 x 64 m and three 6 x 16 m rectangles ahead of the caster
        // laser sweeps: a row of beams across the arena, fired one after another so the line moves over the battlefield
        // fewer, wider-spaced beams: six every 3 s put Teun down at once (the beam is a channel, it hits from its start)
        private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(6);
        private const int SweepBeams = 3;
        private const float SweepSpacing = 12f;
        private static readonly TimeSpan SweepStep = TimeSpan.FromSeconds(0.25);
        private double sweepTimer;
        private const uint InvisibleCaster = 53908u;     // Suppressionbot (Explosion), InvisibleMan.m3 (the train's explosion)
        // retail's orbital caster (deep dive, Jabbithole: ~100 sightings over the arena, only 49944 and 49969): one per strike
        private const uint Battlecruiser = 50761u;       // Dominion Battlecruiser Invisible Unit - T5 - Holdout Boss
        // retail (Teun, video): barrages already come during the Suppressionbot fight (by its health), and a big finale once it
        // dies, ~20 s; ~45 s of orbital fire in all
        private static readonly TimeSpan BombardmentTime = TimeSpan.FromSeconds(20);
        private static readonly float[] BarrageThresholds = [0.75f, 0.5f, 0.25f];
        private static readonly TimeSpan BarrageTime = TimeSpan.FromSeconds(6);
        private IUnitEntity suppressionbot;
        private int nextBarrage;
        private double barrageLeft;
        private static readonly TimeSpan StrikeInterval = TimeSpan.FromSeconds(0.7);
        private const float StrikeScatter = 14f;

        // the celebration: farmers run in from outside the arena
        // 20 run in (Teun, video): 19 farmers and Ayita (Jabbithole has her once on that side, -2322, -1425). Their names
        // aren't known (Jabbithole sees Detained Farmers only in the cages): Detained Farmers, the freed prisoners
        private const int CelebratingFarmers = 20;
        // the Dome Force Field (50900): its model is 19.8 m in radius, at the creature's scale 2.5 about 49.5 m (a 30 m guess
        // teleported players standing inside it, Teun)
        private const float ArenaRadius = 49f;
        private static readonly Vector3 BellFarmhouseSpot = new(-2261.7f, -924.8f, -1330.2f);   // WorldLocation2 39450
        // retail: they cheer; the cheer emote didn't show on them, so they dance (Teun, 2 Oct 2026)
        // retail (Teun): they follow the party around, cheering and dancing
        private static readonly uint[] FarmerEmotes = [28u, 34u, 28u, 362u, 297u];   // cheer, dance, cheer, dance1, dance2
        private static readonly TimeSpan FollowInterval = TimeSpan.FromSeconds(2.5);
        private static readonly TimeSpan EmoteInterval = TimeSpan.FromSeconds(6);
        private const float FollowDistance = 7f;
        private const float FarmerRunSpeed = 6f;
        private readonly List<IUnitEntity> celebrating = [];
        private double followTimer;
        private double emoteTimer;

        private static readonly TimeSpan TrainRetryDelay = TimeSpan.FromSeconds(8);
        private static readonly Vector3 RetrySpot = new(-2259.5f, -924.3f, -1332f);   // in the Bell Farmhouse, by Ayita
        private static readonly TimeSpan LineGap = TimeSpan.FromSeconds(7);
        private const uint CommunicatorMs = 8000u;

        private const Faction DominionFaction = (Faction)1452;
        private const Faction FriendlyFaction = (Faction)219;

        private enum Stage { Briefing, Train, Fight, Bombardment, Celebration }
        private Stage stage = Stage.Briefing;

        private class Rider
        {
            public IWorldEntity Entity;
            public List<Vector3> Path;
            public float Speed;
        }

        private class Passenger
        {
            public IWorldEntity Entity;
            public IWorldEntity Carrier;
            public Vector3 Local;
            public float Yaw;
        }

        // the current train
        private readonly List<Rider> riders = [];
        private readonly List<Passenger> passengers = [];
        private readonly List<(IWorldEntity Car, ushort Spline)> splineCars = [];
        private readonly Dictionary<IWorldEntity, (Vector3 Position, float Yaw)> carrierFrames = [];
        // the same route as 4508, starting 49 m and 102 m further back (Spline2, 2 m/s)
        private static readonly ushort[] CarSplines = [15012, 14981];
        private const bool CarsOnTableSplines = false;

        // TEMPORARY test (2 Oct 2026): the cars' cages and farmers never show while the train moves (they do parked; the
        // locomotive's passengers show). Car 1 also carries a friendly Suppression Specialist (a guard type that shows on the
        // locomotive): seen = the cage/farmer models, unseen = the cars as carriers. Car 2's passengers are attached again
        // when a player comes within 80 m: seen then = the client lost the attachment
        private const bool DevCarPassengerTest = false;   // benched: the cars' cages and farmers never show (Teun)

        // a prop's state only shows when it is sent with ServerEmote (as !entity modify standstate does), setting the stand
        // state alone left the cages without their laser bars: sent when the train sets off and again every few seconds
        private readonly List<IWorldEntity> cages = [];
        private static readonly TimeSpan CageStateInterval = TimeSpan.FromSeconds(10);
        private double cageStateTimer;
        private const float ReattachRange = 80f;
        private readonly List<Passenger> reattach = [];
        private readonly List<IWorldEntity> trainParts = [];       // everything that goes with the train
        // by reference: an entity's guid is only set once it is on the map (a moment after AddToMap)
        private readonly HashSet<IUnitEntity> guards = [];
        private readonly Dictionary<IUnitEntity, double> guardCooldowns = [];
        private IWorldEntity locomotive;
        private IWorldEntity driveSystem;
        private bool trainLaunched;
        private double trainClock;
        private bool shieldsDown;
        private bool bombPlanted;
        private bool trainArrived;
        private int attempt;

        private Vector3[] route;
        private float[] routeDistances;
        private float trainStart;   // where along the route the locomotive starts (its centre)

        // the fight
        private int wave = -1;
        private readonly HashSet<IUnitEntity> waveUnits = [];   // by reference, see guards
        private IWorldEntity arena;
        private Vector3 arenaCentre;
        private double bombardmentLeft;
        private double strikeTimer;
        private readonly List<(IWorldEntity Caster, double Left)> strikeCasters = [];

        private readonly TimedActionQueue actionQueue = new();

        #region Dependency Injection

        private readonly ILogger<AllAboardMissionScript> log;
        private readonly IGameTableManager gameTableManager;
        private readonly IStoryBuilder storyBuilder;
        private readonly ICreatureInfoManager creatureInfoManager;
        private readonly IFactory<ISpellParameters> spellParametersFactory;
        private readonly HycrestDialogue dialogue;

        public AllAboardMissionScript(
            ILogger<AllAboardMissionScript> log,
            IGameTableManager gameTableManager,
            IStoryBuilder storyBuilder,
            ICreatureInfoManager creatureInfoManager,
            IFactory<ISpellParameters> spellParametersFactory)
        {
            this.log                    = log;
            this.gameTableManager       = gameTableManager;
            this.storyBuilder           = storyBuilder;
            this.creatureInfoManager    = creatureInfoManager;
            this.spellParametersFactory = spellParametersFactory;
            dialogue = new HycrestDialogue(gameTableManager, actionQueue);
        }

        #endregion

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public override void OnLoad(IPublicEvent owner)
        {
            base.OnLoad(owner);

            route = gameTableManager.Spline2Node.Entries
                .Where(n => n.SplineId == RouteSpline)
                .OrderBy(n => n.Ordinal)
                .Select(n => new Vector3(n.Position0, n.Position1, n.Position2))
                .Aggregate(new List<Vector3>(), (list, p) => { if (list.Count == 0 || Vector3.Distance(list[^1], p) > 0.05f) list.Add(p); return list; })
                .ToArray();
            routeDistances = new float[route.Length];
            for (int i = 1; i < route.Length; i++)
                routeDistances[i] = routeDistances[i - 1] + Vector3.Distance(route[i - 1], route[i]);
            trainStart = routeDistances[^1] - TrainSpeed * (float)TrainTime.TotalSeconds;

            // the big map's marker for talking to Ayita (the minimap shows her through the target group already)
            publicEvent.SetObjectiveLocations(TalkToAyita, BellFarmhouse);

            log.LogInformation($"Hycrest: All Aboard, route {RouteSpline} {routeDistances[^1]:0} m, the locomotive starts at {trainStart:0} m. Talk to Ayita.");
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public override void Update(double lastTick)
        {
            actionQueue.Update(lastTick);

            switch (stage)
            {
                case Stage.Train:
                    UpdateTrain(lastTick);
                    break;
                case Stage.Fight:
                    UpdateBarrages(lastTick);
                    break;
                case Stage.Bombardment:
                    UpdateBombardment(lastTick);
                    break;
                case Stage.Celebration:
                    UpdateCelebration(lastTick);
                    break;
            }

            if (stage is Stage.Fight or Stage.Bombardment or Stage.Celebration)
                ClearStuckCrowdControl(lastTick);

            if (arena is { InWorld: true } && stage is Stage.Fight or Stage.Bombardment)
            {
                arenaCheck -= lastTick;
                if (arenaCheck <= 0d)
                {
                    arenaCheck = 2d;
                    KeepPlayersInArena();
                }
            }

            for (int i = strikeCasters.Count - 1; i >= 0; i--)
            {
                (IWorldEntity caster, double left) = strikeCasters[i];
                left -= lastTick;
                if (left > 0d)
                {
                    strikeCasters[i] = (caster, left);
                    continue;
                }

                if (caster.InWorld)
                    caster.RemoveFromMap();
                strikeCasters.RemoveAt(i);
            }
        }

        /// <summary>
        /// Invoked when the status of an objective changes.
        /// </summary>
        public override void OnPublicEventObjectiveStatus(IPublicEventObjective objective)
        {
            switch (objective.Entry.Id, objective.Status)
            {
                case (TalkToAyita, PublicEventStatus.Succeeded):
                    Communicator(AyitaBriefing, AyitaSinnatus);
                    SetMusic(true);
                    actionQueue.Enqueue(TimeSpan.FromSeconds(1), StartTrain);
                    actionQueue.Enqueue(TimeSpan.FromSeconds(20), () =>
                    {
                        if (stage == Stage.Train && !bombPlanted)
                            Communicator(AyitaTrain, AyitaSinnatus);
                    });
                    break;
                case (KillGuards, PublicEventStatus.Succeeded):
                    log.LogInformation("Hycrest: All Aboard, the train's guards are down; the lever can be pulled.");
                    break;
                case (PlantBomb, PublicEventStatus.Succeeded):
                    Communicator(AyitaBombPlanted, AyitaSinnatus);
                    // the countdown goes on, green ("wait for Ayita to spring the trap")
                    publicEvent.ActivateObjective(SpringTrap, 0u, TimeSpan.FromSeconds(trainClock));
                    publicEvent.SetObjectiveLocations(SpringTrap, 13143u);   // its own point, the end of the route
                    break;
                case (PlantBomb, PublicEventStatus.Failed):
                    TrainFailed();
                    break;
                case (DefeatWaves, PublicEventStatus.Succeeded):
                    StartBombardment();
                    break;
            }
        }

        /// <summary>
        /// Invoked when a <see cref="IGridEntity"/> is added to the map the public event is on.
        /// </summary>
        public override void OnAddToMap(IGridEntity entity)
        {
            if (entity is IUnitEntity unit && guards.Contains(unit))
                publicEvent.AddObjectiveTarget(KillGuards, unit);

            // "kill the guards" names the drive system's creature (target group 6862, copied from 190), so the engine adds
            // the drive system as a target on its own: it can't be killed, the objective stayed at 1 (Teun, 1 Oct 2026)
            if (entity == driveSystem)
                actionQueue.Enqueue(TimeSpan.Zero, () =>
                {
                    if (driveSystem is { InWorld: true })
                        publicEvent.RemoveObjectiveTarget(KillGuards, driveSystem.Guid);
                });
        }

        /// <summary>
        /// Invoked when a unit on the map has been killed.
        /// </summary>
        public override void OnEntityKilled(IUnitEntity unit)
        {
            // the Tank Gun's destroyed look is its State2 (its model: AP_State0_State2 is the death, AP_State2_Idle the
            // wreck); dying resets a unit's stand state to State0 (UpdateCombatState), so it is set a moment later
            if (unit.CreatureId == TankGun && guards.Contains(unit))
                actionQueue.Enqueue(TimeSpan.FromSeconds(0.5), () =>
                {
                    if (unit.InWorld)
                        HycrestDropShip.SetState(unit, StandState.State2);   // sent, not only stored (as the cages)
                });

            if (!waveUnits.Remove(unit) || waveUnits.Count > 0 || stage != Stage.Fight)
                return;

            publicEvent.UpdateObjective(DefeatWaves, 1);
            log.LogInformation($"Hycrest: All Aboard, wave {wave + 1} down.");
            if (wave + 1 < Waves.Length)
                actionQueue.Enqueue(NextWaveDelay, StartNextWave);
        }

        // --- the train --------------------------------------------------------------------------------------------------

        /// <summary>
        /// Put the train on its route (locomotive, two cars, guards, lever, drive system, force fields, cages, farmers) and
        /// start "plant the bomb" with its 180 s; everything sets off together once it is all on the map.
        /// </summary>
        private void StartTrain()
        {
            if (publicEvent.HasFinished)
                return;

            attempt++;
            stage        = Stage.Train;
            trainLaunched = false;
            trainClock   = 0d;
            trainMarker  = 0u;
            markerTimer  = 0d;
            shieldsDown  = false;
            bombPlanted  = false;
            trainArrived = false;

            publicEvent.ActivateObjective(KillGuards);
            publicEvent.ActivateObjective(PlantBomb);

            locomotive = SpawnRider(Locomotive, 0f, Vector3.Zero, null);
            IWorldEntity[] cars = [SpawnCar(CarSplines[0], 1), SpawnCar(CarSplines[1], 2)];

            driveSystem = SpawnPassenger(DriveSystem, locomotive, 0f, DriveSystemSpot, DriveSystemYaw, FriendlyFaction);
            SpawnPassenger(Lever, locomotive, 0f, LeverSpot, LeverYaw, FriendlyFaction);
            foreach ((uint creature, Vector3 spot, float yaw) in Guards)
            {
                if (SpawnPassenger(creature, locomotive, 0f, spot, yaw, DominionFaction) is IUnitEntity guard)
                {
                    guard.IsTurret = true;
                    guards.Add(guard);
                }
            }

            foreach ((Vector3 spot, float yaw) in ForceFieldSpots)
                SpawnPassenger(ForceField, locomotive, 0f, spot, yaw, FriendlyFaction);

            for (int c = 0; c < cars.Length; c++)
            {
                if (DevCarPassengerTest && c == 0)
                    SpawnPassenger(Specialist, cars[c], 0f, new Vector3(0f, 2.8f, 0f), 0f, FriendlyFaction);

                float along = -(c + 1) * CarSpacing;
                foreach (Vector3 cageSpot in CageSpots)
                {
                    // State1: the red laser bars, closed (Teun, 2 Oct 2026); sent once the train sets off (CageStates)
                    if (SpawnPassenger(Cage, cars[c], 0f, cageSpot, 0f, FriendlyFaction) is IWorldEntity cage)
                        cages.Add(cage);
                    for (int f = 0; f < CagedFarmerOffsets.Length; f++)
                        SpawnPassenger(DetainedFarmers[(c * 8 + f) % DetainedFarmers.Length], cars[c], 0f,
                            cageSpot + CagedFarmerOffsets[f], MathF.PI * (f % 2), FriendlyFaction);
                }
            }

            log.LogInformation($"Hycrest: All Aboard, the train is set up (attempt {attempt}, {riders.Count} riders, {passengers.Count} passengers).");
        }

        /// <summary>
        /// Spawn something that keeps pace with the locomotive along its own copy of the route: its place on the train is
        /// <paramref name="local"/> in the frame of the car <paramref name="along"/> metres behind the locomotive's centre.
        /// </summary>
        private IWorldEntity SpawnRider(uint creatureId, float along, Vector3 local, Faction? faction)
        {
            var path = new List<Vector3>();
            float time = (float)TrainTime.TotalSeconds;
            int steps = Math.Max(1, (int)(TrainSpeed * time / PathStep));
            for (int i = 0; i <= steps; i++)
                path.Add(OnTrain(trainStart + along + TrainSpeed * time * i / steps, local));

            // parked (dev): at the end spot, no movement
            float startAt = DevParkTrain ? trainStart + along + TrainSpeed * time : trainStart + along;
            IWorldEntity entity = Spawn(creatureId, DevParkTrain ? path[^1] : path[0], Yaw(startAt), faction);
            if (entity == null)
                return null;

            float length = 0f;
            for (int i = 1; i < path.Count; i++)
                length += Vector3.Distance(path[i - 1], path[i]);

            riders.Add(new Rider { Entity = entity, Path = path, Speed = length / time });
            trainParts.Add(entity);
            carrierFrames[entity] = (DevParkTrain ? path[^1] : path[0], Yaw(startAt));
            return entity;
        }

        /// <summary>
        /// A car on the table's own copy of the route (<see cref="CarSplines"/>): the game's spline (smooth, unlike a sampled
        /// path, which jittered on slopes, Teun), started with the locomotive and stopped where it is when the locomotive
        /// arrives (179.7 s: 49 m and 102 m behind it).
        /// </summary>
        private IWorldEntity SpawnCar(ushort splineId, int number)
        {
            Vector3[] nodes = gameTableManager.Spline2Node.Entries
                .Where(n => n.SplineId == splineId)
                .OrderBy(n => n.Ordinal)
                .Select(n => new Vector3(n.Position0, n.Position1, n.Position2))
                .Aggregate(new List<Vector3>(), (list, p) => { if (list.Count == 0 || Vector3.Distance(list[^1], p) > 0.05f) list.Add(p); return list; })
                .ToArray();
            // the table splines jittered even more (Teun, 2 Oct 2026): the cars ride like the locomotive again, on their own
            // sampled copy of the route, exactly CarSpacing apart from start to stop
            if (DevParkTrain || nodes.Length < 2 || !CarsOnTableSplines)
            {
                // a unit now (see Spawn): friendly and out of reach, not a target
                IWorldEntity rider = SpawnRider(Flatbed, -number * CarSpacing, Vector3.Zero, FriendlyFaction);
                if (rider is IUnitEntity unit)
                    unit.IsInvulnerable = true;
                return rider;
            }

            Vector3 direction = nodes[1] - nodes[0];
            float yaw = MathF.Atan2(-direction.X, -direction.Z);
            IWorldEntity car = Spawn(Flatbed, nodes[0], yaw, null);
            if (car == null)
                return null;

            splineCars.Add((car, splineId));
            trainParts.Add(car);
            carrierFrames[car] = (nodes[0], yaw);
            return car;
        }

        /// <summary>
        /// Spawn something that rides <paramref name="carrier"/> as a platform passenger (seen and solid, no own movement).
        /// </summary>
        private IWorldEntity SpawnPassenger(uint creatureId, IWorldEntity carrier, float along, Vector3 local, float yaw, Faction? faction)
        {
            if (carrier == null)
                return null;

            if (!carrierFrames.TryGetValue(carrier, out var frame))
                return null;
            float sin = MathF.Sin(frame.Yaw), cos = MathF.Cos(frame.Yaw);
            Vector3 world = frame.Position + new Vector3(local.X * cos + local.Z * sin, local.Y, -local.X * sin + local.Z * cos);
            IWorldEntity entity = Spawn(creatureId, world, frame.Yaw + yaw, faction);
            if (entity == null)
                return null;

            passengers.Add(new Passenger { Entity = entity, Carrier = carrier, Local = local, Yaw = yaw });
            trainParts.Add(entity);
            return entity;
        }

        private void UpdateTrain(double lastTick)
        {
            if (!trainLaunched)
            {
                // everything sets off in the same tick, once it is all on the map
                if (trainParts.Count == 0 || trainParts.Any(e => !e.InWorld))
                    return;

                foreach (Passenger passenger in passengers)
                    passenger.Entity.SetPlatform(passenger.Carrier, passenger.Local, new Vector3(passenger.Yaw, 0f, 0f));
                if (DevCarPassengerTest)
                {
                    reattach.Clear();
                    IWorldEntity secondCar = riders.Where(r => r.Entity.CreatureId == Flatbed).Select(r => r.Entity).Skip(1).FirstOrDefault();
                    reattach.AddRange(passengers.Where(p => p.Carrier == secondCar));
                }
                trainLaunched = true;
                if (DevParkTrain)
                {
                    LogParkedTrain();
                    return;
                }

                foreach (Rider rider in riders)
                {
                    rider.Entity.MovementManager.SetMode(ModeType.Walk);
                    rider.Entity.MovementManager.LaunchSpline(rider.Path, SplineType.CatmullRom, SplineMode.OneShot, rider.Speed);
                }
                foreach ((IWorldEntity car, ushort spline) in splineCars)
                {
                    car.MovementManager.SetMode(ModeType.Walk);
                    car.MovementManager.LaunchSpline(spline, SplineMode.OneShot, TrainSpeed, true);
                }

                log.LogInformation("Hycrest: All Aboard, the train sets off.");
                return;
            }

            trainClock += lastTick;
            UpdateGuardAttacks(lastTick);

            markerTimer -= lastTick;
            if (markerTimer <= 0d && locomotive is { InWorld: true })
            {
                markerTimer = MarkerInterval.TotalSeconds;
                var at = new Vector2(locomotive.Position.X, locomotive.Position.Z);
                uint nearest = RouteMarkers.OrderBy(m => Vector2.Distance(m.Position, at)).First().Id;
                if (nearest != trainMarker)
                {
                    trainMarker = nearest;
                    publicEvent.SetObjectiveLocations(PlantBomb, nearest);
                    publicEvent.SetObjectiveLocations(KillGuards, nearest);
                }
            }

            cageStateTimer -= lastTick;
            if (cageStateTimer <= 0d)
            {
                cageStateTimer = CageStateInterval.TotalSeconds;
                foreach (IWorldEntity cage in cages.Where(c => c.InWorld))
                    HycrestDropShip.SetState(cage, StandState.State1);
            }

            // TEMPORARY test: car 2's passengers attached again once a player is near it
            if (DevCarPassengerTest && reattach.Count > 0)
            {
                IWorldEntity car = reattach[0].Carrier;
                if (car.InWorld && mapInstance.GetPlayers().Any(p => Vector3.Distance(p.Position, car.Position) <= ReattachRange))
                {
                    foreach (Passenger passenger in reattach)
                        if (passenger.Entity.InWorld)
                            passenger.Entity.SetPlatform(passenger.Carrier, passenger.Local, new Vector3(passenger.Yaw, 0f, 0f));
                    log.LogInformation($"Hycrest: All Aboard, test: car 2's {reattach.Count} passengers attached again (a player is near).");
                    reattach.Clear();
                }
            }

            if (!trainArrived && trainClock >= TrainTime.TotalSeconds)
            {
                trainArrived = true;
                // the cars on their longer splines stop where they are, in a row behind the locomotive
                foreach ((IWorldEntity car, _) in splineCars)
                    if (car.InWorld)
                        car.MovementManager.Finalise();
                if (bombPlanted)
                    TrainExplodes();
                // otherwise "plant the bomb" fails on its own timer a moment later (TrainFailed)
            }
        }

        /// <summary>
        /// The guards fire their own attacks at the nearest player in range (the Tank Gun only ahead of the train).
        /// </summary>
        private void UpdateGuardAttacks(double lastTick)
        {
            foreach (IUnitEntity guard in guards)
            {
                if (!guard.InWorld || !guard.IsAlive)
                    continue;

                double cooldown = guardCooldowns.GetValueOrDefault(guard) - lastTick;
                guardCooldowns[guard] = cooldown;
                if (cooldown > 0d)
                    continue;

                switch (guard.CreatureId)
                {
                    case Specialist:
                    {
                        if (PlayersInCone(guard.Position, guard.Rotation.X, 0f, FlamesRange, FlamesHalfAngle).FirstOrDefault() is not IPlayer target)
                            continue;

                        CastAt(guard, FlamesSpell, target);
                        for (int pulse = 1; pulse <= FlamesPulses; pulse++)
                            actionQueue.Enqueue(TimeSpan.FromTicks(FlamesPulse.Ticks * pulse), () =>
                            {
                                if (guard.InWorld && guard.IsAlive)
                                    foreach (IPlayer hit in PlayersInCone(guard.Position, guard.Rotation.X, 0f, FlamesRange, FlamesHalfAngle))
                                        Hurt(hit, FlamesDamage, guard);
                            });
                        guardCooldowns[guard] = FlamesCooldown.TotalSeconds;
                        break;
                    }
                    case Sniper:
                    {
                        IPlayer target = mapInstance.GetPlayers()
                            .Where(p => p.IsAlive && Vector3.Distance(p.Position, guard.Position) <= SnipeRange)
                            .OrderBy(p => Vector3.Distance(p.Position, guard.Position))
                            .FirstOrDefault();
                        if (target == null)
                            continue;

                        CastAt(guard, SnipeSpell, target);
                        actionQueue.Enqueue(SnipeAim, () =>
                        {
                            if (guard.InWorld && guard.IsAlive && target.InWorld && target.IsAlive
                                && Vector3.Distance(target.Position, guard.Position) <= SnipeRange)
                                Hurt(target, SnipeDamage, guard);
                        });
                        guardCooldowns[guard] = SnipeCooldown.TotalSeconds;
                        break;
                    }
                    case TankGun:
                    {
                        // fixed: it only fires ahead of the train and doesn't turn
                        float ahead = Yaw(trainStart + TrainSpeed * (float)trainClock);
                        if (PlayersInCone(guard.Position, ahead, GatlingMin, GatlingMax, GatlingHalfAngle).FirstOrDefault() is not IPlayer target)
                            continue;

                        CastAt(guard, GatlingSpell, target, GatlingWindUp);
                        actionQueue.Enqueue(GatlingWindUp, () =>
                        {
                            if (!guard.InWorld || !guard.IsAlive)
                                return;
                            float nowAhead = Yaw(trainStart + TrainSpeed * (float)trainClock);
                            foreach (IPlayer hit in PlayersInCone(guard.Position, nowAhead, GatlingMin, GatlingMax, GatlingHalfAngle))
                                Hurt(hit, GatlingDamage, guard);
                        });
                        guardCooldowns[guard] = GatlingCooldown.TotalSeconds;
                        break;
                    }
                }
            }
        }

        private IEnumerable<IPlayer> PlayersInCone(Vector3 from, float yaw, float minRange, float maxRange, float halfAngle)
        {
            return mapInstance.GetPlayers()
                .Where(p => p.IsAlive)
                .Where(p =>
                {
                    float distance = Vector2.Distance(new Vector2(p.Position.X, p.Position.Z), new Vector2(from.X, from.Z));
                    return distance >= minRange && distance <= maxRange && AngleTo(from, yaw, p.Position) <= halfAngle;
                })
                .OrderBy(p => Vector3.Distance(p.Position, from));
        }

        private void CastAt(IUnitEntity caster, uint spell4Id, IUnitEntity target, TimeSpan? castTime = null)
        {
            if (caster.IsCasting())
                return;
            ISpellParameters parameters = spellParametersFactory.Resolve();
            if (castTime.HasValue)
                parameters.CastTimeOverride = (int)castTime.Value.TotalMilliseconds;
            parameters.PrimaryTargetId        = target.Guid;
            parameters.UserInitiatedSpellCast = false;
            caster.CastSpell(spell4Id, parameters);
        }

        private static void Hurt(IPlayer player, float fraction, IUnitEntity source)
        {
            if (player.IsAlive)
                player.ModifyHealth(Math.Max(1u, (uint)(player.MaxHealth * fraction)), DamageType.Physical, source);
        }

        /// <summary>
        /// The lever at the back: the force fields drop and the drive system can be bombed. Retail (Teun, 1 Oct 2026): it can
        /// be pulled any time, the guards don't have to be down; being hit interrupts the pull (its cast).
        /// </summary>
        public void OnLeverPulled(IWorldEntity lever, IPlayer player)
        {
            if (stage != Stage.Train || shieldsDown || trainArrived)
                return;

            shieldsDown = true;
            foreach (Passenger field in passengers.Where(p => p.Entity.CreatureId == ForceField).ToList())
            {
                if (field.Entity.InWorld)
                    field.Entity.RemoveFromMap();
                passengers.Remove(field);
            }

            Communicator(AyitaShieldsDown, AyitaSinnatus);
            log.LogInformation($"Hycrest: All Aboard, {player.Name} pulled the lever: the force fields are down.");
        }

        /// <summary>
        /// The bomb is planted at the drive system (only once the force fields are down).
        /// </summary>
        public void OnBombPlanted(IWorldEntity driveSystem, IPlayer player)
        {
            if (stage != Stage.Train || !shieldsDown || bombPlanted || trainArrived || driveSystem != this.driveSystem)
                return;

            bombPlanted = true;
            publicEvent.UpdateObjective(PlantBomb, 1);
            log.LogInformation($"Hycrest: All Aboard, {player.Name} planted the bomb ({trainClock:0} s in).");
        }

        /// <summary>
        /// The train reached the end of its route without a bomb: Ayita's line, then everything is set back to just after
        /// talking to her and the train comes again.
        /// </summary>
        private void TrainFailed()
        {
            if (stage != Stage.Train)
                return;

            Communicator(AyitaTooLate, AyitaSinnatus);
            if (DevParkTrain)
            {
                // measuring the parked train: it stays
                log.LogInformation("Hycrest: All Aboard, the timer ran out (parked train, dev): no restart.");
                return;
            }

            log.LogInformation($"Hycrest: All Aboard, the train got through (attempt {attempt}); it starts over.");
            actionQueue.Enqueue(TrainRetryDelay, () =>
            {
                // back to where the mission starts, Ayita's farmhouse (Teun, 2 Oct 2026)
                foreach (IPlayer player in mapInstance.GetPlayers())
                    if (player.CanTeleport())
                        player.TeleportToLocal(RetrySpot, false);
                RemoveTrain();
                publicEvent.ResetObjective(PlantBomb);
                publicEvent.ResetObjective(KillGuards);
                StartTrain();
            });
        }

        private void RemoveTrain()
        {
            foreach (IUnitEntity guard in guards)
                if (guard.InWorld)
                    publicEvent.RemoveObjectiveTarget(KillGuards, guard.Guid);
            foreach (IWorldEntity part in trainParts)
                if (part.InWorld)
                    part.RemoveFromMap();

            trainParts.Clear();
            riders.Clear();
            passengers.Clear();
            splineCars.Clear();
            carrierFrames.Clear();
            cages.Clear();
            guards.Clear();
            guardCooldowns.Clear();
            locomotive  = null;
            driveSystem = null;
        }

        /// <summary>
        /// The train is at the end of its route with the bomb on board: it blows up, Arsenax comes on, then the waves.
        /// </summary>
        private void TrainExplodes()
        {
            stage = Stage.Fight;
            arenaCentre = Grounded(route[^1]);
            // the train stays where it stopped (retail); the guards' objective is done with
            foreach (IUnitEntity guard in guards)
                if (guard.InWorld)
                    publicEvent.RemoveObjectiveTarget(KillGuards, guard.Guid);
            guards.Clear();
            // five blasts along the train (one alone was too small, Teun)
            float end = trainStart + TrainSpeed * (float)TrainTime.TotalSeconds;
            foreach ((float along, Vector3 local) in new[]
            {
                (0f, DriveSystemSpot), (0f, new Vector3(0f, 6f, 12f)), (0f, new Vector3(0f, 6f, -16f)),
                (-CarSpacing, new Vector3(0f, 3f, 0f)), (-2f * CarSpacing, new Vector3(0f, 3f, 0f))
            })
                Explosion(OnTrain(end + along, local));
            publicEvent.UpdateObjective(SpringTrap, 1);
            log.LogInformation("Hycrest: All Aboard, the train explodes.");

            Communicator(AyitaBombsAway, AyitaSinnatus);
            TimeSpan time = LineGap;
            actionQueue.Enqueue(time, () => Communicator(VesnaWarship, VesnaTaranoft));
            actionQueue.Enqueue(time += LineGap, () => Communicator(ArsenaxAzrion, ArsenaxSeverus));
            actionQueue.Enqueue(time += LineGap, () => Communicator(AyitaNoSurrender, AyitaSinnatus));
            actionQueue.Enqueue(time += TimeSpan.FromSeconds(3), () =>
            {
                arena = Spawn(ArenaForceField, arenaCentre, 0f, FriendlyFaction);
                KeepPlayersInArena();
                publicEvent.ActivateObjective(DefeatWaves);
                StartNextWave();
            });
        }

        private void Explosion(Vector3 at)
        {
            if (Spawn(InvisibleCaster, at, 0f, FriendlyFaction) is not IUnitEntity caster)
                return;

            caster.IsInvulnerable = true;
            strikeCasters.Add((caster, 5d));
            actionQueue.Enqueue(TimeSpan.FromSeconds(0.3), () =>
            {
                if (!caster.InWorld)
                    return;
                ISpellParameters parameters = spellParametersFactory.Resolve();
                parameters.PrimaryTargetId        = caster.Guid;
                parameters.UserInitiatedSpellCast = false;
                caster.CastSpell(ExplosionSpell, parameters);
            });
        }

        // --- the fight --------------------------------------------------------------------------------------------------

        private void StartNextWave()
        {
            if (stage != Stage.Fight || publicEvent.HasFinished)
                return;

            wave++;
            foreach (uint creatureId in Waves[wave])
            {
                // inside the dome, around its centre (around a player, a wave spawned outside when that player was being
                // moved in at the same moment)
                Vector3 centre = arenaCentre;
                float angle = (float)(Random.Shared.NextDouble() * MathF.Tau);
                float distance = WaveSpawnMin + (float)Random.Shared.NextDouble() * (WaveSpawnMax - WaveSpawnMin);
                Vector3 spot = Grounded(centre + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * distance);

                if (Spawn(creatureId, spot, MathF.Atan2(-(centre.X - spot.X), -(centre.Z - spot.Z)), DominionFaction) is IUnitEntity unit)
                {
                    waveUnits.Add(unit);
                    if (creatureId == Suppressionbot)
                        suppressionbot = unit;
                }
            }

            log.LogInformation($"Hycrest: All Aboard, wave {wave + 1} ({string.Join(", ", Waves[wave])}).");
        }

        /// <summary>
        /// The Suppressionbot is down: Arsenax fires from orbit for ~30 s.
        /// </summary>
        private void StartBombardment()
        {
            stage = Stage.Bombardment;
            bombardmentLeft = BombardmentTime.TotalSeconds;
            strikeTimer = 0d;   // at once, the moment the Suppressionbot dies
            sweepTimer  = 0d;
            Communicator(ArsenaxUglyOne, ArsenaxSeverus);
            actionQueue.Enqueue(TimeSpan.FromSeconds(10), () => Communicator(ArsenaxBatteries, ArsenaxSeverus));
            log.LogInformation("Hycrest: All Aboard, the orbital bombardment starts.");
        }

        private void UpdateBombardment(double lastTick)
        {
            bombardmentLeft -= lastTick;
            if (bombardmentLeft <= 0d)
            {
                EndBombardment();
                return;
            }

            OrbitalFire(lastTick);
        }

        /// <summary>
        /// During the Suppressionbot fight: a short barrage each time it drops below 75%, 50% and 25% health.
        /// </summary>
        private void UpdateBarrages(double lastTick)
        {
            if (suppressionbot is { InWorld: true, IsAlive: true } && nextBarrage < BarrageThresholds.Length
                && suppressionbot.Health <= suppressionbot.MaxHealth * BarrageThresholds[nextBarrage])
            {
                nextBarrage++;
                barrageLeft = BarrageTime.TotalSeconds;
                strikeTimer = 0d;
                sweepTimer  = 0d;
                Communicator(ArsenaxStrikeLines[Random.Shared.Next(ArsenaxStrikeLines.Length)], ArsenaxSeverus);
                log.LogInformation($"Hycrest: All Aboard, orbital barrage {nextBarrage} (Suppressionbot below {BarrageThresholds[nextBarrage - 1]:P0}).");
            }

            if (barrageLeft <= 0d)
                return;
            barrageLeft -= lastTick;
            OrbitalFire(lastTick);
        }

        private void OrbitalFire(double lastTick)
        {
            sweepTimer -= lastTick;
            if (sweepTimer <= 0d)
            {
                sweepTimer = SweepInterval.TotalSeconds;
                LaserSweep();
            }

            strikeTimer -= lastTick;
            if (strikeTimer > 0d)
                return;
            strikeTimer = StrikeInterval.TotalSeconds;

            List<IPlayer> players = mapInstance.GetPlayers().Where(p => p.IsAlive).ToList();
            if (players.Count == 0)
                return;

            IPlayer near = players[Random.Shared.Next(players.Count)];
            float angle = (float)(Random.Shared.NextDouble() * MathF.Tau);
            float distance = (float)Random.Shared.NextDouble() * StrikeScatter;
            Vector3 spot = Grounded(near.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * distance);
            OrbitalCast(OrbitalStrikeSpell, spot, 0f, TimeSpan.FromSeconds(0.2));

        }

        /// <summary>
        /// A row of laser beams (Orbital Strike Pulse, 8 x 64 m) from the arena's edge across it, one after another.
        /// </summary>
        private void LaserSweep()
        {
            float yaw = (float)(Random.Shared.NextDouble() * MathF.Tau);
            var forward = new Vector3(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw));
            var side = new Vector3(-forward.Z, 0f, forward.X);
            Vector3 start = arenaCentre - forward * ArenaRadius;
            for (int i = 0; i < SweepBeams; i++)
            {
                float offset = (i - (SweepBeams - 1) / 2f) * SweepSpacing;
                OrbitalCast(OrbitalPulseSpell, Grounded(start + side * offset), yaw, TimeSpan.FromTicks(SweepStep.Ticks * i) + TimeSpan.FromSeconds(0.2));
            }
        }

        // the strikes' own spells only show (cast by friendly casters, in their own timing: the hits didn't match the
        // telegraphs, Teun); the script deals the damage when the telegraph says so
        //   blast (49944): a 6 m circle, 2 s telegraph, then the hit
        //   beam (49969): an 8 x 64 m strip ahead of the caster, a 1.5 s channel ticking every 0.5 s
        private static readonly TimeSpan BlastTelegraph = TimeSpan.FromSeconds(2);
        private const float BlastRadius = 6f, BlastDamage = 0.05f;
        private const float BeamHalfWidth = 4f, BeamLength = 64f, BeamDamage = 0.02f;
        private const int BeamTicks = 4;
        private static readonly TimeSpan BeamTick = TimeSpan.FromSeconds(0.5);

        // the beam: a red strip first, 2 s later it fires (retail, Teun). Its own 8 x 64 m telegraph (5844, 2.5 s) is meant
        // to show while its phases run down the strip, but the engine fires the whole channel at once, so the red strip
        // comes from another spell with a long rectangle and a cast: Ondu's Line Smash (42420, 6 x 60 m, 1.5 s, stretched
        // to 2 s), cast by a friendly caster (it can't hurt players); then the beam
        private const uint BeamWarningSpell = 42420u;
        private static readonly TimeSpan BeamWarning = TimeSpan.FromSeconds(2);
        // the strip stays while the beam fires (Teun): its cast runs on through the beam's 1.5 s channel
        private static readonly TimeSpan BeamStrip = BeamWarning + TimeSpan.FromSeconds(1.5);

        private void OrbitalCast(uint spell4Id, Vector3 spot, float yaw, TimeSpan delay)
        {
            if (spell4Id == OrbitalPulseSpell)
            {
                CastVisual(BeamWarningSpell, spot, yaw, delay, BeamStrip);
                delay += BeamWarning;
            }

            CastVisual(spell4Id, spot, yaw, delay);

            if (spell4Id == OrbitalStrikeSpell)
            {
                actionQueue.Enqueue(delay + BlastTelegraph, () =>
                {
                    foreach (IPlayer player in mapInstance.GetPlayers().Where(p => p.IsAlive))
                    {
                        float distance = Vector2.Distance(new Vector2(player.Position.X, player.Position.Z), new Vector2(spot.X, spot.Z));
                        if (distance <= BlastRadius)
                        {
                            Hurt(player, BlastDamage, null);
                            log.LogDebug($"Hycrest: All Aboard, orbital blast hit {player.Name} ({distance:0.0} m from {spot}).");
                        }
                    }
                });
                return;
            }

            var forward = new Vector2(-MathF.Sin(yaw), -MathF.Cos(yaw));
            var side = new Vector2(-forward.Y, forward.X);
            var origin = new Vector2(spot.X, spot.Z);
            for (int tick = 0; tick < BeamTicks; tick++)
                actionQueue.Enqueue(delay + TimeSpan.FromTicks(BeamTick.Ticks * tick), () =>
                {
                    foreach (IPlayer player in mapInstance.GetPlayers().Where(p => p.IsAlive))
                    {
                        Vector2 offset = new Vector2(player.Position.X, player.Position.Z) - origin;
                        float along = Vector2.Dot(offset, forward), across = Vector2.Dot(offset, side);
                        if (along >= 0f && along <= BeamLength && MathF.Abs(across) <= BeamHalfWidth)
                        {
                            Hurt(player, BeamDamage, null);
                            log.LogDebug($"Hycrest: All Aboard, beam hit {player.Name} ({along:0.0} m along, {across:0.0} m across).");
                        }
                    }
                });
        }

        private void CastVisual(uint spell4Id, Vector3 spot, float yaw, TimeSpan delay, TimeSpan? castTime = null)
        {
            if (Spawn(Battlecruiser, spot, yaw, FriendlyFaction) is not IUnitEntity caster)
                return;

            caster.IsInvulnerable = true;
            strikeCasters.Add((caster, delay.TotalSeconds + 6d));
            actionQueue.Enqueue(delay, () =>
            {
                if (!caster.InWorld)
                    return;
                ISpellParameters parameters = spellParametersFactory.Resolve();
                parameters.PrimaryTargetId        = caster.Guid;
                parameters.UserInitiatedSpellCast = false;
                if (castTime.HasValue)
                    parameters.CastTimeOverride = (int)castTime.Value.TotalMilliseconds;
                caster.CastSpell(spell4Id, parameters);
            });
        }

        /// <summary>
        /// The warship's fail-safes kick in: the last lines, the farmers run in and celebrate, then the mission ends.
        /// </summary>
        private void EndBombardment()
        {
            stage = Stage.Celebration;
            SetMusic(false);
            log.LogInformation("Hycrest: All Aboard, the bombardment is over.");

            Communicator(RhadmanFailSafes, MajorRhadman);
            TimeSpan time = LineGap;
            actionQueue.Enqueue(time, () => Communicator(ArsenaxCannotStop, ArsenaxSeverus));
            actionQueue.Enqueue(time += LineGap, () =>
            {
                Communicator(AyitaWeDidIt, AyitaSinnatus);
                FarmersRunIn();
            });
            actionQueue.Enqueue(time += LineGap, () => Communicator(AyitaTechnical, AyitaSinnatus));
            actionQueue.Enqueue(time += LineGap, () => Communicator(AyitaHighborns, AyitaSinnatus));
            // Ayita's last communicator shows 8 s: the church only after it (they talked through each other, Teun)
            actionQueue.Enqueue(time + TimeSpan.FromMilliseconds(CommunicatorMs) + TimeSpan.FromSeconds(1), () =>
            {
                log.LogInformation("Hycrest: All Aboard complete.");
                CompleteMission();
            });
        }

        private void FarmersRunIn()
        {
            // from the Bell Farmhouse's side (the train stays, they mustn't spawn in or run through it), to the party on that
            // side of the train
            Vector3 towardsFarm = Vector3.Normalize(new Vector3(BellFarmhouseSpot.X - arenaCentre.X, 0f, BellFarmhouseSpot.Z - arenaCentre.Z));
            var across = new Vector3(-towardsFarm.Z, 0f, towardsFarm.X);
            for (int i = 0; i < CelebratingFarmers; i++)
            {
                float spread = ((i % 7) - 3f) * 3f + (float)(Random.Shared.NextDouble() - 0.5);
                float depth  = (i / 7) * 3f;
                Vector3 from = Grounded(arenaCentre + towardsFarm * (ArenaRadius + 6f + depth) + across * spread);
                Vector3 to   = Grounded(arenaCentre + towardsFarm * (14f + depth + (float)Random.Shared.NextDouble() * 4f) + across * spread * 1.2f);

                // only the Detained Farmers without an action set: 17993/17995 have one (a crying idle that beats the emotes)
                uint creatureId = i == 0 ? AyitaSinnatus : (i % 2 == 0 ? 17992u : 17994u);
                if (Spawn(creatureId, from, MathF.Atan2(-(to.X - from.X), -(to.Z - from.Z)), FriendlyFaction) is not IUnitEntity farmer)
                    continue;

                celebrating.Add(farmer);
                actionQueue.Enqueue(TimeSpan.FromSeconds(0.5), () => RunTo(farmer, to));
            }

            // following and emotes start once they have run in
            followTimer = 8d;
            emoteTimer  = 7d;
        }

        /// <summary>
        /// The freed farmers stay around the players (each sticks to one of them) and keep cheering and dancing.
        /// </summary>
        private void UpdateCelebration(double lastTick)
        {
            if (celebrating.Count == 0)
                return;

            List<IPlayer> players = mapInstance.GetPlayers().ToList();
            followTimer -= lastTick;
            if (followTimer <= 0d && players.Count > 0)
            {
                followTimer = FollowInterval.TotalSeconds;
                for (int i = 0; i < celebrating.Count; i++)
                {
                    IUnitEntity farmer = celebrating[i];
                    IPlayer player = players[i % players.Count];
                    if (!farmer.InWorld || Vector3.Distance(farmer.Position, player.Position) <= FollowDistance)
                        continue;

                    float angle = MathF.Tau * i / celebrating.Count;
                    float distance = 3f + (i % 4);
                    RunTo(farmer, Grounded(player.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * distance));
                }
            }

            // staggered: one farmer at a time, 0.3 s apart (all at once gave a lag spike when they started, Teun)
            emoteTimer -= lastTick;
            if (emoteTimer <= 0d)
            {
                emoteTimer = EmoteInterval.TotalSeconds;
                for (int i = 0; i < celebrating.Count; i++)
                {
                    IUnitEntity farmer = celebrating[i];
                    uint emote = FarmerEmotes[(i + Random.Shared.Next(2)) % FarmerEmotes.Length];
                    actionQueue.Enqueue(TimeSpan.FromSeconds(0.3 * i), () =>
                    {
                        if (farmer.InWorld)
                            farmer.EnqueueToVisible(new ServerEmote { Guid = farmer.Guid, StandState = StandState.Emote, EmoteId = emote });
                    });
                }
            }
        }

        private static void RunTo(IUnitEntity farmer, Vector3 to)
        {
            if (!farmer.InWorld)
                return;
            farmer.MovementManager.SetMode(ModeType.Walk);
            farmer.MovementManager.LaunchSpline([farmer.Position, to], SplineType.Linear, SplineMode.OneShot, FarmerRunSpeed);
        }

        /// <summary>
        /// TEMPORARY dev aid: where the parked train and everything on it stand (world), and the locomotive's frame, so
        /// spots measured in game with !entity info can be turned into places on the train.
        /// </summary>
        private void LogParkedTrain()
        {
            float end = trainStart + TrainSpeed * (float)TrainTime.TotalSeconds;
            log.LogInformation($"Hycrest: All Aboard, PARKED train (dev): locomotive centre {RoutePoint(end)}, yaw {Yaw(end):0.0000}.");
            foreach (Passenger passenger in passengers)
                log.LogInformation($"Hycrest: All Aboard, parked: {passenger.Entity.CreatureId} (guid {passenger.Entity.Guid}) local {passenger.Local} world {passenger.Entity.Position}.");
            log.LogInformation("Hycrest: All Aboard, the train is parked at its end spot (DevParkTrain).");
        }

        // --- helpers ----------------------------------------------------------------------------------------------------

        /// <summary>
        /// The point <paramref name="distance"/> metres along the route.
        /// </summary>
        private Vector3 RoutePoint(float distance)
        {
            distance = Math.Clamp(distance, 0f, routeDistances[^1]);
            for (int i = 1; i < route.Length; i++)
            {
                if (distance > routeDistances[i])
                    continue;

                float length = routeDistances[i] - routeDistances[i - 1];
                return Vector3.Lerp(route[i - 1], route[i], length > 0f ? (distance - routeDistances[i - 1]) / length : 0f);
            }
            return route[^1];
        }

        /// <summary>
        /// The train's heading <paramref name="distance"/> metres along the route (front at -Z: yaw = atan2(-dx, -dz)).
        /// </summary>
        private float Yaw(float distance)
        {
            Vector3 direction = RoutePoint(distance + 3f) - RoutePoint(distance - 3f);
            return MathF.Atan2(-direction.X, -direction.Z);
        }

        /// <summary>
        /// World position of <paramref name="local"/> on a car whose centre is <paramref name="distance"/> metres along the
        /// route (the platform rotation the engine uses: x' = x cos + z sin, z' = -x sin + z cos).
        /// </summary>
        private Vector3 OnTrain(float distance, Vector3 local)
        {
            float yaw = Yaw(distance);
            float sin = MathF.Sin(yaw), cos = MathF.Cos(yaw);
            return RoutePoint(distance) + new Vector3(local.X * cos + local.Z * sin, local.Y, -local.X * sin + local.Z * cos);
        }

        private static float AngleTo(Vector3 from, float yaw, Vector3 to)
        {
            var forward = new Vector2(-MathF.Sin(yaw), -MathF.Cos(yaw));
            var offset = new Vector2(to.X - from.X, to.Z - from.Z);
            if (offset.LengthSquared() < 0.01f)
                return 0f;
            return MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(offset), forward), -1f, 1f));
        }

        private void SetMusic(bool on)
        {
            if (!PlayFinaleMusic)
                return;

            foreach (IPlayer player in mapInstance.GetPlayers())
            {
                if (!on)
                {
                    player.GetSpellBySpellId(FinaleMusic)?.Finish();
                    continue;
                }

                ISpellParameters parameters = spellParametersFactory.Resolve();
                parameters.PrimaryTargetId        = player.Guid;
                parameters.UserInitiatedSpellCast = false;
                player.CastSpell(FinaleMusic, parameters);
            }
        }

        // safety net: a knockdown from the orbital strikes once never ended (Teun, 2 Oct 2026: stunned until the end) - any
        // crowd control still on a player after this long is removed (the strikes' knockdowns last 1.5 s)
        private static readonly TimeSpan StuckCrowdControl = TimeSpan.FromSeconds(4);
        private readonly Dictionary<(uint Player, CCState State), double> crowdControlSeen = [];
        private double crowdControlCheck;

        private void ClearStuckCrowdControl(double lastTick)
        {
            crowdControlCheck -= lastTick;
            if (crowdControlCheck > 0d)
                return;
            crowdControlCheck = 0.5d;

            var current = new HashSet<(uint, CCState)>();
            foreach (IPlayer player in mapInstance.GetPlayers())
            {
                foreach (CCState state in player.CrowdControlManager.GetCCStates().ToList())
                {
                    var key = (player.Guid, state);
                    current.Add(key);
                    double seen = crowdControlSeen.GetValueOrDefault(key) + 0.5d;
                    crowdControlSeen[key] = seen;
                    if (seen < StuckCrowdControl.TotalSeconds)
                        continue;

                    uint effectId = player.CrowdControlManager.GetCCEffect(state)?.EffectId ?? 0u;
                    player.CrowdControlManager.RemoveCCEffect(state);
                    player.EnqueueToVisible(new ServerEntityCCStateRemove
                    {
                        Guid           = player.Guid,
                        CCState        = state,
                        EffectUniqueId = effectId,
                        Removed        = true
                    }, true);
                    crowdControlSeen.Remove(key);
                    log.LogWarning($"Hycrest: All Aboard, removed a stuck {state} from {player.Name}.");
                }
            }

            foreach (var key in crowdControlSeen.Keys.Where(k => !current.Contains(k)).ToList())
                crowdControlSeen.Remove(key);
        }

        // the fight is inside the dome (retail): anyone outside it is put back in, near the edge on their side (Teun)
        private const float ArenaInside = ArenaRadius - 6f;
        private double arenaCheck;

        private void KeepPlayersInArena()
        {
            foreach (IPlayer player in mapInstance.GetPlayers())
            {
                var offset = new Vector3(player.Position.X - arenaCentre.X, 0f, player.Position.Z - arenaCentre.Z);
                if (offset.Length() <= ArenaRadius || !player.CanTeleport())
                    continue;

                Vector3 inside = Grounded(arenaCentre + Vector3.Normalize(offset) * ArenaInside) + new Vector3(0f, 1f, 0f);
                player.TeleportToLocal(inside, false);
                log.LogInformation($"Hycrest: All Aboard, {player.Name} was outside the arena: moved inside.");
            }
        }

        private void Communicator(uint textId, uint creatureId)
        {
            foreach (IPlayer player in mapInstance.GetPlayers())
                storyBuilder.SendStoryCommunicator(textId, creatureId, player, CommunicatorMs);
        }

        private IWorldEntity Spawn(uint creatureId, Vector3 position, float yaw, Faction? faction)
        {
            ICreatureInfo creatureInfo = creatureInfoManager.GetCreatureInfo(creatureId);
            if (creatureInfo == null)
            {
                log.LogWarning($"Hycrest: All Aboard, no creature info for {creatureId}.");
                return null;
            }

            // (the cars as plain units jittered even standing still, Teun: platforms again; their collision jitter is benched)
            IWorldEntity entity = creatureInfo.Entry.CreationTypeEnum == EntityType.Platform
                ? publicEvent.CreateEntity<IPlatformEntity>()
                : publicEvent.CreateEntity<INonPlayerEntity>();
            entity.Initialise(creatureInfo);
            if (faction.HasValue)
            {
                entity.Faction1 = faction.Value;
                entity.Faction2 = faction.Value;
            }
            entity.Rotation = new Vector3(yaw, 0f, 0f);
            entity.AddToMap(mapInstance, position);
            return entity;
        }

        private Vector3 Grounded(Vector3 position)
        {
            float? height = mapInstance.GetTerrainHeight(position.X, position.Z);
            if (height.HasValue)
                position.Y = height.Value;
            return position;
        }

        /// <summary>
        /// The mission is over (or the hideout closes): the fight's leftovers go.
        /// </summary>
        public override void OnMissionEnded()
        {
            base.OnMissionEnded();
            riders.Clear();
            passengers.Clear();
            trainParts.Clear();
        }
    }
}
