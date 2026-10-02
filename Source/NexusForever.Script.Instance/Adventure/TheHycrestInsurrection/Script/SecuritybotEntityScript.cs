using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Script
{
    /// <summary>
    /// The dormant Securitybot of Clearance (creature 50772): activating it (its activate spell 49970 "Repairing") hacks it,
    /// the mission script turns the player into the bot.
    /// </summary>
    [ScriptFilterCreatureId(50772u)]
    public class SecuritybotEntityScript : IWorldEntityScript, IOwnedScript<IWorldEntity>
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
            entity.Map?.PublicEventManager?.GetEvent(HycrestPublicEvent.Clearance)?
                .InvokeScriptCollection<IClearanceMissionScript>(s => s.OnSecuritybotHacked(entity, activator));
        }
    }

    /// <summary>
    /// The Securitybot vehicle of Clearance (creature 48438, vehicle 486): its bar's buttons and its end go to the mission.
    /// </summary>
    [ScriptFilterCreatureId(48438u)]
    public class SecuritybotVehicleScript : IWorldEntityScript, IOwnedScript<IWorldEntity>
    {
        private IVehicleEntity vehicle;
        private IBaseMap map;

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public void OnLoad(IWorldEntity owner)
        {
            vehicle = owner as IVehicleEntity;
        }

        /// <summary>
        /// Invoked when <see cref="IGridEntity"/> is added to <see cref="IBaseMap"/>.
        /// </summary>
        public void OnAddToMap(IBaseMap map)
        {
            this.map = map;
        }

        /// <summary>
        /// Invoked when the pilot of this vehicle uses a button of the vehicle's action bar.
        /// </summary>
        public void OnVehicleAbility(IPlayer pilot, ushort index, bool pressed)
        {
            if (vehicle != null)
                map?.PublicEventManager?.GetEvent(HycrestPublicEvent.Clearance)?
                    .InvokeScriptCollection<IClearanceMissionScript>(s => s.OnSecuritybotAbility(vehicle, pilot, index, pressed));
        }

        /// <summary>
        /// Invoked when <see cref="IGridEntity"/> is removed from <see cref="IBaseMap"/>.
        /// </summary>
        public void OnRemoveFromMap(IBaseMap map)
        {
            if (vehicle != null)
                map?.PublicEventManager?.GetEvent(HycrestPublicEvent.Clearance)?
                    .InvokeScriptCollection<IClearanceMissionScript>(s => s.OnSecuritybotGone(vehicle));
        }
    }
}
