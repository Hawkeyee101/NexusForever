using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity.Trigger;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    /// <summary>
    /// Regroup (public event 445): between missions the players gather at a hideout (objectives 1772-1776).
    /// </summary>
    /// <remarks>
    /// Created by the main event script for each regroup and finished by it once everyone has arrived. The objectives are
    /// participants in a trigger volume (the table's object id) at the objective's WorldLocation2 point, so they wait for
    /// the whole party.
    /// </remarks>
    [ScriptFilterOwnerId(HycrestPublicEvent.Regroup)]
    public class HycrestRegroupEventScript : IPublicEventScript, IOwnedScript<IPublicEvent>, IHycrestRegroupScript
    {
        // the hideout points have a radius of 1; the trigger covers the building
        private const float TriggerRange = 10f;
        private const uint TriggerId = 114910u;

        // measured hideouts: the trigger is a sphere, so it is kept small and centred on the NPCs inside the building
        // (the table point with 10 m also caught players outside the walls)
        private const float InsideTriggerRange = 4f;
        private static readonly Dictionary<uint, Vector3> InsideTriggerPositions = new()
        {
            [HycrestMissions.RegroupAbandonedBarn] = new(-2525.34f, -925.82f, -1190.13f),
            [HycrestMissions.RegroupSinnatusBarn]  = new(-2391.63f, -926.28f, -1527.55f)
        };

        private IPublicEvent publicEvent;
        private IMapInstance mapInstance;

        private uint activeObjective;
        private IVolumeGridTriggerEntity trigger;

        #region Dependency Injection

        private readonly ILogger<HycrestRegroupEventScript> log;
        private readonly IGameTableManager gameTableManager;

        public HycrestRegroupEventScript(
            ILogger<HycrestRegroupEventScript> log,
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
        }

        /// <summary>
        /// Send the players to the hideout of the supplied regroup objective.
        /// </summary>
        public void StartRegroup(uint objectiveId, uint participants)
        {
            PublicEventObjectiveEntry entry = gameTableManager.PublicEventObjective.GetEntry(objectiveId);
            WorldLocation2Entry location = entry != null ? gameTableManager.WorldLocation2.GetEntry(entry.WorldLocation2Id) : null;
            if (location == null)
            {
                log.LogError($"Hycrest: regroup objective {objectiveId} has no location.");
                return;
            }

            publicEvent.ActivateObjective(objectiveId, participants);
            activeObjective = objectiveId;

            // a new trigger each time: players already standing at the hideout are counted when it is added
            if (trigger?.InWorld == true)
                trigger.RemoveFromMap();

            bool inside = InsideTriggerPositions.TryGetValue(objectiveId, out Vector3 position);
            if (!inside)
                position = new Vector3(location.Position0, location.Position1, location.Position2);

            trigger = publicEvent.CreateEntity<IVolumeGridTriggerEntity>();
            trigger.Initialise(TriggerId, inside ? InsideTriggerRange : TriggerRange, entry.ObjectId);
            trigger.AddToMap(mapInstance, position);

            log.LogInformation($"Hycrest: regroup at objective {objectiveId} (location {entry.WorldLocation2Id}) for {participants} player(s).");
        }

        /// <summary>
        /// Update the number of players that have to reach the hideout, when players join or leave.
        /// </summary>
        public void SetParticipants(uint participants)
        {
            if (activeObjective != 0u)
                publicEvent.SetObjectiveDynamicMax(activeObjective, participants);
        }

        /// <summary>
        /// Invoked when the status of an objective changes.
        /// </summary>
        public void OnPublicEventObjectiveStatus(IPublicEventObjective objective)
        {
            if (objective.Entry.Id != activeObjective || objective.Status != PublicEventStatus.Succeeded)
                return;

            activeObjective = 0u;
            if (trigger?.InWorld == true)
                trigger.RemoveFromMap();

            publicEvent.Map.PublicEventManager.GetEvent(HycrestPublicEvent.Main)?
                .InvokeScriptCollection<IHycrestMainEventScript>(s => s.OnRegroupComplete());
        }
    }
}
