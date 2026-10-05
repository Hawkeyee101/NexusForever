using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// The unit's death state: value 0 is alive.
    /// </summary>
    /// <remarks>
    /// E.g. Call Reinforcements (48499) persists while its caster is alive (prerequisite 22: type 12, Equal, value 0). Only alive and dead are told apart here.
    /// </remarks>
    [PrerequisiteCheck(PrerequisiteType.DeadState)]
    public class PrerequisiteCheckDeadState : IPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckDeadState> log;

        public PrerequisiteCheckDeadState(
            ILogger<PrerequisiteCheckDeadState> log)
        {
            this.log = log;
        }

        #endregion

        public bool Meets(IUnitEntity subject, PrerequisiteComparison comparison, uint value, uint objectId, IPrerequisiteParameters parameters)
        {
            uint deathState = subject.IsAlive ? 0u : 1u;

            switch (comparison)
            {
                case PrerequisiteComparison.Equal:
                    return deathState == value;
                case PrerequisiteComparison.NotEqual:
                    return !(deathState == value);
                default:
                    log.LogWarning($"Unhandled PrerequisiteComparison {comparison} for {PrerequisiteType.DeadState}!");
                    return false;
            }
        }
    }
}
