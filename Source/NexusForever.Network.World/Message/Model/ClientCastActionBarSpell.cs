using NexusForever.Network.Message;

namespace NexusForever.Network.World.Message.Model
{
    /// <summary>
    /// A button of a vehicle's action bar (the bar sent with <see cref="Abilities.ServerShowActionBar"/>, VehicleBar) was
    /// pressed. Not documented anywhere; the layout is assumed to be the one of <see cref="ClientCastSpell"/> and the raw
    /// bytes are kept so the handler can log them.
    /// </summary>
    [Message(GameMessageOpcode.ClientCastActionBarSpell)]
    public class ClientCastActionBarSpell : IReadable
    {
        public byte[] Raw { get; private set; }

        public uint ClientUniqueId { get; private set; }
        public ushort BagIndex { get; private set; }
        public uint CasterId { get; private set; }
        public bool ButtonPressed { get; private set; }

        public void Read(GamePacketReader reader)
        {
            uint start = reader.BytePosition;
            uint length = reader.BytesRemaining;

            ClientUniqueId = reader.ReadUInt();
            BagIndex       = reader.ReadUShort();
            CasterId       = reader.ReadUInt();
            ButtonPressed  = reader.ReadBit();

            reader.ResetBits();
            reader.BytePosition = start;
            Raw = reader.ReadBytes(length);
        }
    }
}
