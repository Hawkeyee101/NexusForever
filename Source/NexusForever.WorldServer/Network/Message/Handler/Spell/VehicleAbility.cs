using NexusForever.Game.Abstract.Entity;
using NexusForever.Script.Template;

namespace NexusForever.WorldServer.Network.Message.Handler.Spell
{
    /// <summary>
    /// Casts from a vehicle's action bar: the client names the vehicle as the caster, not the player.
    /// </summary>
    public static class VehicleAbility
    {
        /// <summary>
        /// If <paramref name="casterGuid"/> is the vehicle <paramref name="player"/> controls, pass the button press to the
        /// vehicle's scripts (<see cref="IWorldEntityScript.OnVehicleAbility"/>) and return true.
        /// </summary>
        public static bool TryHandle(IPlayer player, uint casterGuid, ushort index, bool pressed)
        {
            if (casterGuid == 0u || casterGuid == player.Guid || player.ControlGuid != casterGuid)
                return false;

            IVehicleEntity vehicle = player.Map?.GetEntity<IVehicleEntity>(casterGuid);
            if (vehicle == null)
                return false;

            vehicle.InvokeScriptCollection<IWorldEntityScript>(s => s.OnVehicleAbility(player, index, pressed));
            return true;
        }
    }
}
