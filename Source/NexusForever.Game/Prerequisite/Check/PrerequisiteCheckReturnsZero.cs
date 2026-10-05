using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// The client's check for this type always yields 0, which is then compared with the value.
    /// </summary>
    /// <remarks>
    /// krakal's reverse engineering of the client's prerequisite processors ("ReturnsZero", Discord, 12 Aug 2026). Rows use
    /// it as "NotEqual &lt;a Spell4Base id&gt;" (e.g. Call Reinforcements 48499, prerequisite 20600), which is then always met.
    /// </remarks>
    [PrerequisiteCheck(PrerequisiteType.ReturnsZero)]
    public class PrerequisiteCheckReturnsZero : IPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckReturnsZero> log;

        public PrerequisiteCheckReturnsZero(
            ILogger<PrerequisiteCheckReturnsZero> log)
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
                    log.LogWarning($"Unhandled PrerequisiteComparison {comparison} for {PrerequisiteType.ReturnsZero}!");
                    return false;
            }
        }
    }
}
