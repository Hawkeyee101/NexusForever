using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    [PrerequisiteCheck(PrerequisiteType.InCombat)]
    public class PrerequisiteCheckInCombat : IPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckInCombat> log;

        public PrerequisiteCheckInCombat(
            ILogger<PrerequisiteCheckInCombat> log)
        {
            this.log = log;
        }

        #endregion

        public bool Meets(IUnitEntity subject, PrerequisiteComparison comparison, uint value, uint objectId, IPrerequisiteParameters parameters)
        {
            // the combat state (1 in combat, 0 not) is compared with the value: "NotEqual 0" is in combat (Snipe's target
            // check 24281), "Equal 0" out of combat
            uint inCombat = subject.InCombat ? 1u : 0u;
            switch (comparison)
            {
                case PrerequisiteComparison.Equal:
                    return inCombat == value;
                case PrerequisiteComparison.NotEqual:
                    return inCombat != value;
                default:
                    log.LogWarning($"Unhandled PrerequisiteComparison {comparison} for {PrerequisiteType.InCombat}!");
                    return false;
            }
        }
    }
}
