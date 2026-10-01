using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Spell;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Script
{
    /// <summary>
    /// Arsenax Severus in Breach of Protocol (creature 48373, "T3 Ambush"; the end boss is 17928): he can't be beaten, his
    /// health doesn't go below half. The mission script lets him leave at that point or after 30 s; before he joins and when
    /// he leaves it keeps him out of reach (`IsInvulnerable`).
    /// </summary>
    /// <remarks>
    /// Retail ends the fight on damage to his shield; until shields and the spell rework allow that, health stands in.
    /// The engine calls <see cref="OnHealthChange"/> after lowering the health and before its death check, so raising
    /// the health back here also keeps a single big hit from killing him.
    /// </remarks>
    [ScriptFilterCreatureId(48373u)]
    public class ArsenaxAmbushEntityScript : INonPlayerScript, IOwnedScript<INonPlayerEntity>
    {
        public const float HealthFloor = 0.5f;

        private INonPlayerEntity entity;

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public void OnLoad(INonPlayerEntity owner)
        {
            entity = owner;
        }

        /// <summary>
        /// Invoked when health is changed by source <see cref="IUnitEntity"/>.
        /// </summary>
        public void OnHealthChange(IUnitEntity source, uint amount, DamageType? type)
        {
            if (type is DamageType.Heal or null)
                return;

            uint floor = (uint)(entity.MaxHealth * HealthFloor);
            if (entity.Health < floor)
                entity.ModifyHealth(floor - entity.Health, DamageType.Heal, null);
        }
    }
}
