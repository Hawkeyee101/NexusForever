using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// The server has no trigger volume data (client volumes, e.g. 2728 in Snipe's target check 5810, "You are out of
    /// bounds"), so a unit is never in one: the check yields 0, which is then compared with the value.
    /// </summary>
    /// <remarks>
    /// Local stand-in (5 Oct 2026): replace once the server knows the real state.
    /// </remarks>
    [PrerequisiteCheck(PrerequisiteType.InTriggerVolume)]
    public class PrerequisiteCheckInTriggerVolume : IPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckInTriggerVolume> log;

        public PrerequisiteCheckInTriggerVolume(
            ILogger<PrerequisiteCheckInTriggerVolume> log)
        {
            this.log = log;
        }

        #endregion

        public bool Meets(IUnitEntity subject, PrerequisiteComparison comparison, uint value, uint objectId, IPrerequisiteParameters parameters)
        {
            switch (comparison)
            {
                case PrerequisiteComparison.Equal:
                    return value == 0u;
                case PrerequisiteComparison.NotEqual:
                    return value != 0u;
                default:
                    log.LogWarning($"Unhandled PrerequisiteComparison {comparison} for {PrerequisiteType.InTriggerVolume}!");
                    return false;
            }
        }
    }
}
