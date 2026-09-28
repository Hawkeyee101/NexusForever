using NexusForever.Game.Static.PublicEvent;
using NexusForever.Network.Message;

namespace NexusForever.Network.World.Message.Model.PublicEvent
{
    [Message(GameMessageOpcode.ServerPublicEventLocationUpdate)]
    public class ServerPublicEventLocationUpdate : IWritable
    {
        public uint ObjectId { get; set; } // Can be either PublicEventId or PublicEventObjectiveId depending on the operation
        public PublicEventOperationType Operation { get; set; }
        public uint WorldLocation2Id { get; set; }

        // dev: the id's width isn't confirmed (14 bits here, the related unit and map region updates use 32)
        public uint ObjectIdBits { get; set; } = 14u;

        public void Write(GamePacketWriter writer)
        {
            writer.Write(ObjectId, ObjectIdBits);
            writer.Write(Operation, 3u);
            writer.Write(WorldLocation2Id, 17u);
        }
    }
}
