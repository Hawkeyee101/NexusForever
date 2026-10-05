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
        private float searchRadius;

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

            searchRadius    = GetSearchRadius();

            searchCheck.Initialise(caster, initialPosition, searchRadius, info.BaseInfo.TargetMechanics.Flags);
        }

        /// <summary>
        /// The spell's range, or further if its telegraphs reach further (the telegraph filter then decides): Eradicate 66395
        /// has a 10 m range and a 15 m cone, and found no one from 12 m (Teun, 5 Oct 2026). 0 stays unlimited.
        /// </summary>
        private float GetSearchRadius()
        {
            float range = info.Entry.TargetMaxRange;
            if (range <= 0f)
                return range;

            float reach = info.Telegraphs
                .Select(t => MathF.Max(t.Param00, MathF.Max(t.Param01, t.Param02)) + MathF.Abs(t.XPositionOffset) + MathF.Abs(t.ZPositionOffset))
                .DefaultIfEmpty(0f)
                .Max();
            return MathF.Max(range, reach);
        }

        public void SelectTargets(List<ISpellTargetImplicit> implicitTargets)
        {
            foreach (IUnitEntity target in caster.Map.Search(initialPosition, searchRadius, searchCheck))
                implicitTargets.Add(new SpellTargetImplicit(target, Vector3.Distance(initialPosition, target.Position)));
        }
    }
}
