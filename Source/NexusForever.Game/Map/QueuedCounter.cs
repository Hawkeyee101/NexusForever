using NexusForever.Game.Abstract.Map;

namespace NexusForever.Game.Map
{
    /// <summary>
    /// Hands out ids, reusing released ones once they have been free for <see cref="ReuseDelay"/>.
    /// </summary>
    /// <remarks>
    /// The client keeps a removed unit around for a moment (e.g. a despawn or fly-off), so a new unit that gets its id right
    /// away can take over the old one's state: a door spawned just after a ship left with the same guid vanished on the client.
    /// </remarks>
    public class QueuedCounter : IQueuedCounter
    {
        private static readonly TimeSpan ReuseDelay = TimeSpan.FromSeconds(30);

        private uint counter = 1;
        private readonly Queue<(uint Value, long Released)> queue = new();

        public uint Dequeue()
        {
            if (queue.TryPeek(out (uint Value, long Released) released)
                && Environment.TickCount64 - released.Released >= ReuseDelay.TotalMilliseconds)
            {
                queue.Dequeue();
                return released.Value;
            }

            return counter++;
        }

        public void Enqueue(uint value)
        {
            queue.Enqueue((value, Environment.TickCount64));
        }
    }
}
