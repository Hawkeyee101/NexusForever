using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.Script.Template;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// Base for the mission scripts (public events 420-434): spawns phase 0, finishes the mission and removes its spawns
    /// once it has ended.
    /// </summary>
    /// <remarks>
    /// The main event script (419) creates the mission, joins the players and moves on when it finishes. Finished sub-events
    /// stay in the instance with their spawns, so the mission removes what shouldn't stay (<see cref="KeepAfterMission"/>).
    /// </remarks>
    public abstract class HycrestMissionScript : IPublicEventScript, IOwnedScript<IPublicEvent>, IHycrestMissionScript
    {
        protected IPublicEvent publicEvent;
        protected IMapInstance mapInstance;

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public virtual void OnLoad(IPublicEvent owner)
        {
            publicEvent = owner;
            mapInstance = publicEvent.Map as IMapInstance;

            publicEvent.SetPhase(0u);
        }

        /// <summary>
        /// Finish the mission as a success, the main event script moves on to what comes next.
        /// </summary>
        protected void CompleteMission()
        {
            publicEvent.Finish(PublicEventTeam.PublicTeam);
        }

        /// <summary>
        /// Invoked when the mission has finished: remove what shouldn't stay in the world.
        /// </summary>
        public virtual void OnMissionEnded()
        {
            foreach (IGridEntity entity in publicEvent.GetEntities().ToList())
                if (entity.InWorld && !KeepAfterMission(entity))
                    entity.RemoveFromMap();
        }

        /// <summary>
        /// Return true if <paramref name="entity"/> stays in the world after the mission, e.g. a rescued NPC at their home.
        /// </summary>
        protected virtual bool KeepAfterMission(IGridEntity entity)
        {
            return false;
        }
    }
}
