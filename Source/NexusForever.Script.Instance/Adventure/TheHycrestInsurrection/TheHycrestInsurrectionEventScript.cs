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
    [ScriptFilterOwnerId(PublicEventId)]
    public class TheHycrestInsurrectionEventScript : IPublicEventScript, IOwnedScript<IPublicEvent>
    {
        public const uint PublicEventId = 419u;

        // TODO: test-only, remove with VoteTestGridTriggerEntityScript once intro event 418 exists.
        // The real vote start is completing 418 (meeting Vesna Taranoft at the Abandoned Barn, WorldLocation2 13091).
        // vote test: entering this trigger in the Abandoned Orchards starts the first mission vote
        // WorldLocation2 45923, 13.5m from the Abandoned Barn, reach it with !teleport location 45923
        public const uint VoteTestTriggerId = 114901u;
        private const float VoteTestTriggerRange = 4f;
        private static readonly Vector3 VoteTestTriggerPosition = new(-2521.66f, -925.82f, -1203.41f);

        private const uint MissionVoteId = 45u;

        // vote 45 options in order: The Farmer's Daughter, The Science of Revenge, Leveling the Field
        private static readonly uint[] MissionVoteEvents = [420u, 421u, 422u];

        private IPublicEvent publicEvent;
        private IMapInstance mapInstance;

        private bool voteInProgress;
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

            var trigger = publicEvent.CreateEntity<IGridTriggerEntity>();
            trigger.Initialise(VoteTestTriggerId, VoteTestTriggerRange);
            trigger.AddToMap(mapInstance, VoteTestTriggerPosition);
        }

        /// <summary>
        /// Invoked when a <see cref="IGridEntity"/> is added to the map the public event is on.
        /// </summary>
        public void OnAddToMap(IGridEntity entity)
        {
            if (entity is not IPlayer player)
                return;

            // late joiners also need to join the current mission, the map script only joins them to the main event
            if (mission != null && !mission.HasFinished)
                JoinEvent(mission, player);
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
                log.LogWarning($"Hycrest vote test: public event {PublicEventId} has finished, unload the map to start a new instance.");
                return;
            }

            voteInProgress = true;
            publicEvent.StartVote(PublicEventTeam.PublicTeam, MissionVoteId, 0u);
            log.LogInformation($"Hycrest vote test: started vote {MissionVoteId} for public event {PublicEventId}.");
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

            log.LogInformation($"Hycrest vote test: vote {voteId} for public event {publicEvent.Id} finished, winner {winner} ({label ?? "unknown"}).");

            if (voteId == MissionVoteId && winner < MissionVoteEvents.Length)
                StartMission(MissionVoteEvents[winner]);
        }

        private void StartMission(uint missionId)
        {
            // sub-events are never removed after they finish, a second CreateEvent for the same id would throw
            if (publicEvent.Map.PublicEventManager.GetEvent(missionId) != null)
            {
                log.LogInformation($"Hycrest vote test: mission {missionId} already exists in this instance, unload the map to try it again.");
                return;
            }

            mission = publicEvent.Map.PublicEventManager.CreateEvent(missionId);
            if (mission == null)
            {
                log.LogError($"Hycrest vote test: failed to create mission {missionId}.");
                return;
            }

            foreach (IPlayer player in mapInstance.GetPlayers())
                JoinEvent(mission, player);

            log.LogInformation($"Hycrest vote test: started mission {missionId} with {mapInstance.PlayerCount} player(s).");
        }

        private static void JoinEvent(IPublicEvent publicEvent, IPlayer player)
        {
            // joining the same character twice throws
            bool isMember = publicEvent.GetTeams()
                .SelectMany(t => t.GetMembers())
                .Any(m => m.CharacterId == player.CharacterId);
            if (isMember)
                return;

            publicEvent.JoinEvent(player, PublicEventTeam.PublicTeam);
        }
    }
}
