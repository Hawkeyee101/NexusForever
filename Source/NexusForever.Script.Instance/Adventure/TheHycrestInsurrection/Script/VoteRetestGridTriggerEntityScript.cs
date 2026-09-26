using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Trigger;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Script
{
    /// <summary>
    /// Development aid in the Abandoned Barn: after the intro, entering it again restarts the mission vote.
    /// Only created when <see cref="TheHycrestInsurrectionEventScript.AllowVoteRetest"/> is set.
    /// </summary>
    [ScriptFilterOwnerId(TheHycrestInsurrectionEventScript.VoteRetestTriggerId)]
    public class VoteRetestGridTriggerEntityScript : IGridEntityScript, IOwnedScript<IGridTriggerEntity>
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

            IPublicEvent publicEvent = trigger.Map.PublicEventManager.GetEvent(HycrestPublicEvent.Main);
            publicEvent?.InvokeScriptCollection<TheHycrestInsurrectionEventScript>(s => s.StartMissionVote());
        }
    }
}
