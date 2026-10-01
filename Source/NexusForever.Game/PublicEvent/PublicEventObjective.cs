using System.Numerics;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.Game.Static.Reputation;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.PublicEvent;
using NexusForever.Script.Template;
using NexusForever.Shared.Game;

namespace NexusForever.Game.PublicEvent
{
    public class PublicEventObjective : IPublicEventObjective
    {
        public IPublicEventTeam Team { get; private set; }
        public PublicEventObjectiveEntry Entry { get; private set; }
        public PublicEventStatus Status { get; private set; }
        public uint Count { get; private set; }
        public uint DynamicMax { get; private set; }
        public uint Checklist { get; private set; }

        public bool IsBusy { get; private set; }

        private double elapsedTimer;
        private UpdateTimer failureTimer;

        // Exterminate: the units that have to be killed; the count is the number killed, the dynamic max the number of
        // targets so far (units the event spawns while the objective is active are added, e.g. waves)
        private readonly HashSet<uint> targets = [];
        // a script set the Exterminate total (e.g. every wave known up front): spawning targets no longer raises it
        private bool dynamicMaxFixed;

        // WorldLocation2 points shown for the objective on the map (the area and the minimap marker), like the ones the
        // client takes from the objective's own WorldLocation2 in the tables; most objectives have none there, scripts
        // supply them. Sent with ServerPublicEventLocationUpdate while the objective is active (putting them in the
        // objective message's Locations/MapRegions left the client without the event in its tracker)
        private readonly List<uint> locations = [];


        // most Exterminate locations have a radius of a few metres, the fight around them is larger
        private const float ExterminateMinRadius = 40f;

        private bool IsExterminate => Entry.PublicEventObjectiveTypeEnum == PublicEventObjectiveType.Exterminate;

        private bool IsChecklist => Entry.PublicEventObjectiveTypeEnum
            is PublicEventObjectiveType.ActivateTargetGroupChecklist
            or PublicEventObjectiveType.TalkToChecklist;

        /// <summary>
        /// Initialise <see cref="PublicEventObjective"/> with suppled <see cref="IPublicEventTeam"/> and <see cref="PublicEventObjectiveEntry"/>.
        /// </summary>
        public void Initialise(IPublicEventTeam team, PublicEventObjectiveEntry entry)
        {
            Team   = team;
            Entry  = entry;
            Status = entry.PublicEventObjectiveFlags.HasFlag(PublicEventObjectiveFlag.InitialObjective)
                ? PublicEventStatus.Active : PublicEventStatus.Inactive;

            // created once and frozen, it only runs while the objective is active
            if (entry.FailureTimeMs > 0)
                failureTimer = new UpdateTimer(TimeSpan.FromMilliseconds(entry.FailureTimeMs), false);

            if (Status == PublicEventStatus.Active)
                StartTimers();
        }

        /// <summary>
        /// Start the elapsed and failure timers, invoked when the objective becomes active.
        /// </summary>
        /// <remarks>
        /// Timers must not run while the objective is inactive, otherwise objectives activated later in the event fail early.
        /// </remarks>
        private void StartTimers()
        {
            elapsedTimer = 0d;
            failureTimer?.Reset();
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public void Update(double lastTick)
        {
            if (IsBusy)
                return;

            if (Status != PublicEventStatus.Active)
                return;

            elapsedTimer += lastTick;

            if (failureTimer == null || !failureTimer.IsTicking)
                return;

            failureTimer.Update(lastTick);
            if (failureTimer.HasElapsed)
            {
                // timed wait objectives (waiting for an NPC to finish speaking, an animation or an event) succeed when
                // the timer ends, any other timed objective fails
                SetStatus(Entry.PublicEventObjectiveTypeEnum == PublicEventObjectiveType.TimedWin
                    ? PublicEventStatus.Succeeded : PublicEventStatus.Failed);
            }
        }

        private void SetStatus(PublicEventStatus status)
        {
            Status = status;

            if (status == PublicEventStatus.Active)
            {
                StartTimers();
                if (IsExterminate)
                    SelectTargets();
            }
            else
                failureTimer?.Pause();

            // the client runs an objective's timer from the ElapsedTimeMs it last received (at event start for objectives
            // activated later), so activation sends the full objective (it includes the status) to restart the client's
            // timer; a status-only update first made the timer flicker
            if (status == PublicEventStatus.Active)
                BroadcastObjectiveUpdate();
            else
                BroadcastObjectiveStatusUpdate();

            // the map locations show while the objective is active (sent with the full objective above on activation)
            if (status != PublicEventStatus.Active)
                foreach (uint id in locations)
                    BroadcastLocation(PublicEventOperationType.RemoveFromObjective, id);

            Team.PublicEvent.InvokeScriptCollection<IPublicEventScript>(s => s.OnPublicEventObjectiveStatus(this));
        }

        private void BroadcastObjectiveUpdate()
        {
            BroadcastObjectiveUpdate(new ServerPublicEventObjectiveUpdate
            {
                Objective = Build()
            });

            // the full objective resets the client's locations for it (its location list is empty), send them again: a
            // location set just before a count update (a script reacting to the interaction that counts) was lost
            if (Status == PublicEventStatus.Active)
                foreach (uint id in locations)
                    BroadcastLocation(PublicEventOperationType.AddToObjective, id);
        }

        private void BroadcastObjectiveStatusUpdate()
        {
            BroadcastObjectiveUpdate(new ServerPublicEventObjectiveStatusUpdate
            {
                ObjectiveId     = Entry.Id,
                ObjectiveStatus = BuildObjectiveStatus()
            });
        }

        private void BroadcastObjectiveUpdate(IWritable message)
        {
            if (Entry.LocalizedTextIdOtherTeam == 0)
                Team.Broadcast(message);
            else
            {
                foreach (IPublicEventTeam team in Team.PublicEvent.GetTeams())
                    team.Broadcast(message);
            }
        }

        /// <summary>
        /// Set busy state for the objective.
        /// </summary>
        /// <remarks>
        /// This will pause the objective preventing updates.
        /// </remarks>
        public void SetBusy(bool busy)
        {
            IsBusy = busy;
            BroadcastObjectiveUpdate();
        }

        /// <summary>
        /// Update objective with the supplied count.
        /// </summary>
        public void UpdateObjective(int count)
        {
            if (IsBusy)
                return;

            if (Status != PublicEventStatus.Active)
                return;

            uint oldCount = Count;

            if (IsChecklist)
            {
                uint flag = (uint)(1 << count);
                if ((Checklist & flag) == 0)
                {
                    Checklist |= flag;
                    Count++;
                }
            }
            else
                Count = (uint)Math.Max(0, (int)Count + count);

            if (oldCount != Count)
                BroadcastObjectiveUpdate();

            if (IsComplete())
                SetStatus(PublicEventStatus.Succeeded);
        }

        private bool IsComplete()
        {
            if (IsExterminate)
                return DynamicMax > 0 && Count >= DynamicMax;

            if (Entry.PublicEventObjectiveFlags.HasFlag(PublicEventObjectiveFlag.DynamicObjective))
                return Count >= DynamicMax;

            // participant objectives without a fixed count (e.g. "Meet with Vesna Taranoft") wait for every participant
            // the client shows the remaining participants as "Waiting for N more" based on the dynamic max
            if (Entry.PublicEventObjectiveTypeEnum == PublicEventObjectiveType.ParticipantsInTriggerVolume
                && Entry.Count == 0
                && DynamicMax > 0)
                return Count >= DynamicMax;

            return Count >= Entry.Count;
        }

        /// <summary>
        /// Activate the objective.
        /// </summary>
        /// <remarks>
        /// This shows the objective to members and allows it to be updated.
        /// </remarks>
        public void ActivateObjective(uint max)
        {
            if (Status != PublicEventStatus.Inactive)
                return;

            DynamicMax = max;
            SetStatus(PublicEventStatus.Active);
        }

        /// <summary>
        /// Set the WorldLocation2 points shown on the map for the objective.
        /// </summary>
        public void SetLocations(IEnumerable<uint> worldLocation2Ids)
        {
            List<uint> ids = worldLocation2Ids.Distinct().ToList();
            if (Status == PublicEventStatus.Active)
            {
                foreach (uint id in locations.Except(ids))
                    BroadcastLocation(PublicEventOperationType.RemoveFromObjective, id);
                foreach (uint id in ids.Except(locations))
                    BroadcastLocation(PublicEventOperationType.AddToObjective, id);
            }

            locations.Clear();
            locations.AddRange(ids);
        }

        /// <summary>
        /// Send the objective's map locations to <see cref="IPlayer"/>, e.g. after joining the event.
        /// </summary>
        public void SendMarkers(IPlayer player)
        {
            if (Status != PublicEventStatus.Active)
                return;

            foreach (uint id in locations)
                player.Session.EnqueueMessageEncrypted(BuildLocationUpdate(PublicEventOperationType.AddToObjective, id));
        }

        private void BroadcastLocation(PublicEventOperationType operation, uint worldLocation2Id)
        {
            BroadcastObjectiveUpdate(BuildLocationUpdate(operation, worldLocation2Id));
        }

        private ServerPublicEventLocationUpdate BuildLocationUpdate(PublicEventOperationType operation, uint worldLocation2Id)
        {
            return new ServerPublicEventLocationUpdate
            {
                ObjectId         = Entry.Id,
                Operation        = operation,
                WorldLocation2Id = worldLocation2Id
            };
        }

        /// <summary>
        /// Set the dynamic max of an active objective, for example when participants join or leave.
        /// </summary>
        /// <remarks>
        /// The objective is completed immediately if the current count already meets the new max.
        /// </remarks>
        public void SetDynamicMax(uint max)
        {
            if (Status != PublicEventStatus.Active)
                return;

            if (IsExterminate)
                dynamicMaxFixed = true;

            if (DynamicMax == max)
                return;

            DynamicMax = max;
            BroadcastObjectiveStatusUpdate();

            if (IsComplete())
                SetStatus(PublicEventStatus.Succeeded);
        }

        /// <summary>
        /// Select the units that have to be killed for an Exterminate objective from the entities of its public event.
        /// </summary>
        private void SelectTargets()
        {
            targets.Clear();
            Count = 0;
            dynamicMaxFixed = false;

            foreach (IGridEntity entity in Team.PublicEvent.GetEntities())
                if (entity is IUnitEntity unit && IsTargetCandidate(unit))
                    targets.Add(unit.Guid);

            DynamicMax = (uint)targets.Count;
        }

        /// <summary>
        /// Return true if <paramref name="unit"/> counts for an Exterminate objective: alive, hostile to the players and in
        /// the objective's location and target group when the objective has them.
        /// </summary>
        private bool IsTargetCandidate(IUnitEntity unit)
        {
            if (!unit.IsAlive || unit is IPlayer)
                return false;

            if (Entry.WorldLocation2Id != 0)
            {
                WorldLocation2Entry location = GameTableManager.Instance.WorldLocation2.GetEntry(Entry.WorldLocation2Id);
                if (location != null)
                {
                    float radius = Math.Max(location.Radius, ExterminateMinRadius);
                    var centre = new Vector2(location.Position0, location.Position2);
                    if (Vector2.Distance(centre, new Vector2(unit.Position.X, unit.Position.Z)) > radius)
                        return false;
                }
            }

            // a creature target group names the units, otherwise every unit hostile to the players counts
            TargetGroupEntry targetGroup = Entry.ObjectId != 0 ? GameTableManager.Instance.TargetGroup.GetEntry(Entry.ObjectId) : null;
            if (targetGroup?.Type == 1u)
                return AssetManager.Instance.GetTargetGroupsForCreatureId(unit.CreatureId).Contains(Entry.ObjectId);

            return IsHostileToPlayers(unit);
        }

        private bool IsHostileToPlayers(IUnitEntity unit)
        {
            if (Team.PublicEvent.Map is not IMapInstance instance)
                return false;

            return instance.GetPlayers().Any(p => unit.GetDispositionTo(p.Faction1) < Disposition.Friendly);
        }

        /// <summary>
        /// Add a unit that has to be killed for an active Exterminate objective, <paramref name="force"/> skips the checks
        /// that decide which units count on their own (hostile, in the objective's location or target group).
        /// </summary>
        public void AddTarget(IUnitEntity unit, bool force = false)
        {
            if (!IsExterminate || Status != PublicEventStatus.Active || targets.Contains(unit.Guid))
                return;

            if (!force && !IsTargetCandidate(unit))
                return;

            targets.Add(unit.Guid);
            if (!dynamicMaxFixed)
                DynamicMax++;
            BroadcastObjectiveUpdate();
        }

        /// <summary>
        /// Invoked when a unit left the map or was killed, <paramref name="killed"/> counts it towards an Exterminate objective.
        /// </summary>
        public void OnTargetRemoved(uint guid, bool killed)
        {
            if (!IsExterminate || Status != PublicEventStatus.Active || !targets.Remove(guid))
                return;

            if (killed)
                Count++;
            else if (!dynamicMaxFixed)
                DynamicMax--;

            BroadcastObjectiveUpdate();

            if (IsComplete())
                SetStatus(PublicEventStatus.Succeeded);
        }

        /// <summary>
        /// Reset the objective.
        /// </summary>
        /// <remarks>
        /// This will reset the objective to its initial state allowing it to be activated again.
        /// </remarks>
        public void ResetObjective()
        {
            if (Status != PublicEventStatus.Succeeded)
                return;

            Count      = 0;
            DynamicMax = 0;
            Checklist  = 0;
            targets.Clear();

            SetStatus(PublicEventStatus.Inactive);
        }

        public Network.World.Message.Model.Shared.PublicEventObjective Build()
        {
            return new Network.World.Message.Model.Shared.PublicEventObjective
            {
                ObjectiveId      = Entry.Id,
                ObjectiveStatus  = BuildObjectiveStatus(),
                Busy             = IsBusy,
                ElapsedTimeMs    = (uint)(elapsedTimer * 1000d)
            };
        }

        private Network.World.Message.Model.Shared.PublicEventObjectiveStatus BuildObjectiveStatus()
        {
            return new Network.World.Message.Model.Shared.PublicEventObjectiveStatus
            {
                Status        = Status,
                ObjectiveData = IsChecklist ? Checklist : Count,
                DynamicMax    = DynamicMax
            };
        }
    }
}
