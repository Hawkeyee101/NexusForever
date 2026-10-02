using System;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;
using NexusForever.Script.Template;

namespace NexusForever.WorldServer.Network.Message.Handler.Spell
{
    public class ClientCastActionBarSpellHandler : IMessageHandler<IWorldSession, ClientCastActionBarSpell>
    {
        #region Dependency Injection

        private readonly ILogger<ClientCastActionBarSpellHandler> log;

        public ClientCastActionBarSpellHandler(
            ILogger<ClientCastActionBarSpellHandler> log)
        {
            this.log = log;
        }

        #endregion

        /// <summary>
        /// A button of the vehicle bar: passed to the scripts of the vehicle the player controls.
        /// </summary>
        /// <remarks>
        /// The layout of the message is assumed (see <see cref="ClientCastActionBarSpell"/>); its raw bytes are logged until
        /// it is confirmed.
        /// </remarks>
        public void HandleMessage(IWorldSession session, ClientCastActionBarSpell castSpell)
        {
            IPlayer player = session.Player;
            log.LogDebug($"ClientCastActionBarSpell from {player.Name}: {Convert.ToHexString(castSpell.Raw)} (read as unique id {castSpell.ClientUniqueId}, index {castSpell.BagIndex}, caster {castSpell.CasterId}, pressed {castSpell.ButtonPressed}; controlling {player.ControlGuid}).");

            if (VehicleAbility.TryHandle(player, castSpell.CasterId, castSpell.BagIndex, castSpell.ButtonPressed))
                return;

            // the layout may be off: the vehicle the player controls still gets the press
            if (player.ControlGuid is uint controlGuid && controlGuid != player.Guid
                && player.Map?.GetEntity<IVehicleEntity>(controlGuid) is IVehicleEntity vehicle)
                vehicle.InvokeScriptCollection<IWorldEntityScript>(s => s.OnVehicleAbility(player, castSpell.BagIndex, castSpell.ButtonPressed));
        }
    }
}
