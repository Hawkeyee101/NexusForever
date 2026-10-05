using NexusForever.Game.Abstract.Entity;
using NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Script
{
    /// <summary>
    /// The Jetpacks in the Detention Center yard of Jailbreak (creature 48254, activate spell 47331, 2 s): the mission
    /// script launches the player up to the Chua Warblimp.
    /// </summary>
    [ScriptFilterCreatureId(48254u)]
    public class JailbreakJetpackScript : IWorldEntityScript, IOwnedScript<IWorldEntity>
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
            entity.Map?.PublicEventManager?.GetEvent(HycrestPublicEvent.Jailbreak)?
                .InvokeScriptCollection<IJailbreakMissionScript>(s => s.OnJetpackUsed(entity, activator));
        }
    }

    /// <summary>
    /// The Navigation Array on the Chua Warblimp's deck (creature 18404, activate spell 1817, 2 s): starts the 11 s overload
    /// of the Balance Stabilizers.
    /// </summary>
    [ScriptFilterCreatureId(18404u)]
    public class JailbreakNavigationArrayScript : IWorldEntityScript, IOwnedScript<IWorldEntity>
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
            entity.Map?.PublicEventManager?.GetEvent(HycrestPublicEvent.Jailbreak)?
                .InvokeScriptCollection<IJailbreakMissionScript>(s => s.OnNavigationArrayActivated(entity, activator));
        }
    }

    /// <summary>
    /// The Balance Stabilizers on the Chua Warblimp's deck (creature 18235, activate spell 49352 "Overloading"): the
    /// objective (1758, target group 6752) counts them on its own, the mission script takes the overloaded one away.
    /// </summary>
    [ScriptFilterCreatureId(18235u)]
    public class JailbreakStabilizerScript : IWorldEntityScript, IOwnedScript<IWorldEntity>
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
            entity.Map?.PublicEventManager?.GetEvent(HycrestPublicEvent.Jailbreak)?
                .InvokeScriptCollection<IJailbreakMissionScript>(s => s.OnStabilizerOverloaded(entity, activator));
        }
    }
}
