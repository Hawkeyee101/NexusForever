using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Script.Template.Filter;
using NexusForever.Shared;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    [ScriptFilterOwnerId(1149)]
    public class TheHycrestInsurrectionMapScript : EventBaseContentMapScript, IUpdate
    {
        public override uint PublicEventId => HycrestPublicEvent.Main;

        /// <summary>
        /// Invoked when <see cref="IGridEntity"/> is added to map.
        /// </summary>
        public override void OnAddToMap(IGridEntity entity)
        {
            base.OnAddToMap(entity);

            if (entity is not IPlayer player)
                return;

            // the intro event is created by the main event script, the base class only joins the main event
            HycrestPublicEvent.JoinPublicTeam(map.PublicEventManager.GetEvent(HycrestPublicEvent.Intro), player);
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public void Update(double lastTick)
        {
            // public event scripts don't receive ticks from the engine, forward them so event scripts can run timers
            foreach (IPublicEvent publicEvent in map.PublicEventManager.GetEvents().ToList())
                publicEvent.InvokeScriptCollection<IUpdate>(s => s.Update(lastTick));
        }
    }
}
