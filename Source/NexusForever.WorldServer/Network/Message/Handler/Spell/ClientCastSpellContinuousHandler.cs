using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Spell
{
    public class ClientCastSpellContinuousHandler : IMessageHandler<IWorldSession, ClientCastSpellContinuous>
    {
        /// <summary>
        /// This handler is used when the player has enabled continous casting in Settings > Controls
        /// </summary>
        public void HandleMessage(IWorldSession session, ClientCastSpellContinuous castSpell)
        {
            // a button of the vehicle bar: the caster is the vehicle the player controls, the index is the button's
            if (VehicleAbility.TryHandle(session.Player, castSpell.Guid, castSpell.BagIndex, castSpell.ButtonPressed))
                return;

            IItem item = session.Player.Inventory.GetItem(InventoryLocation.Ability, castSpell.BagIndex);
            if (item == null)
                throw new InvalidPacketValueException();

            ICharacterSpell characterSpell = session.Player.SpellManager.GetSpell(item.Id);
            if (characterSpell == null)
                throw new InvalidPacketValueException();

            characterSpell.Cast(castSpell.ButtonPressed);
        }
    }
}
