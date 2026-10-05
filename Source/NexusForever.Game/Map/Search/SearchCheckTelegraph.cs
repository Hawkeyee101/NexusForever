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
            // flag 2 ("Self") isn't "only the caster": every ground targeted telegraph has it (Mortar Barrage 7106/7107,
            // Energy Leap, Napalm Grenade, Wild Barrage, Exterminate 8554), placed at the target's position, so it doesn't
            // limit who is hit (local reading, 5 Oct 2026)
            if (telegraph.TelegraphTargetTypeFlags.HasFlag(TelegraphTargetTypeFlags.Other) && entity == caster)
                return false;

            return telegraph.InsideTelegraph(entity.Position, entity.HitRadius);
        }
    }
}
