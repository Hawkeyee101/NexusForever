using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Trigger;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Static;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;

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
            [425u] = (PublicEventCreature.LysionSinnatus, 465558u, 0u)       // "Deliver my army to freedom..."
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
        private IPublicEvent regroup;

        // the run so far: the tier of the current mission and the track chosen on each tier
        private int tier = -1;
        private readonly HycrestTrack[] tracks = new HycrestTrack[HycrestMissions.TierCount];

        private readonly TimedActionQueue sceneQueue = new();
        // the story NPCs can stand in more than one hideout (Abandoned Barn, and the one the players regroup at)
        private readonly Dictionary<PublicEventCreature, List<uint>> npcGuids = [];

        // phase of the main event with the NPCs of a hideout, set when the players regroup there
        private static readonly Dictionary<uint, uint> HideoutPhases = new()
        {
            [HycrestMissions.RegroupSinnatusBarn] = 11u
        };
        private readonly List<uint> barnDoors = [];
        private uint regroupObjective;
        private uint hideoutPhase;

        // a barn door belongs to the hideout whose regroup point is this close
        private const float BarnDoorRange = 40f;
        private bool barnArrivalPlayed;
        private double sceneClock;
        private double barnArrivalLineEnd;

        #region Dependency Injection

        private readonly ILogger<TheHycrestInsurrectionEventScript> log;
        private readonly IGameTableManager gameTableManager;
        private readonly IStoryBuilder storyBuilder;
        private readonly HycrestDialogue dialogue;

        public TheHycrestInsurrectionEventScript(
            ILogger<TheHycrestInsurrectionEventScript> log,
            IGameTableManager gameTableManager,
            IStoryBuilder storyBuilder)
        {
            this.log              = log;
            this.gameTableManager = gameTableManager;
            this.storyBuilder     = storyBuilder;
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

            // the intro is a separate root event, players are joined to it by the map script
            publicEvent.Map.PublicEventManager.CreateEvent(HycrestPublicEvent.Intro);
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
            var creature = (PublicEventCreature)worldEntity.CreatureId;
            switch (creature)
            {
                case PublicEventCreature.VesnaTaranoft:
                case PublicEventCreature.LysionSinnatus:
                    AddNpc(creature, worldEntity.Guid);
                    break;
                case PublicEventCreature.BarnDoor:
                    // one per hideout barn (Abandoned Barn, Sinnatus's Barn): open while players arrive, closed for the
                    // briefing or regroup and the vote (retail video), open again when the mission starts
                    barnDoors.Add(worldEntity.Guid);
                    worldEntity.StandState = StandState.State1;
                    break;
                case PublicEventCreature.AyitaSinnatus:
                {
                    AddNpc(creature, worldEntity.Guid);
                    // retail: she sits on top of the hay bales; stand state is a stat, so players arriving later see it too
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
            foreach ((PublicEventCreature speaker, uint textId) in lines)
            {
                sceneQueue.Enqueue(time, () =>
                {
                    // the argument during a vote stops once the vote is over (solo, it ends as soon as you vote)
                    if (!whileVoting || voteInProgress)
                        dialogue.Say(GetNpc(speaker), textId, gestures && speaker != PublicEventCreature.AyitaSinnatus || UsesGesture(speaker));
                });
                TimeSpan duration = GetLineDuration(textId) - shortening;
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
            SetBarnDoors(StandState.State0, near: GetObjectiveLocation(HycrestMissions.RegroupAbandonedBarn));
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

            SetBarnDoors(StandState.State1);

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

            if (!HycrestMissions.TryGetTierAndTrack(missionId, out int missionTier, out HycrestTrack track))
                return;

            log.LogInformation($"Hycrest: mission {missionId} (tier {missionTier + 1}, {track}) finished.");

            if (missionTier == HycrestMissions.FinaleTier)
            {
                CompleteRun();
                return;
            }

            // the finale follows the tier 4 track right away, it starts where the tier 4 mission ended
            if (missionTier == HycrestMissions.FinaleTier - 1)
            {
                sceneQueue.Enqueue(NextMissionDelay, () => StartMission(HycrestMissions.GetMission(HycrestMissions.FinaleTier, track)));
                return;
            }

            StartRegroup(HycrestMissions.RegroupAfter[missionId]);
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
        /// Open or close the barn doors, only those of the hideout at <paramref name="near"/> if given.
        /// </summary>
        private void SetBarnDoors(StandState state, Vector3? near = null)
        {
            foreach (uint guid in barnDoors)
            {
                IWorldEntity door = mapInstance.GetEntity<IWorldEntity>(guid);
                if (door == null || near.HasValue && Vector3.Distance(door.Position, near.Value) > BarnDoorRange)
                    continue;

                HycrestDropShip.SetState(door, state);
            }
        }

        private Vector3? GetObjectiveLocation(uint objectiveId)
        {
            PublicEventObjectiveEntry entry = gameTableManager.PublicEventObjective.GetEntry(objectiveId);
            WorldLocation2Entry location = entry != null ? gameTableManager.WorldLocation2.GetEntry(entry.WorldLocation2Id) : null;
            return location != null ? new Vector3(location.Position0, location.Position1, location.Position2) : null;
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
            SetBarnDoors(StandState.State0, near: GetObjectiveLocation(regroupObjective));
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

        private void CompleteRun()
        {
            log.LogInformation($"Hycrest: run complete ({string.Join(", ", tracks)}).");
            publicEvent.Finish(PublicEventTeam.PublicTeam);
        }
    }
}
