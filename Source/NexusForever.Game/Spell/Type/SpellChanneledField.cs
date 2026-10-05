using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Target;
using NexusForever.Game.Abstract.Spell.Validator;
using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Spell.Type
{
    /// <summary>
    /// A channel on a field (e.g. a ground area) rather than on a target.
    /// </summary>
    /// <remarks>
    /// Executes like a <see cref="SpellChanneled"/> for now, which at least lets the spell execute and finish instead of
    /// staying in its initiating state forever (an NPC casting one never did anything else again).
    /// </remarks>
    public class SpellChanneledField : SpellChanneled
    {
        public override CastMethod CastMethod => CastMethod.ChanneledField;

        public SpellChanneledField(
            ILogger<SpellChanneledField> log,
            ISpellTargetInfoCollection spellTargetInfoCollection,
            IGlobalSpellManager globalSpellManager,
            ICastResultValidatorManager castResultValidatorManager,
            IDisableManager disableManager)
            : base(log, spellTargetInfoCollection, globalSpellManager, castResultValidatorManager, disableManager)
        {
        }
    }
}
