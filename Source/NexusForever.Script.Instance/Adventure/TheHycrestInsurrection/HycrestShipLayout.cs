using System.Numerics;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    /// <summary>
    /// Where the intro drop ship hovers and where things stand inside it. Keep in sync with the world database spawns
    /// (Instance/Adventure/The Hycrest Insurrection.sql).
    /// </summary>
    /// <remarks>
    /// The ship is the intro set ship (creature 70557, spawned as a platform), confirmed in game: its interior matches
    /// the retail videos. Horizontal offsets come from the retail WorldLocation2 points around the set origin 49984
    /// (identity rotation): player spots 50008/50009/50022, Dawson's spot 50021, the end of the walkway 50011. Those
    /// points stand ~4.54 m above 49984 (Dawson's spot); in game the floor is 1.38 m above the ship's spawn position,
    /// so heights are taken relative to the measured floor. The ship keeps the identity rotation so the offsets stay valid.
    /// </remarks>
    public static class HycrestShipLayout
    {
        // barn doorway measured in game: -2520.6, -929.1575, -1223.0962, facing out of the barn (-Z)
        // the ship hovers in front of it so players land on open ground in front of the door, not on the barn roof
        public const float DeckHeightAboveGround = 60f;
        private const float BarnDoorwayGroundY = -929.1575f;
        private const float SpawnOffsetY = 4.54f;

        // measured in game 27 Sep 2026: standing on the interior floor at Y -872.3211, 1.38 m above the spawn position
        private const float FloorOffsetY = 1.38f;

        // height of the retail interior points above the set origin 49984 that corresponds to the floor (Dawson's 50021)
        private const float RetailFloorOffsetY = 4.54f;

        public static readonly Vector3 Origin = new(-2520.6f, BarnDoorwayGroundY + DeckHeightAboveGround - SpawnOffsetY, -1240f);

        /// <summary>
        /// Height of the ship's interior floor.
        /// </summary>
        public static readonly float FloorY = Origin.Y + FloorOffsetY;

        // player spots inside the ship, offsets of 50008, 50009, 50022 from 49984 (+0.5 m so arrivals don't clip the floor)
        public static readonly Vector3[] PlayerSpots =
        [
            Interior(-2.02f, 4.30f, -2.59f) + new Vector3(0f, 0.5f, 0f),
            Interior(-3.51f, 4.54f, -0.08f) + new Vector3(0f, 0.5f, 0f),
            Interior(-2.05f, 4.95f, -2.67f) + new Vector3(0f, 0.5f, 0f)
        ];

        // ESTIMATE, to be measured: in front of the door with the red light strip, where the Caretaker hologram stands
        // and Dawson comes out (offset of 50021)
        public static readonly Vector3 DawsonSpot = Interior(-6.55f, 4.54f, -1.51f);
        public const float DawsonYaw = -0.3093f;

        // ESTIMATE, to be measured: the end of the extended walkway, where players jump off (offset of 50011, facing out, -Z)
        public static readonly Vector3 JumpPoint = Interior(-0.66f, 5.42f, -25.77f);

        // anyone still on board when the ship leaves is moved just past the end of the walkway and glides down
        public static readonly Vector3 DropPoint = JumpPoint + new Vector3(0f, 1f, -3f);

        private static Vector3 Interior(float x, float retailY, float z)
        {
            return new Vector3(Origin.X + x, FloorY + (retailY - RetailFloorOffsetY), Origin.Z + z);
        }
    }
}
