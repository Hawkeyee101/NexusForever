using NexusForever.Game.Abstract.Entity;

namespace NexusForever.Game.Abstract.Spell.Target.Implicit.Filter
{
    public interface ISpellTargetImplicitTelegraphFilter : ISpellTargetImplicitFilter
    {
        /// <summary>
        /// Initialise the filter with every valid <see cref="ITelegraph"/> of the spell, a target has to be inside at least one of them.
        /// </summary>
        void Initialise(IEnumerable<ITelegraph> telegraphs, IUnitEntity caster);
    }
}
