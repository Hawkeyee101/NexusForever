using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Trigger;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;
using NexusForever.Shared;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    /// <summary>
    /// The Hycrest Insurrection (public event 419): the barn scene after the intro, then the run from mission to mission
    /// (see <see cref="HycrestMissions"/>): mission votes, the interlude, the finale and a regroup at a hideout in between.
    /// </summary>
    [ScriptFilterOwnerId(HycrestPublicEvent.Main)]
    public class TheHycrestInsurrectionEventScript : IPublicEventScript, IOwnedScript<IPublicEvent>, IHycrestMainEventScript
    {
        // what happens when a mission wins its vote: the track's spokesperson gives the outcome line and the story
        // communicator is the mission's setup message (known for the first vote; none for The Science of Revenge)
        private static readonly Dictionary<uint, (PublicEventCreature Speaker, uint OutcomeText, uint CommunicatorText)> MissionOutcomes = new()
        {
            [420u] = (PublicEventCreature.AyitaSinnatus,  465550u, 444047u), // "Prema's father Tarquim is right outside..."
            [421u] = (PublicEventCreature.VesnaTaranoft,  465551u, 0u),      // "Be quick about finding Groo..."
            [422u] = (PublicEventCreature.LysionSinnatus, 465552u, 444107u), // "Once we've compromised their shields..."
            // vote 46; Vesna says the families line in the retail video; communicators not known yet
            [423u] = (PublicEventCreature.VesnaTaranoft,  465556u, 0u),      // "Thank you. Those families deserve the chance..."
            [424u] = (PublicEventCreature.VesnaTaranoft,  465557u, 0u),      // "Be careful as you move around the city..."
            [425u] = (PublicEventCreature.LysionSinnatus, 465558u, 0u),      // "Deliver my army to freedom..."
            // vote 48 (speaker inferred from the track)
            [429u] = (PublicEventCreature.AyitaSinnatus,  465559u, 444074u)  // "Dominion security is tight..." / "...special security clearance."
        };

        // the scene before a vote at a regroup (retail video, Sinnatus's Barn after The Farmer's Daughter): the lead lines,
        // then the vote starts with the first line of the argument, which goes on while the players vote
        private static readonly Dictionary<uint, ((PublicEventCreature Speaker, uint TextId)[] Lead, (PublicEventCreature Speaker, uint TextId)[] DuringVote)> VoteScenes = new()
        {
            [46u] = (
                [
                    (PublicEventCreature.AyitaSinnatus,  466943u)  // "Father, that's not fair! Vesna has seen to everything we need..."
                ],
                [
                    (PublicEventCreature.LysionSinnatus, 464175u), // "I don't know why we're even discussing this... we'll need an army!"
                    (PublicEventCreature.VesnaTaranoft,  464191u), // "We have all the army we need right here, Lysion..."
                    (PublicEventCreature.AyitaSinnatus,  464213u), // "Have you two forgotten what we're fighting for?..."
                    (PublicEventCreature.VesnaTaranoft,  464254u), // "We also need the people of Hycrest on our side..."
                    (PublicEventCreature.LysionSinnatus, 464259u)  // "Enough! Neither of those things is of any use..."
                ]),
            // Arcwulff Farm after Breach of Protocol (retail video: the vote comes at once, Vesna and Ayita argue during it;
            // the video stops after Ayita's first line, the rest are the neighbouring ids, order assumed)
            [48u] = (
                [],
                [
                    (PublicEventCreature.VesnaTaranoft,  464268u), // "Lysion, we can't distribute munitions and supplies we haven't cleared!"
                    (PublicEventCreature.AyitaSinnatus,  464272u), // "Arming the rebellion can't be our only priority..."
                    (PublicEventCreature.VesnaTaranoft,  464274u), // "We can't arm the rebellion with weapons we haven't checked..."
                    (PublicEventCreature.AyitaSinnatus,  464276u), // "Those people need our help!"
                    (PublicEventCreature.VesnaTaranoft,  464278u)  // "I understand, but our security has to come first..."
                ])
        };

        // barn scene, en-US text ids in speaking order (retail video); gestures for Vesna and Lysion
        private const uint BarnArrivalLine = 466926u; // Ayita: "Sometimes I worry about you, Father..."
        private static readonly (PublicEventCreature Speaker, uint TextId)[] BarnBriefing =
        [
            (PublicEventCreature.VesnaTaranoft,  160643u), // "Ah, you are here at last. Let us begin the briefing."
            (PublicEventCreature.VesnaTaranoft,  160648u), // "This is Lysion Sinnatus and his daughter, Ayita..."
            (PublicEventCreature.AyitaSinnatus,  160644u), // "All we want is to leave this horrid place..."
            (PublicEventCreature.LysionSinnatus, 160649u), // "To hell with leavin'. This is our home!..."
            (PublicEventCreature.VesnaTaranoft,  160650u), // "As you can see, we've had a few disagreements..."
            (PublicEventCreature.VesnaTaranoft,  455329u)  // "Regardless, there's plenty of work to be done..."
        ];
        private static readonly (PublicEventCreature Speaker, uint TextId)[] MissionVotePitches =
        [
            (PublicEventCreature.AyitaSinnatus,  464075u), // Merciful: "We have to save Prema and Milithia!..."
            (PublicEventCreature.VesnaTaranoft,  464079u), // Tactical: "We can't let emotion cloud our vision..."
            (PublicEventCreature.LysionSinnatus, 464076u)  // Militant: "Ayita, how many times must I tell you?..."
        ];

        // speech pacing: at least MinLineSeconds per line, longer lines get more time
        private const double MinLineSeconds = 3d;
        private const double CharactersPerSecond = 20d;

        // first barn (from the retail video): Vesna starts while Ayita's arrival line is still up, and the briefing
        // runs faster, each line this much shorter (never shorter than the minimum gap)
        private const double BarnBriefingOverlapStart = 2d;
        private static readonly TimeSpan BarnLineShortening = TimeSpan.FromSeconds(1.5);
        private static readonly TimeSpan BarnMinLineGap = TimeSpan.FromSeconds(1.5);

        private const uint CommunicatorDurationMs = 10000u;
        private static readonly TimeSpan OutcomeCommunicatorDelay = TimeSpan.FromSeconds(1.5);
        private static readonly TimeSpan OutcomeMissionDelay = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan NextMissionDelay = TimeSpan.FromSeconds(5);

        private IPublicEvent publicEvent;
        private IMapInstance mapInstance;

        private bool voteInProgress;
        private IPublicEvent mission;
        // the last finished mission: what it kept on the map (e.g. its enemies) goes once the hideout's barn closes
        private IPublicEvent finishedMission;
        private IPublicEvent regroup;

        // the run so far: the tier of the current mission and the track chosen on each tier
        // TEMPORARY dev shortcut (Teun, 1 Oct 2026, to speed up testing; not for upstream): a fresh instance skips the intro
        // and starts straight in this mission, as if the route before it was played, and moves joining players to
        // DevStartSpot. 0 = the normal run. The tracks below lead to Breach of Protocol (Farmer's Daughter, The Great Escape)
        private const uint DevStartMission = 0u;
        private static readonly HycrestTrack[] DevStartTracks = [HycrestTrack.Merciful, HycrestTrack.Merciful, HycrestTrack.Merciful, HycrestTrack.Merciful];
        private static readonly Vector3 DevStartSpot = new(-2259.5f, -924.3f, -1332f);   // All Aboard: in the Bell Farmhouse, by Ayita
        private static readonly TimeSpan DevTeleportDelay = TimeSpan.FromSeconds(4);
        private readonly HashSet<ulong> devTeleported = [];

        private int tier = -1;
        private readonly HycrestTrack[] tracks = new HycrestTrack[HycrestMissions.TierCount];

        private readonly TimedActionQueue sceneQueue = new();
        // the story NPCs can stand in more than one hideout (Abandoned Barn, and the one the players regroup at)
        private readonly Dictionary<PublicEventCreature, List<uint>> npcGuids = [];

        // phase of the main event with the NPCs of a hideout, set when the players regroup there
        private static readonly Dictionary<uint, uint> HideoutPhases = new()
        {
            [HycrestMissions.RegroupSinnatusBarn] = 11u,
            [HycrestMissions.RegroupArcwulffFarm] = 13u,
            [HycrestMissions.RegroupBellFarmhouse] = 14u
        };

        // hideouts where Ayita stands: the Arcwulff farmhouse (after Breach of Protocol) and the Bell Farmhouse (after
        // Clearance, ready to start All Aboard) (Teun, 1 Oct 2026)
        private static readonly HashSet<uint> StandingHideoutPhases = [13u, 14u];

        // the finale's setup communicator, once the tier 4 mission has gathered everyone at its hideout
        private static readonly Dictionary<uint, (PublicEventCreature Speaker, uint TextId)> FinaleCommunicators = new()
        {
            [429u] = (PublicEventCreature.AyitaSinnatus, 444080u) // "This is just terrible! ...'relocation transports!'"
        };
        private static readonly TimeSpan FinaleCommunicatorDelay = TimeSpan.FromSeconds(2);

        // guests at a hideout (Arcwulff Farm after Breach of Protocol: Prema; Tarquim and Millithea are in no video): they
        // leave when its door closes
        private static readonly HashSet<uint> HideoutGuests = [17772u];
        private readonly List<uint> hideoutGuests = [];

        // the sky once everyone is in the hideout (retail video: back to daylight when Arcwulff Farm closes after Breach
        // of Protocol)
        private static readonly Dictionary<uint, uint> SkyAtHideout = new()
        {
            [426u] = MorningSkySpell
        };
        private readonly List<uint> barnDoors = [];

        // time of day (retail video): the scenario after the regroup scene of vote 46 is in the morning. World 1149 has
        // fixed sky files (night), the day cycle (ServerTimeOfDay) doesn't change it; the adventure switches skies with
        // spells on the players: "Hycrest Adventure - Daytime Skybox" (27013, Quick 48796, Long 50044) and "Nighttime
        // Skybox" (27236, 48797, 50045), a Fluff aura with the sky visual plus a force remove of the other skies.
        // Players entering later get it after they are on the map.
        // the Long variants: the Quick day sky (48796) was removed again right after it was cast (30 Sep 2026, the barn
        // stayed night); retail switches the sky at once while the barn doors are closed, the Long ones fade slowly
        private const uint MorningSkySpell = 50044u;
        private const uint NightSkySpell = 50045u;
        private const uint CaretakerSkyGreen = 45375u;   // "Adventures - Caretaker Sky Green", the sync's green screen

        // the sky once a mission is done (retail video: back to night when The Great Escape returns to the barn)
        private static readonly Dictionary<uint, uint> SkyAfterMission = new()
        {
            [423u] = NightSkySpell
        };
        private static readonly TimeSpan SkyJoinDelay = TimeSpan.FromSeconds(1);
        private uint? skySpell;

        // a sky change is due: the barn doors stay closed until it has been cast
        private bool timeOfDayPending;

        // barn doors (retail video): no door while the doorway is open; closing spawns the barn's door (its own phase of
        // the main event), opening removes it, no animation
        private static readonly Dictionary<uint, uint> BarnDoorPhases = new()
        {
            [HycrestMissions.RegroupAbandonedBarn] = 20u,
            [HycrestMissions.RegroupSinnatusBarn]  = 21u,
            [HycrestMissions.RegroupArcwulffFarm]  = 22u, // the farmhouse door (Farmhouse Door - Platform 51065)
            [HycrestMissions.RegroupBellFarmhouse] = 23u  // the same door, Bell Farmhouse
        };

        // the doors open once the mission after a vote has everything on the map (any layout): all its spawns added
        private IPublicEvent openDoorsFor;
        // at least this long after the mission appears (and once its spawns are all on the map)
        private static readonly TimeSpan MinDoorOpenDelay = TimeSpan.FromSeconds(2);
        // the first barn (after vote 45) opens sooner again, as at first: its door vanishing was guid reuse (fixed), not
        // the timing (Teun, 1 Oct 2026)
        private static readonly TimeSpan FirstBarnDoorOpenDelay = TimeSpan.FromSeconds(1);
        private double openDoorsWait;
        // a door that has just closed stays closed at least this long (after The Great Escape the next mission follows
        // right away, the door reopened the moment it closed)
        private static readonly TimeSpan MinDoorClosedTime = TimeSpan.FromSeconds(8);
        private double doorClosedAt = double.MinValue;
        private uint regroupObjective;
        private uint hideoutPhase;

        private bool barnArrivalPlayed;
        private double sceneClock;
        private double barnArrivalLineEnd;

        #region Dependency Injection

        private readonly ILogger<TheHycrestInsurrectionEventScript> log;
        private readonly IGameTableManager gameTableManager;
        private readonly IStoryBuilder storyBuilder;
        private readonly IFactory<ISpellParameters> spellParametersFactory;
        private readonly HycrestDialogue dialogue;

        public TheHycrestInsurrectionEventScript(
            ILogger<TheHycrestInsurrectionEventScript> log,
            IGameTableManager gameTableManager,
            IStoryBuilder storyBuilder,
            IFactory<ISpellParameters> spellParametersFactory)
        {
            this.log                    = log;
            this.gameTableManager       = gameTableManager;
            this.storyBuilder           = storyBuilder;
            this.spellParametersFactory = spellParametersFactory;
            dialogue = new HycrestDialogue(gameTableManager, sceneQueue);
        }

        #endregion

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public void OnLoad(IPublicEvent owner)
        {
            publicEvent = owner;
            mapInstance = publicEvent.Map as IMapInstance;

            // spawns Vesna Taranoft and Ayita Sinnatus in the Abandoned Barn
            publicEvent.SetPhase(0u);

            if (DevStartMission != 0u)
            {
                DevStart();
                return;
            }

            // the intro is a separate root event, players are joined to it by the map script
            publicEvent.Map.PublicEventManager.CreateEvent(HycrestPublicEvent.Intro);
        }

        /// <summary>
        /// TEMPORARY dev shortcut, see <see cref="DevStartMission"/>.
        /// </summary>
        private void DevStart()
        {
            if (!HycrestMissions.TryGetTierAndTrack(DevStartMission, out int devTier, out _))
                return;

            for (int i = 0; i < devTier && i < DevStartTracks.Length; i++)
                tracks[i] = DevStartTracks[i];
            // daylight again from the Arcwulff farmhouse (after Breach of Protocol) on
            skySpell = devTier > HycrestMissions.InterludeTier ? MorningSkySpell : NightSkySpell;
            // the finale starts in the tier 4 mission's hideout (All Aboard: Ayita standing in the Bell Farmhouse)
            if (devTier == HycrestMissions.FinaleTier
                && HycrestMissions.RegroupAfter.TryGetValue(HycrestMissions.GetMission(devTier - 1, tracks[devTier - 1]), out uint devHideout))
                MoveNpcsTo(devHideout);
            sceneQueue.Enqueue(TimeSpan.Zero, () => StartMission(DevStartMission));
            log.LogWarning($"Hycrest: DEV START at mission {DevStartMission} (TheHycrestInsurrectionEventScript.DevStartMission), no intro.");
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        /// <remarks>
        /// Forwarded by <see cref="TheHycrestInsurrectionMapScript"/> before the public event manager updates.
        /// </remarks>
        public void Update(double lastTick)
        {
            // runs before the public event manager update, so actions here may create events (see OnVoteFinished)
            sceneClock += lastTick;
            sceneQueue.Update(lastTick);
            UpdateBarnDoors(lastTick);
            UpdateSeated(lastTick);
        }

        // Ayita sits wherever she is (hay bales, the barns), except in the hideouts where she stands
        // (StandingHideoutPhases). The sit goes out again now and then, like Breach's crying
        private static readonly TimeSpan SitResend = TimeSpan.FromSeconds(5);
        private double sitTimer;

        private void UpdateSeated(double lastTick)
        {
            sitTimer -= lastTick;
            if (sitTimer > 0d)
                return;

            sitTimer = SitResend.TotalSeconds;
            if (StandingHideoutPhases.Contains(hideoutPhase))
                return;
            if (!npcGuids.TryGetValue(PublicEventCreature.AyitaSinnatus, out List<uint> guids))
                return;

            foreach (uint guid in guids)
                if (mapInstance.GetEntity<IWorldEntity>(guid) is { InWorld: true, CreatureId: (uint)PublicEventCreature.AyitaSinnatus } ayita)
                    ayita.StandState = StandState.Sit;
        }

        private void UpdateBarnDoors(double lastTick)
        {
            // open the doors once the new mission has all its spawns on the map, whichever layout it picked
            if (openDoorsFor == null)
                return;

            openDoorsWait += lastTick;
            // never before the new time of day is set, nor before the mission has everything on the map
            if (timeOfDayPending
                || openDoorsWait < (HycrestMissions.TryGetTierAndTrack(openDoorsFor.Id, out int tier, out _) && tier == 0
                    ? FirstBarnDoorOpenDelay : MinDoorOpenDelay).TotalSeconds
                || sceneClock - doorClosedAt < MinDoorClosedTime.TotalSeconds
                || !openDoorsFor.HasFinished && openDoorsFor.GetEntities().Any(e => !e.InWorld))
                return;

            openDoorsFor = null;
            OpenBarnDoors();
            log.LogInformation("Hycrest: mission spawns loaded, barn doors open.");
        }

        /// <summary>
        /// Invoked when a <see cref="IGridEntity"/> is added to the map the public event is on.
        /// </summary>
        public void OnAddToMap(IGridEntity entity)
        {
            switch (entity)
            {
                case IPlayer player:
                {
                    // late joiners also need to join the current mission and the regroup, the map script only joins them
                    // to the main and intro events
                    if (mission != null)
                        HycrestPublicEvent.JoinPublicTeam(mission, player);
                    if (regroup != null)
                    {
                        HycrestPublicEvent.JoinPublicTeam(regroup, player);
                        UpdateRegroupParticipants();
                    }

                    // TEMPORARY dev shortcut: once per player, to the start of the mission
                    if (DevStartMission != 0u && devTeleported.Add(player.CharacterId))
                    {
                        uint devGuid = player.Guid;
                        sceneQueue.Enqueue(DevTeleportDelay, () =>
                        {
                            if (mapInstance.GetEntity<IPlayer>(devGuid) is IPlayer joined && joined.CanTeleport())
                                joined.TeleportToLocal(DevStartSpot, false);
                        });
                    }

                    if (skySpell.HasValue)
                    {
                        uint guid = player.Guid;
                        sceneQueue.Enqueue(SkyJoinDelay, () =>
                        {
                            if (mapInstance.GetEntity<IPlayer>(guid) is IPlayer joined && skySpell.HasValue)
                                CastSky(joined, skySpell.Value);
                        });
                    }
                    break;
                }
                case IWorldEntity worldEntity:
                    OnAddToMapWorldEntity(worldEntity);
                    break;
            }
        }

        /// <summary>
        /// Invoked when a <see cref="IGridEntity"/> is removed from the map the public event is on.
        /// </summary>
        public void OnRemoveFromMap(IGridEntity entity)
        {
            // a player leaving no longer has to reach the hideout; counted on the next tick, once they are gone
            if (entity is IPlayer)
                sceneQueue.Enqueue(TimeSpan.Zero, UpdateRegroupParticipants);
        }

        private void OnAddToMapWorldEntity(IWorldEntity worldEntity)
        {
            if (HideoutGuests.Contains(worldEntity.CreatureId) && publicEvent.GetEntities().Contains(worldEntity))
            {
                hideoutGuests.Add(worldEntity.Guid);
                return;
            }

            var creature = (PublicEventCreature)worldEntity.CreatureId;
            switch (creature)
            {
                case PublicEventCreature.VesnaTaranoft:
                case PublicEventCreature.LysionSinnatus:
                    AddNpc(creature, worldEntity.Guid);
                    break;
                case PublicEventCreature.CaretakerHologram:
                    // the end scene's Caretaker in the church
                    if (publicEvent.GetEntities().Contains(worldEntity))
                        caretakerGuid = worldEntity.Guid;
                    break;
                case PublicEventCreature.BarnDoor:
                case PublicEventCreature.FarmhouseDoor:
                    // spawned by CloseBarnDoor, removed by OpenBarnDoors
                    barnDoors.Add(worldEntity.Guid);
                    break;
                case PublicEventCreature.AyitaSinnatus:
                {
                    AddNpc(creature, worldEntity.Guid);
                    // retail: she sits on top of the hay bales; stand state is a stat, so players arriving later see it too
                    if (!StandingHideoutPhases.Contains(hideoutPhase))
                        worldEntity.StandState = StandState.Sit;
                    break;
                }
            }
        }

        private void AddNpc(PublicEventCreature creature, uint guid)
        {
            if (!npcGuids.TryGetValue(creature, out List<uint> guids))
                npcGuids[creature] = guids = [];
            guids.Add(guid);
        }

        /// <summary>
        /// Return the NPC nearest to the players, the one in the hideout they are at.
        /// </summary>
        private IWorldEntity GetNpc(PublicEventCreature creature)
        {
            if (!npcGuids.TryGetValue(creature, out List<uint> guids))
                return null;

            Vector3? players = mapInstance.GetPlayers().FirstOrDefault()?.Position;
            return guids
                .Select(guid => mapInstance.GetEntity<IWorldEntity>(guid))
                .Where(e => e != null)
                .OrderBy(e => players.HasValue ? Vector3.Distance(e.Position, players.Value) : 0f)
                .FirstOrDefault();
        }

        private static bool UsesGesture(PublicEventCreature speaker)
        {
            return speaker is PublicEventCreature.VesnaTaranoft or PublicEventCreature.LysionSinnatus;
        }

        private TimeSpan GetLineDuration(uint textId)
        {
            return TimeSpan.FromSeconds(Math.Max(MinLineSeconds, dialogue.GetText(textId).Length / CharactersPerSecond));
        }

        /// <summary>
        /// Queue <paramref name="lines"/> one after another starting after <paramref name="start"/>, returns when the last line ends.
        /// </summary>
        private TimeSpan QueueLines(TimeSpan start, IEnumerable<(PublicEventCreature Speaker, uint TextId)> lines, bool whileVoting = false,
            TimeSpan shortening = default, bool gestures = false)
        {
            TimeSpan time = start;
            var list = lines.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                (PublicEventCreature speaker, uint textId) = list[i];
                sceneQueue.Enqueue(time, () =>
                {
                    // the argument during a vote stops once the vote is over (solo, it ends as soon as you vote)
                    if (!whileVoting || voteInProgress)
                        dialogue.Say(GetNpc(speaker), textId, gestures && speaker != PublicEventCreature.AyitaSinnatus || UsesGesture(speaker));
                });

                // shortened only when someone else speaks next: an NPC's next line replaces its speech bubble, so its own
                // lines back to back keep their full time (they were cut off)
                bool sameSpeakerNext = i + 1 < list.Count && list[i + 1].Speaker == speaker;
                TimeSpan duration = GetLineDuration(textId) - (sameSpeakerNext ? TimeSpan.Zero : shortening);
                time += shortening > TimeSpan.Zero && duration < BarnMinLineGap ? BarnMinLineGap : duration;
            }

            return time;
        }

        /// <summary>
        /// Invoked by <see cref="TheHycrestInsurrectionIntroEventScript"/> when the first player enters the Abandoned Barn.
        /// </summary>
        public void OnFirstBarnArrival()
        {
            if (barnArrivalPlayed)
                return;

            barnArrivalPlayed  = true;
            barnArrivalLineEnd = sceneClock + BarnBriefingOverlapStart;
            dialogue.Say(GetNpc(PublicEventCreature.AyitaSinnatus), BarnArrivalLine, gesture: false);
        }

        /// <summary>
        /// Invoked by <see cref="TheHycrestInsurrectionIntroEventScript"/> when the intro has been completed.
        /// </summary>
        public void OnIntroComplete()
        {
            // everyone is gathered: Vesna's briefing, the three pitches, then the vote
            // solo, the first arrival also completes 189: Vesna starts shortly after Ayita's arrival line, while it is still up
            TimeSpan start = TimeSpan.FromSeconds(Math.Max(0d, barnArrivalLineEnd - sceneClock));
            CloseBarnDoor(HycrestMissions.RegroupAbandonedBarn);
            TimeSpan time = QueueLines(start, BarnBriefing, shortening: BarnLineShortening);
            time = QueueLines(time, MissionVotePitches, shortening: BarnLineShortening);
            sceneQueue.Enqueue(time, () => StartVote(HycrestMissions.GetVote(0, HycrestTrack.Tactical)));
        }

        /// <summary>
        /// Start a mission vote, unless one is already in progress.
        /// </summary>
        private void StartVote(uint voteId)
        {
            if (voteInProgress)
                return;

            if (publicEvent.HasFinished)
            {
                // a finished event no longer ticks, so the vote would never time out
                log.LogWarning($"Hycrest: public event {HycrestPublicEvent.Main} has finished, start a new instance.");
                return;
            }

            voteInProgress = true;
            publicEvent.StartVote(PublicEventTeam.PublicTeam, voteId, 0u);
            log.LogInformation($"Hycrest: started vote {voteId} for public event {HycrestPublicEvent.Main}.");
        }

        /// <summary>
        /// Invoked when a vote on the public event has finished.
        /// </summary>
        public void OnVoteFinished(uint voteId, uint winner)
        {
            voteInProgress = false;

            string label = null;

            PublicEventVoteEntry entry = gameTableManager.PublicEventVote.GetEntry(voteId);
            if (entry != null && winner < entry.LocalizedTextIdLabel.Length)
                label = gameTableManager.GetTextTable(Language.English).GetEntry(entry.LocalizedTextIdLabel[winner]);

            log.LogInformation($"Hycrest: vote {voteId} for public event {publicEvent.Id} finished, winner {winner} ({label ?? "unknown"}).");

            int voteTier = voteId switch
            {
                45u => 0,
                46u => 1,
                47u or 48u or 49u => 3,
                _ => -1
            };
            if (voteTier < 0)
                return;

            uint missionId = HycrestMissions.GetMission(voteTier, HycrestMissions.GetVoteTrack(voteId, winner));

            // after the regroup scene the story moves on to the next morning
            if (voteId == 46u)
            {
                timeOfDayPending = true;
                SetSky(MorningSkySpell);
            }

            // outcome line from the track's spokesperson, the mission's story communicator, then the mission
            // a vote that times out finishes during the public event manager update, creating an event there would modify
            // the collection being enumerated, so everything runs from the scene queue on the following ticks
            if (MissionOutcomes.TryGetValue(missionId, out var outcome))
            {
                (PublicEventCreature speaker, uint outcomeText, uint communicatorText) = outcome;
                sceneQueue.Enqueue(TimeSpan.Zero, () => dialogue.Say(GetNpc(speaker), outcomeText, UsesGesture(speaker)));
                if (communicatorText != 0u)
                    sceneQueue.Enqueue(OutcomeCommunicatorDelay, () =>
                    {
                        foreach (IPlayer player in mapInstance.GetPlayers())
                            storyBuilder.SendStoryCommunicator(communicatorText, (uint)speaker, player, CommunicatorDurationMs);
                    });
            }

            sceneQueue.Enqueue(OutcomeMissionDelay, () => StartMission(missionId));
        }

        private void StartMission(uint missionId)
        {
            // a finished mission can be created again, a running one can't
            if (publicEvent.Map.PublicEventManager.GetEvent(missionId)?.HasFinished == false)
            {
                log.LogInformation($"Hycrest: mission {missionId} is already running.");
                return;
            }

            if (!HycrestMissions.TryGetTierAndTrack(missionId, out int missionTier, out HycrestTrack track))
                return;

            mission = publicEvent.Map.PublicEventManager.CreateEvent(missionId);
            if (mission == null)
            {
                log.LogError($"Hycrest: failed to create mission {missionId}.");
                return;
            }

            tier = missionTier;
            tracks[missionTier] = track;

            // the barn doors open once the mission's spawns are all on the map, see UpdateBarnDoors
            openDoorsFor  = mission;
            openDoorsWait = 0d;

            foreach (IPlayer player in mapInstance.GetPlayers())
                HycrestPublicEvent.JoinPublicTeam(mission, player);

            log.LogInformation($"Hycrest: started mission {missionId} (tier {missionTier + 1}, {track}) with {mapInstance.PlayerCount} player(s).");
        }

        /// <summary>
        /// Invoked when a mission has finished.
        /// </summary>
        public void OnMissionFinished(IPublicEvent finished)
        {
            // may be invoked during the public event manager update, see OnVoteFinished
            uint missionId = finished.Id;
            sceneQueue.Enqueue(TimeSpan.Zero, () => AfterMission(finished, missionId));
        }

        private void AfterMission(IPublicEvent finished, uint missionId)
        {
            finished.InvokeScriptCollection<IHycrestMissionScript>(s => s.OnMissionEnded());
            if (mission == finished)
                mission = null;
            finishedMission = finished;

            if (!HycrestMissions.TryGetTierAndTrack(missionId, out int missionTier, out HycrestTrack track))
                return;

            log.LogInformation($"Hycrest: mission {missionId} (tier {missionTier + 1}, {track}) finished.");

            if (missionTier == HycrestMissions.FinaleTier)
            {
                StartEndScene();
                return;
            }

            // the finale follows the tier 4 track right away, it starts where the tier 4 mission ended
            if (missionTier == HycrestMissions.FinaleTier - 1)
            {
                if (HycrestMissions.EndsAtHideout.Contains(missionId))
                    CloseFinaleHideout(missionId);
                sceneQueue.Enqueue(NextMissionDelay, () => StartMission(HycrestMissions.GetMission(HycrestMissions.FinaleTier, track)));
                return;
            }

            if (SkyAfterMission.TryGetValue(missionId, out uint sky))
                SetSky(sky);

            // the mission's last objective already gathered everyone at the hideout
            if (HycrestMissions.EndsAtHideout.Contains(missionId))
            {
                regroupObjective = HycrestMissions.RegroupAfter[missionId];
                MoveNpcsTo(regroupObjective);
                OnRegroupComplete();
                return;
            }

            StartRegroup(HycrestMissions.RegroupAfter[missionId]);
        }

        /// <summary>
        /// The tier 4 mission ended in its hideout: the door closes behind the players (the mission's leftovers go), no
        /// vote follows; the finale's setup communicator comes in and the finale starts there.
        /// </summary>
        private void CloseFinaleHideout(uint missionId)
        {
            regroupObjective = HycrestMissions.RegroupAfter[missionId];
            MoveNpcsTo(regroupObjective);

            finishedMission?.InvokeScriptCollection<IHycrestMissionScript>(s => s.OnHideoutClosed());
            finishedMission = null;
            foreach (uint guid in hideoutGuests)
                if (mapInstance.GetEntity<IWorldEntity>(guid) is { InWorld: true } guest)
                    guest.RemoveFromMap();
            hideoutGuests.Clear();

            CloseBarnDoor(regroupObjective);

            if (FinaleCommunicators.TryGetValue(missionId, out var communicator))
                sceneQueue.Enqueue(FinaleCommunicatorDelay, () =>
                {
                    foreach (IPlayer player in mapInstance.GetPlayers())
                        storyBuilder.SendStoryCommunicator(communicator.TextId, (uint)communicator.Speaker, player, CommunicatorDurationMs);
                });
        }

        private void StartRegroup(uint objectiveId)
        {
            // each regroup is its own event: it finishes once everyone has arrived, which takes it off the tracker
            if (regroup == null || regroup.HasFinished)
            {
                regroup = publicEvent.Map.PublicEventManager.CreateEvent(HycrestPublicEvent.Regroup);
                if (regroup == null)
                {
                    log.LogError($"Hycrest: failed to create regroup event {HycrestPublicEvent.Regroup}.");
                    return;
                }

                foreach (IPlayer player in mapInstance.GetPlayers())
                    HycrestPublicEvent.JoinPublicTeam(regroup, player);
            }

            regroupObjective = objectiveId;
            MoveNpcsTo(objectiveId);
            regroup.InvokeScriptCollection<IHycrestRegroupScript>(s => s.StartRegroup(objectiveId, (uint)mapInstance.PlayerCount));
        }

        private void SetSky(uint spell4Id)
        {
            skySpell = spell4Id;
            foreach (IPlayer player in mapInstance.GetPlayers())
                CastSky(player, spell4Id);
            timeOfDayPending = false;
            log.LogInformation($"Hycrest: sky spell {spell4Id} cast on the players.");
        }

        // the skybox spells share spell group 307; each removes the others (SpellForceRemove) when it applies its own sky.
        // A day sky cast over the night sky didn't show (1 Oct 2026, after Breach of Protocol, logged as cast): the new
        // sky reaches the client first, then the old one's removal, and the client falls back to the map's own (night)
        // sky. So the old sky goes first and the new one follows a moment later
        private const uint SkySpellGroup = 307u;
        private static readonly TimeSpan SkyRecastDelay = TimeSpan.FromSeconds(0.5);

        private void CastSky(IPlayer player, uint spell4Id)
        {
            List<ISpell> oldSkies = player.GetSpellsByGroupId(SkySpellGroup).ToList();
            if (oldSkies.Count > 0)
            {
                foreach (ISpell oldSky in oldSkies)
                    oldSky.Finish();

                uint guid = player.Guid;
                sceneQueue.Enqueue(SkyRecastDelay, () =>
                {
                    if (mapInstance.GetEntity<IPlayer>(guid) is IPlayer stillHere)
                        CastSkySpell(stillHere, spell4Id);
                });
                return;
            }

            CastSkySpell(player, spell4Id);
        }

        private void CastSkySpell(IPlayer player, uint spell4Id)
        {
            ISpellParameters parameters = spellParametersFactory.Resolve();
            parameters.PrimaryTargetId        = player.Guid;
            parameters.UserInitiatedSpellCast = false;
            player.CastSpell(spell4Id, parameters);
            log.LogDebug($"Hycrest: sky spell {spell4Id} cast on {player.Name}.");
        }

        /// <summary>
        /// Invoked by a mission once its outcome is clear: the story NPCs go ahead to the hideout of its regroup.
        /// </summary>
        public void PrepareHideout(uint missionId)
        {
            if (HycrestMissions.RegroupAfter.TryGetValue(missionId, out uint objectiveId))
                MoveNpcsTo(objectiveId);
        }

        /// <summary>
        /// The story NPCs leave the hideout they are at and appear at the one of <paramref name="regroupObjective"/>.
        /// </summary>
        /// <remarks>
        /// Only hideouts with a phase in <see cref="HideoutPhases"/>; the barn doors stay (phase 0, not NPCs).
        /// </remarks>
        private void MoveNpcsTo(uint regroupObjective)
        {
            if (!HideoutPhases.TryGetValue(regroupObjective, out uint phase) || hideoutPhase == phase)
                return;

            hideoutPhase = phase;
            foreach (uint guid in npcGuids.Values.SelectMany(g => g))
            {
                IWorldEntity npc = mapInstance.GetEntity<IWorldEntity>(guid);
                if (npc is { InWorld: true })
                    npc.RemoveFromMap();
            }
            npcGuids.Clear();

            publicEvent.SetPhase(phase);
            log.LogInformation($"Hycrest: story NPCs moved to the hideout of regroup objective {regroupObjective} (phase {phase}).");
        }

        /// <summary>
        /// Close the doorway of the hideout of <paramref name="regroupObjective"/>: its door appears.
        /// </summary>
        private void CloseBarnDoor(uint regroupObjective)
        {
            if (BarnDoorPhases.TryGetValue(regroupObjective, out uint phase))
            {
                publicEvent.SetPhase(phase);
                doorClosedAt = sceneClock;
            }
        }

        /// <summary>
        /// Open every barn doorway: the doors disappear.
        /// </summary>
        private void OpenBarnDoors()
        {
            foreach (uint guid in barnDoors)
            {
                IWorldEntity door = mapInstance.GetEntity<IWorldEntity>(guid);
                if (door is { InWorld: true })
                    door.RemoveFromMap();
            }
            barnDoors.Clear();
        }

        private void UpdateRegroupParticipants()
        {
            if (regroup?.HasFinished == false)
                regroup.InvokeScriptCollection<IHycrestRegroupScript>(s => s.SetParticipants((uint)mapInstance.PlayerCount));
        }

        /// <summary>
        /// Invoked when the players have regrouped at the hideout.
        /// </summary>
        public void OnRegroupComplete()
        {
            // the barn closes: the finished mission's remaining spawns (its enemies) go now
            finishedMission?.InvokeScriptCollection<IHycrestMissionScript>(s => s.OnHideoutClosed());
            if (finishedMission != null && SkyAtHideout.TryGetValue(finishedMission.Id, out uint sky))
                SetSky(sky);
            finishedMission = null;

            // the hideout's guests leave with the door
            foreach (uint guid in hideoutGuests)
                if (mapInstance.GetEntity<IWorldEntity>(guid) is { InWorld: true } guest)
                    guest.RemoveFromMap();
            hideoutGuests.Clear();

            CloseBarnDoor(regroupObjective);
            sceneQueue.Enqueue(TimeSpan.Zero, NextTier);
        }

        private void NextTier()
        {
            regroup?.Finish(PublicEventTeam.PublicTeam);
            regroup = null;

            int nextTier = tier + 1;
            log.LogInformation($"Hycrest: regrouped, tier {nextTier + 1} next.");

            // the interlude isn't voted: it follows from the tracks of tier 1 and 2
            if (nextTier == HycrestMissions.InterludeTier)
            {
                HycrestTrack interlude = HycrestMissions.GetInterludeTrack(tracks[0], tracks[1]);
                StartMission(HycrestMissions.GetMission(nextTier, interlude));
                return;
            }

            uint voteId = HycrestMissions.GetVote(nextTier, tracks[HycrestMissions.InterludeTier]);
            if (voteId == 0u)
                return;

            if (!VoteScenes.TryGetValue(voteId, out var scene))
            {
                StartVote(voteId);
                return;
            }

            // everyone standing gestures while talking here; Ayita stays seated, without the gesture
            TimeSpan time = QueueLines(TimeSpan.Zero, scene.Lead, gestures: true);
            sceneQueue.Enqueue(time, () => StartVote(voteId));
            QueueLines(time, scene.DuringVote, whileVoting: true, gestures: true);
        }

        // the end of the run (retail video, Teun 1 Oct 2026): everyone is taken to Hycrest church (WorldLocation2 45046), where
        // the Caretaker (the hologram 56685, as in the intro) talks; the green synchronisation wash plays again, the
        // exit portal appears behind him and the run's stats card follows. Main event phases: 30 the Caretaker, 31 the
        // portal. Spots from Jabbithole (Caretaker -2274, -1867; portal -2263, -1862), church floor -868.4 (Surveyor)
        private const uint EndScenePhase  = 30u;
        private const uint EndPortalPhase = 31u;
        private static readonly Vector3 ChurchSpot = new(-2278.5f, -868.0f, -1869f);   // in front of the Caretaker
        private const uint CaretakerCongratulations = 448493u; // "Congratulations, test subjects. ..."
        private const uint CaretakerDoNotAssume     = 448494u; // "Do not assume that because you beat this simulation once, ..."
        private const uint CaretakerGold            = 455474u; // gold medal comment (silver 455475, bronze 455476)
        private const uint SyncSpell = 62968u;                 // Transimulator Synchronization, the green wash
        private static readonly TimeSpan SyncDuration = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan EndSyncDelay = TimeSpan.FromSeconds(1.5);
        private static readonly TimeSpan EndLineGap   = TimeSpan.FromSeconds(9);
        private uint caretakerGuid;

        private void StartEndScene()
        {
            log.LogInformation("Hycrest: the run's end scene in Hycrest church.");
            publicEvent.SetPhase(EndScenePhase);
            foreach (IPlayer player in mapInstance.GetPlayers())
                if (player.CanTeleport())
                    player.TeleportToLocal(ChurchSpot, false);

            // the green synchronisation on the players as soon as they are there (Teun, 2 Oct 2026)
            sceneQueue.Enqueue(EndSyncDelay, () =>
            {
                foreach (IPlayer player in mapInstance.GetPlayers())
                {
                    ISpellParameters parameters = spellParametersFactory.Resolve();
                    parameters.PrimaryTargetId        = player.Guid;
                    parameters.UserInitiatedSpellCast = false;
                    player.CastSpell(SyncSpell, parameters);
                }
                // the green screen (the Caretaker's green sky) with the glow
                SetSky(CaretakerSkyGreen);
            });
            sceneQueue.Enqueue(EndSyncDelay + SyncDuration, () =>
            {
                foreach (IPlayer player in mapInstance.GetPlayers())
                    player.GetSpellBySpellId(SyncSpell)?.Finish();
                // then daylight for the church
                SetSky(MorningSkySpell);
            });

            TimeSpan time = TimeSpan.FromSeconds(4) + SyncDuration;
            foreach (uint line in new[] { CaretakerCongratulations, CaretakerDoNotAssume, CaretakerGold })
            {
                uint textId = line;
                sceneQueue.Enqueue(time, () =>
                {
                    if (mapInstance.GetEntity<IWorldEntity>(caretakerGuid) is { InWorld: true } caretaker)
                        dialogue.Say(caretaker, textId, false);
                });
                time += EndLineGap;
            }

            // the exit portal (phase 31: no spawn until its spot is measured, Teun), then the stats card
            sceneQueue.Enqueue(time, () => publicEvent.SetPhase(EndPortalPhase));
            sceneQueue.Enqueue(time + TimeSpan.FromSeconds(2), CompleteRun);
        }

        private void CompleteRun()
        {
            log.LogInformation($"Hycrest: run complete ({string.Join(", ", tracks)}).");
            publicEvent.Finish(PublicEventTeam.PublicTeam);
        }
    }
}
