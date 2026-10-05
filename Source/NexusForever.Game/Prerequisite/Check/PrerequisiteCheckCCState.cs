using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Combat.CrowdControl;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// The unit is under the crowd control state given as the value.
    /// </summary>
    /// <remarks>
    /// E.g. Mortar Barrage's proxy 69305 persists while its caster is not knocked down or vulnerable (prerequisite 22657: NotEqual 8, 9, 10 = Knockdown, Vulnerability, VulnerabilityWithAct). Inferred from the values.
    /// </remarks>
    [PrerequisiteCheck(PrerequisiteType.CCState)]
    public class PrerequisiteCheckCCState : IPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckCCState> log;

        public PrerequisiteCheckCCState(
            ILogger<PrerequisiteCheckCCState> log)
        {
            this.log = log;
        }

        #endregion

        public bool Meets(IUnitEntity subject, PrerequisiteComparison comparison, uint value, uint objectId, IPrerequisiteParameters parameters)
        {
            switch (comparison)
            {
                case PrerequisiteComparison.Equal:
                    return subject.CrowdControlManager?.GetCCStates().Contains((CCState)value) == true;
                case PrerequisiteComparison.NotEqual:
                    return !(subject.CrowdControlManager?.GetCCStates().Contains((CCState)value) == true);
                default:
                    log.LogWarning($"Unhandled PrerequisiteComparison {comparison} for {PrerequisiteType.CCState}!");
                    return false;
            }
        }
    }
}
