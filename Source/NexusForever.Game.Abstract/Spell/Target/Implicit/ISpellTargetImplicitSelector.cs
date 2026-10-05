using System.Numerics;
using NexusForever.Game.Abstract.Entity;

namespace NexusForever.Game.Abstract.Spell.Target.Implicit
{
    public interface ISpellTargetImplicitSelector
    {
        /// <summary>
        /// Initialise the selector to search around <paramref name="position"/>, the caster or where the spell lands.
        /// </summary>
        void Initialise(IUnitEntity caster, ISpellParameters parameters, Vector3 position);

        void SelectTargets(List<ISpellTargetImplicit> implicitTargets);
    }
}
