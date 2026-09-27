using System.Numerics;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    /// <summary>
    /// Where the intro drop ship hovers and where things stand inside it. Keep in sync with the world database spawns
    /// (Instance/Adventure/The Hycrest Insurrection.sql).
    /// </summary>
    /// <remarks>
    /// The ship is the intro set ship (creature 70557, spawned as a platform), confirmed in game: its interior matches
    /// the retail videos. Positions inside were measured in game with !entity info (27 Sep 2026). The ship has two folded
    /// walkways built in, one on each side; in retail only the right one (+X) extends, and that is the exit.
    /// </remarks>
    public static class HycrestShipLayout
    {
        // barn doorway measured in game: -2520.6, -929.1575, -1223.0962, facing out of the barn (-Z)
        // the ship hovers in front of it so players land on open ground in front of the door, not on the barn roof
        public const float DeckHeightAboveGround = 60f;
        private const float BarnDoorwayGroundY = -929.1575f;
        private const float SpawnOffsetY = 4.54f;

        public static readonly Vector3 Origin = new(-2520.6f, BarnDoorwayGroundY + DeckHeightAboveGround - SpawnOffsetY, -1240f);

        /// <summary>
        /// Height of the ship's interior floor, measured at four spots (-873.70 to -873.82).
        /// </summary>
        public const float FloorY = -873.76f;

        // player arrival spot, measured (+0.5 m so arrivals don't start in the floor); more spots for party members to follow
        public static readonly Vector3[] PlayerSpots =
        [
            new(-2516.7417f, -873.8232f + 0.5f, -1233.5726f)
        ];

        // in front of the door with the red light strip, facing into the room: the Caretaker hologram, measured
        public static readonly Vector3 HologramSpot = new(-2521.1797f, -873.81055f, -1244.1765f);
        public const float HologramYaw = -3.1174135f;

        // Dawson's spot, measured
        public static readonly Vector3 DawsonSpot = new(-2524.8179f, -873.7381f, -1243.8883f);
        public const float DawsonYaw = -2.81273f;

        // in front of the right-side walkway, the exit (facing out, +X), measured
        public static readonly Vector3 ExitWalkway = new(-2513.4956f, -873.69946f, -1241.1969f);

        // anyone still on board when the ship leaves is moved just outside the right side of the hull (the ship is 63 m
        // wide, its +X side at ~-2489) and glides down; ESTIMATE until the extended walkway's end is known
        public static readonly Vector3 DropPoint = new(-2483.5f, FloorY + 1f, ExitWalkway.Z);
    }
}
