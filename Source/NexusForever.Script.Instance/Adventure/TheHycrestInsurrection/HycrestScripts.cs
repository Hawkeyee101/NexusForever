using NexusForever.Game.Abstract.PublicEvent;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    /// <summary>
    /// Implemented by the main event script (419): runs the adventure from mission to mission.
    /// </summary>
    public interface IHycrestMainEventScript
    {
        /// <summary>
        /// Invoked when a mission has finished.
        /// </summary>
        void OnMissionFinished(IPublicEvent mission);

        /// <summary>
        /// Invoked when the players have regrouped at the hideout.
        /// </summary>
        void OnRegroupComplete();

        /// <summary>
        /// Move the story NPCs to the hideout the players regroup at after <paramref name="missionId"/>, invoked by the
        /// mission once its outcome is clear (e.g. The Farmer's Daughter when Millithea is freed).
        /// </summary>
        void PrepareHideout(uint missionId);
    }

    /// <summary>
    /// Implemented by the regroup event script (445).
    /// </summary>
    public interface IHycrestRegroupScript
    {
        /// <summary>
        /// Send the players to the hideout of the supplied regroup objective.
        /// </summary>
        void StartRegroup(uint objectiveId, uint participants);

        /// <summary>
        /// Update the number of players that have to reach the hideout, when players join or leave.
        /// </summary>
        void SetParticipants(uint participants);
    }

    /// <summary>
    /// Implemented by the mission scripts (420-434).
    /// </summary>
    public interface IHycrestMissionScript
    {
        /// <summary>
        /// Invoked when the mission has finished: remove what shouldn't stay in the world.
        /// </summary>
        void OnMissionEnded();

        /// <summary>
        /// Invoked when the hideout's barn closes after the mission (the regroup is complete): remove what stayed.
        /// </summary>
        void OnHideoutClosed();
    }
}
