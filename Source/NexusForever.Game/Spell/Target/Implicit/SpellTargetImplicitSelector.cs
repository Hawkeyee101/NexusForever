using System.Numerics;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map.Search;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Info;
using NexusForever.Game.Abstract.Spell.Target.Implicit;

namespace NexusForever.Game.Spell.Target.Implicit
{
    public class SpellTargetImplicitSelector : ISpellTargetImplicitSelector
    {
        private IUnitEntity caster;
        private Vector3 initialPosition;
        private ISpellInfo info;

        #region Dependency Injection

        private readonly ISearchCheckSpellTargetImplicit searchCheck;

        public SpellTargetImplicitSelector(
            ISearchCheckSpellTargetImplicit searchCheck)
        {
            this.searchCheck = searchCheck;
        }

        #endregion

        public void Initialise(IUnitEntity caster, ISpellParameters parameters, Vector3 position)
        {
            if (this.caster != null)
                throw new InvalidOperationException();

            this.caster     = caster;
            info            = parameters.SpellInfo;
            initialPosition = position;

            searchCheck.Initialise(caster, initialPosition, info.Entry.TargetMaxRange, info.BaseInfo.TargetMechanics.Flags);
        }

        public void SelectTargets(List<ISpellTargetImplicit> implicitTargets)
        {
            foreach (IUnitEntity target in caster.Map.Search(initialPosition, info.Entry.TargetMaxRange, searchCheck))
                implicitTargets.Add(new SpellTargetImplicit(target, Vector3.Distance(initialPosition, target.Position)));
        }
    }
}
