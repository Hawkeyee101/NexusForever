using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Trigger;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Script
{
    /// <summary>
    /// Test trigger in the Abandoned Orchards, entering it starts the first mission vote.
    /// TODO: test-only, the real vote start is completing intro event 418 (see TheHycrestInsurrectionEventScript).
    /// </summary>
    [ScriptFilterOwnerId(TheHycrestInsurrectionEventScript.VoteTestTriggerId)]
    public class VoteTestGridTriggerEntityScript : IGridEntityScript, IOwnedScript<IGridTriggerEntity>
    {
        private IGridTriggerEntity trigger;

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public void OnLoad(IGridTriggerEntity owner)
        {
            trigger = owner;
        }

        /// <summary>
        /// Invoked when <see cref="IGridEntity"/> is added to range check range.
        /// </summary>
        public void OnEnterRange(IGridEntity entity)
        {
            if (entity is not IPlayer)
                return;

            IPublicEvent publicEvent = trigger.Map.PublicEventManager.GetEvent(TheHycrestInsurrectionEventScript.PublicEventId);
            publicEvent?.InvokeScriptCollection<TheHycrestInsurrectionEventScript>(s => s.StartMissionVote());
        }
    }
}
