using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// The server has no stealth state for this check (e.g. Snipe's target check 5810), so a unit is never stealthed:
    /// the check yields 0, which is then compared with the value.
    /// </summary>
    /// <remarks>
    /// Local stand-in (5 Oct 2026): replace once the server knows the real state.
    /// </remarks>
    [PrerequisiteCheck(PrerequisiteType.Stealth)]
    public class PrerequisiteCheckStealth : IPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckStealth> log;

        public PrerequisiteCheckStealth(
            ILogger<PrerequisiteCheckStealth> log)
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
                    log.LogWarning($"Unhandled PrerequisiteComparison {comparison} for {PrerequisiteType.Stealth}!");
                    return false;
            }
        }
    }
}
