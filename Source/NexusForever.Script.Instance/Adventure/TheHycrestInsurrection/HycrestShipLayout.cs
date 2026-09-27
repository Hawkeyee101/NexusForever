using System.Numerics;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    /// <summary>
    /// Where the intro drop ship hovers and where things stand inside it. Keep in sync with the world database spawns
    /// (Instance/Adventure/The Hycrest Insurrection.sql).
    /// </summary>
    /// <remarks>
    /// The ship is the Dominion Imperium Transport 17722 (confirmed against the retail videos), with the door entities
    /// 18338 (right) and 28509 (left) over its always-open doorways. Offsets were measured in game on summoned copies
    /// (27 Sep 2026) and are in the ship's own frame (right ramp along +X, doorway at -17 m); a yaw r turns a local
    /// offset (x, z) into world (x cos r + z sin r, -x sin r + z cos r). The ship is turned -90 degrees so the right ramp
    /// points north and its lower end touches down in front of the Abandoned Barn.
    /// </remarks>
    public static class HycrestShipLayout
    {
        /// <summary>
        /// The ship's position; the right ramp's lower end (15.60, -9.23, -17.19) touches down in front of the barn at
        /// (-2520.6306, -929.33386, -1229.9689), measured.
        /// </summary>
        public static readonly Vector3 Origin = new(-2537.821f, -920.104f, -1245.569f);
        public const float Yaw = -1.5708f;

        /// <summary>
        /// Height of the ship's interior floor (3.69 m below the ship's position).
        /// </summary>
        public const float FloorY = -923.794f;

        // arrival spots, measured (+0.5 m so arrivals don't start in the floor); one per party member, the sixth is spare
        public static readonly Vector3[] PlayerSpots =
        [
            new(-2525.451f, -923.794f + 0.5f, -1243.369f),
            new(-2527.751f, -923.374f + 0.5f, -1244.049f),
            new(-2531.051f, -922.794f + 0.5f, -1244.999f),
            new(-2530.411f, -922.774f + 0.5f, -1247.769f),
            new(-2526.551f, -923.844f + 0.5f, -1247.689f),
            new(-2523.891f, -923.804f + 0.5f, -1248.039f)
        ];

        // in front of the door with the red light strip, where the Caretaker hologram stands, and Dawson's spot, measured
        public static readonly Vector3 HologramSpot = new(-2520.181f, -923.794f, -1245.839f);
        public const float HologramYaw = 1.6108f;
        public static readonly Vector3 DawsonSpot = new(-2519.001f, -923.724f, -1246.529f);
        public const float DawsonYaw = 1.5913f;

        // both door entities stand at this point (on the ship's centre line, 13.28 m forward, 3.28 m down), turned like the ship
        public static readonly Vector3 DoorPoint = new(-2524.541f, -923.384f, -1245.439f);

        // the right ramp, the exit: its top at the doorway and its lower end on the ground in front of the barn
        public static readonly Vector3 RampTop = new(-2520.771f, -923.144f, -1239.249f);
        public static readonly Vector3 RampEnd = new(-2520.631f, -929.334f, -1229.969f);

        // anyone still on board when the ship leaves is moved to the foot of the ramp
        public static readonly Vector3 DropPoint = RampEnd + new Vector3(0f, 0.5f, 1f);
    }
}
