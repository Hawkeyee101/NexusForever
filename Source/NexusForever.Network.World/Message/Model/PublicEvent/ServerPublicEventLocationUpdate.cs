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

        // the id is 32 bits, like in the unit and map region updates: with 14 bits the client showed nothing, with 32 it
        // shows the objective's map area and minimap icon (tested in game, 28 Sep 2026)
        public uint ObjectIdBits { get; set; } = 32u;

        public void Write(GamePacketWriter writer)
        {
            writer.Write(ObjectId, ObjectIdBits);
            writer.Write(Operation, 3u);
            writer.Write(WorldLocation2Id, 17u);
        }
    }
}
