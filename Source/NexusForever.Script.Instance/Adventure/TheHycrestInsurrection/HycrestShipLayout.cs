using System.Numerics;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    /// <summary>
    /// Where the intro drop ship hovers and where things stand inside it. Keep in sync with the world database spawns
    /// (Instance/Adventure/The Hycrest Insurrection.sql).
    /// </summary>
    /// <remarks>
    /// The ship is the intro set ship (creature 70557, spawned as a platform). Its interior offsets come from the retail
    /// WorldLocation2 points around the set origin 49984 (identity rotation): player spots 50008/50009/50022, Dawson's
    /// spot 50021. The ship keeps the identity rotation so these offsets stay valid.
    /// </remarks>
    public static class HycrestShipLayout
    {
        // barn doorway measured in game: -2520.6, -929.1575, -1223.0962, facing out of the barn (-Z)
        // the ship hovers in front of it so players land on open ground in front of the door, not on the barn roof
        public const float DeckHeightAboveGround = 60f;
        private const float BarnDoorwayGroundY = -929.1575f;
        private const float DeckOffsetY = 4.54f;

        public static readonly Vector3 Origin = new(-2520.6f, BarnDoorwayGroundY + DeckHeightAboveGround - DeckOffsetY, -1240f);

        public static readonly float DeckY = Origin.Y + DeckOffsetY;

        // player spots inside the ship, offsets of 50008, 50009, 50022 from 49984 (+0.5 m so arrivals don't clip the floor)
        public static readonly Vector3[] PlayerSpots =
        [
            Origin + new Vector3(-2.02f, 4.80f, -2.59f),
            Origin + new Vector3(-3.51f, 5.04f, -0.08f),
            Origin + new Vector3(-2.05f, 5.45f, -2.67f)
        ];

        // Dawson's spot (offset of 50021), where the Caretaker hologram stands until Dawson comes out
        public static readonly Vector3 DawsonSpot = Origin + new Vector3(-6.55f, 4.54f, -1.51f);
        public const float DawsonYaw = -0.3093f;

        // anyone still on board when the ship leaves is moved here, just in front of the ship, and glides down
        public static readonly Vector3 DropPoint = Origin + new Vector3(0f, 2f, -15f);
    }
}
