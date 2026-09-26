using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Trigger;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Static;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    [ScriptFilterOwnerId(HycrestPublicEvent.Main)]
    public class TheHycrestInsurrectionEventScript : IPublicEventScript, IOwnedScript<IPublicEvent>
    {
        // development aid: after the intro, walking into the Abandoned Barn again restarts the mission vote
        // set to false once missions follow each other properly
        public const bool AllowVoteRetest = true;
        public const uint VoteRetestTriggerId = 114901u;
        private const float VoteRetestTriggerRange = 8f;
        private static readonly Vector3 VoteRetestTriggerPosition = new(-2526.80f, -925.82f, -1190.93f);

        private const uint MissionVoteId = 45u;

        // vote 45 options in order: The Farmer's Daughter, The Science of Revenge, Leveling the Field
        private static readonly uint[] MissionVoteEvents = [420u, 421u, 422u];

        private IPublicEvent publicEvent;
        private IMapInstance mapInstance;

        private bool voteInProgress;
        private uint? pendingMissionId;
        private IPublicEvent mission;

        #region Dependency Injection

        private readonly ILogger<TheHycrestInsurrectionEventScript> log;
        private readonly IGameTableManager gameTableManager;

        public TheHycrestInsurrectionEventScript(
            ILogger<TheHycrestInsurrectionEventScript> log,
            IGameTableManager gameTableManager)
        {
            this.log              = log;
            this.gameTableManager = gameTableManager;
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
            if (pendingMissionId == null)
                return;

            uint missionId = pendingMissionId.Value;
            pendingMissionId = null;
            StartMission(missionId);
        }

        /// <summary>
        /// Invoked when a <see cref="IGridEntity"/> is added to the map the public event is on.
        /// </summary>
        public void OnAddToMap(IGridEntity entity)
        {
            if (entity is not IPlayer player)
                return;

            // late joiners also need to join the current mission, the map script only joins them to the main and intro events
            if (mission != null)
                HycrestPublicEvent.JoinPublicTeam(mission, player);
        }

        /// <summary>
        /// Invoked by <see cref="TheHycrestInsurrectionIntroEventScript"/> when the intro has been completed.
        /// </summary>
        public void OnIntroComplete()
        {
            StartMissionVote();

            if (!AllowVoteRetest)
                return;

            var trigger = publicEvent.CreateEntity<IGridTriggerEntity>();
            trigger.Initialise(VoteRetestTriggerId, VoteRetestTriggerRange);
            trigger.AddToMap(mapInstance, VoteRetestTriggerPosition);
        }

        /// <summary>
        /// Start the mission vote, unless one is already in progress.
        /// </summary>
        public void StartMissionVote()
        {
            if (voteInProgress)
                return;

            if (publicEvent.HasFinished)
            {
                // a finished event no longer ticks, so the vote would never time out
                log.LogWarning($"Hycrest: public event {HycrestPublicEvent.Main} has finished, restart the world server for a new instance.");
                return;
            }

            voteInProgress = true;
            publicEvent.StartVote(PublicEventTeam.PublicTeam, MissionVoteId, 0u);
            log.LogInformation($"Hycrest: started vote {MissionVoteId} for public event {HycrestPublicEvent.Main}.");
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

            // a vote that times out finishes during the public event manager update, creating an event there would modify
            // the collection being enumerated, so the mission is created on the next tick instead
            if (voteId == MissionVoteId && winner < MissionVoteEvents.Length)
                pendingMissionId = MissionVoteEvents[winner];
        }

        private void StartMission(uint missionId)
        {
            // sub-events are never removed after they finish, a second CreateEvent for the same id would throw
            if (publicEvent.Map.PublicEventManager.GetEvent(missionId) != null)
            {
                log.LogInformation($"Hycrest: mission {missionId} already exists in this instance, restart the world server to try it again.");
                return;
            }

            mission = publicEvent.Map.PublicEventManager.CreateEvent(missionId);
            if (mission == null)
            {
                log.LogError($"Hycrest: failed to create mission {missionId}.");
                return;
            }

            foreach (IPlayer player in mapInstance.GetPlayers())
                HycrestPublicEvent.JoinPublicTeam(mission, player);

            log.LogInformation($"Hycrest: started mission {missionId} with {mapInstance.PlayerCount} player(s).");
        }
    }
}
