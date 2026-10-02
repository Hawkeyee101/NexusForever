using NexusForever.Game.Abstract.Entity;
using NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Script
{
    /// <summary>
    /// The Force Field Lever on the locomotive of All Aboard (creature 50775, activate spell 58497 "Pulling Lever"): the
    /// mission script drops the force fields around the drive system.
    /// </summary>
    [ScriptFilterCreatureId(50775u)]
    public class AllAboardLeverScript : IWorldEntityScript, IOwnedScript<IWorldEntity>
    {
        private IWorldEntity entity;

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public void OnLoad(IWorldEntity owner)
        {
            entity = owner;
        }

        /// <summary>
        /// Invoked when <see cref="IWorldEntity"/> is successfully activated by <see cref="IPlayer"/>.
        /// </summary>
        public void OnActivateSuccess(IPlayer activator)
        {
            entity.Map?.PublicEventManager?.GetEvent(HycrestPublicEvent.AllAboard)?
                .InvokeScriptCollection<IAllAboardMissionScript>(s => s.OnLeverPulled(entity, activator));
        }
    }

    /// <summary>
    /// The PEC Drive System inside the locomotive of All Aboard (creature 17939, activate spell 50068, "Planting Bomb", 2 s):
    /// the bomb is planted.
    /// </summary>
    [ScriptFilterCreatureId(17939u)]
    public class AllAboardDriveSystemScript : IWorldEntityScript, IOwnedScript<IWorldEntity>
    {
        private IWorldEntity entity;

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public void OnLoad(IWorldEntity owner)
        {
            entity = owner;
        }

        /// <summary>
        /// Invoked when <see cref="IWorldEntity"/> is successfully activated by <see cref="IPlayer"/>.
        /// </summary>
        public void OnActivateSuccess(IPlayer activator)
        {
            entity.Map?.PublicEventManager?.GetEvent(HycrestPublicEvent.AllAboard)?
                .InvokeScriptCollection<IAllAboardMissionScript>(s => s.OnBombPlanted(entity, activator));
        }
    }
}
