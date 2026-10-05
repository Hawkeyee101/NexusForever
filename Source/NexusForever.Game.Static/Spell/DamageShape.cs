namespace NexusForever.Game.Static.Spell
{
    public enum DamageShape
    {
        Circle        = 0,
        Ring          = 1,
        Square        = 2,
        /// <summary>
        /// Two perpendicular boxes, the second with the long and width axes swapped.
        /// </summary>
        Cross         = 3,
        Cone          = 4,
        /// <summary>
        /// This is a pie shape, where the radius value is the size of the missing slice.
        /// </summary>
        Pie           = 5,
        /// <summary>
        /// A volume with its three radii given directly (no half extents), used by auras.
        /// </summary>
        Volume        = 6,
        Rectangle     = 7,
        LongCone      = 8
    }
}
