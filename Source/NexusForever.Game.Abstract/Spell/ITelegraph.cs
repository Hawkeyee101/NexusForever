using System.Numerics;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Abstract.Spell
{
    public interface ITelegraph
    {
        IUnitEntity Caster { get; }
        Vector3 Position { get; }
        Vector3 Rotation { get; }
        TelegraphDamageEntry TelegraphDamage { get; }
        TelegraphTargetTypeFlags TelegraphTargetTypeFlags { get; }

        /// <summary>
        /// The unit the telegraph is attached to: the caster, or for a per target telegraph (target type flag 4) that target.
        /// </summary>
        uint AttachedUnitId { get; }

        /// <summary>
        /// The position entry flags the client matches against the telegraph's target type flags to draw it (1 caster,
        /// 2 target or field position, 4 each target).
        /// </summary>
        byte PositionFlags { get; }

        /// <summary>
        /// Returns whether the supplied <see cref="Vector3"/> is inside the telegraph.
        /// </summary>
        bool InsideTelegraph(Vector3 position, float hitRadius);
    }
}