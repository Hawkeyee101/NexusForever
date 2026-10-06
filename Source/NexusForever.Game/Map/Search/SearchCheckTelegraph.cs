using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map.Search;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Map.Search
{
    public class SearchCheckTelegraph : ISearchCheckTelegraph
    {
        private ITelegraph telegraph;
        private IUnitEntity caster;

        public void Initialise(ITelegraph telegraph, IUnitEntity caster)
        {
            if (this.telegraph != null)
                throw new InvalidOperationException("SearchCheckTelegraph has already been initialised.");

            this.telegraph = telegraph;
            this.caster    = caster;
        }

        public bool CheckEntity(IUnitEntity entity)
        {
            // the target type flags only say where the client draws the telegraph (1 caster, 2 target or field position,
            // 4 each target; client FUN_140546e60), not who it hits: that is the spell's target mechanics
            return telegraph.InsideTelegraph(entity.Position, entity.HitRadius);
        }
    }
}
