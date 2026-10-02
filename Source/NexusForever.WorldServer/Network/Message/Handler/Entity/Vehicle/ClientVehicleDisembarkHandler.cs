using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Spell;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Entity.Vehicle
{
    public class ClientVehicleDisembarkHandler : IMessageHandler<IWorldSession, ClientVehicleDisembark>
    {
        public void HandleMessage(IWorldSession session, ClientVehicleDisembark _)
        {
            // If player is mounted and tries to summon a different mount, the client sends this packet twice.
            // Ignore Disembark request if no vehicle.
            if (session.Player.PlatformGuid == null)
                return;

            bool mounted = false;
            foreach (ISpell spell in session.Player.GetSpellsByEffect(SpellEffectType.SummonMount))
            {
                spell.Finish();
                mounted = true;
            }

            // a vehicle that isn't a mount (e.g. a script's): its exit button takes the player out of it
            if (!mounted
                && session.Player.Map?.GetEntity<IVehicleEntity>(session.Player.PlatformGuid.Value) is IVehicleEntity vehicle
                && vehicle is not IMountEntity
                && vehicle.GetPassenger(session.Player.Guid) != null)
                vehicle.PassengerRemove(session.Player);
        }
    }
}
