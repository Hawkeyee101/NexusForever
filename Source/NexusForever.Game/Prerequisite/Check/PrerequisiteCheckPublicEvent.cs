using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    [PrerequisiteCheck(PrerequisiteType.PublicEvent117)]
    public class PrerequisiteCheckPublicEvent : IPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckPublicEvent> log;

        public PrerequisiteCheckPublicEvent(
            ILogger<PrerequisiteCheckPublicEvent> log)
        {
            this.log = log;
        }

        #endregion

        /// <remarks>
        /// PublicEventIsActive (krakal's reverse engineering of the client): objectId is the public event, met while it is
        /// active on the unit's map. E.g. the carry buff of Leveling the Field (spell 57748, caster persistence 23646, event
        /// 422) lasts while the event runs. Most rows (408 of 455 in 16042) have value 0; other values are unknown.
        /// </remarks>
        public bool Meets(IUnitEntity subject, PrerequisiteComparison comparison, uint value, uint objectId, IPrerequisiteParameters parameters)
        {
            if (value != 0u)
            {
                log.LogWarning($"Unhandled value {value} for {PrerequisiteType.PublicEvent117}!");
                return false;
            }

            IPublicEvent publicEvent = subject.Map?.PublicEventManager.GetEvent(objectId);
            bool inEvent = publicEvent != null && !publicEvent.HasFinished;

            switch (comparison)
            {
                case PrerequisiteComparison.Equal:
                    return inEvent;
                case PrerequisiteComparison.NotEqual:
                    return !inEvent;
                default:
                    log.LogWarning($"Unhandled PrerequisiteComparison {comparison} for {PrerequisiteType.PublicEvent117}!");
                    return false;
            }
        }
    }
}
