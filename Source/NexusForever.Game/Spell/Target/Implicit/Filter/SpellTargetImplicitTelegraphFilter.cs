using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map.Search;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Target.Implicit;
using NexusForever.Game.Abstract.Spell.Target.Implicit.Filter;
using NexusForever.Game.Static.Spell.Target;
using NexusForever.Shared;

namespace NexusForever.Game.Spell.Target.Implicit.Filter
{
    public class SpellTargetImplicitTelegraphFilter : ISpellTargetImplicitTelegraphFilter
    {
        private readonly List<ISearchCheckTelegraph> searchChecks = [];
        private bool initialised;

        #region Dependency Injection

        private readonly IFactory<ISearchCheckTelegraph> searchCheckTelegraphFactory;

        public SpellTargetImplicitTelegraphFilter(
            IFactory<ISearchCheckTelegraph> searchCheckTelegraphFactory)
        {
            this.searchCheckTelegraphFactory = searchCheckTelegraphFactory;
        }

        #endregion

        public void Initialise(IEnumerable<ITelegraph> telegraphs, IUnitEntity caster)
        {
            if (initialised)
                throw new InvalidOperationException("SpellTargetImplicitTelegraphFilter has already been initialised.");

            initialised = true;

            foreach (ITelegraph telegraph in telegraphs)
            {
                ISearchCheckTelegraph searchCheck = searchCheckTelegraphFactory.Resolve();
                searchCheck.Initialise(telegraph, caster);
                searchChecks.Add(searchCheck);
            }
        }

        public void Filter(List<ISpellTargetImplicit> targets)
        {
            if (searchChecks.Count == 0)
                return;

            // the telegraphs of a spell together make up its area, a target inside any of them is hit
            // (e.g. Shell Storm 66281 is a small circle around the caster plus a cone in front of it)
            foreach (ISpellTargetImplicit target in targets)
                if (!searchChecks.Any(c => c.CheckEntity(target.Entity)))
                    target.Result = SpellTargetImplicitSelectionResult.OutsideTelegraph;
        }

        // TODO: this needs more research...
        /*private bool EvaluateDamageFlagsForTarget(IGridEntity target, ISpell spell)
        {
            TelegraphDamageFlag damageFlag = (TelegraphDamageFlag)TelegraphDamage.TelegraphDamageFlags;

            // This is Invalid
            //if (damageFlag.HasFlag(TelegraphDamageFlag.SpellMustBeMultiPhase))
            //    if (spell.CastMethod != CastMethod.Multiphase)
            //        return false;

            if (damageFlag.HasFlag(TelegraphDamageFlag.CasterMustBeNPC))
                if (Caster is IPlayer)
                    return false;

            if (damageFlag.HasFlag(TelegraphDamageFlag.CasterMustBePlayer))
                if (Caster is not IPlayer)
                    return false;

            if (damageFlag.HasFlag(TelegraphDamageFlag.TargetMustBeUnit))
                if (target is not IUnitEntity)
                    return false;

            return true;
        }*/
    }
}
