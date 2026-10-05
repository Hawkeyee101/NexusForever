using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// The instance difficulty: value 0 normal, 1 veteran.
    /// </summary>
    /// <remarks>
    /// E.g. Ondu's Line Smash (42420) applies different effects per difficulty (prerequisites 14409, 14893). Veteran difficulty isn't tracked yet, so every map counts as normal.
    /// </remarks>
    [PrerequisiteCheck(PrerequisiteType.Difficulty)]
    public class PrerequisiteCheckDifficulty : IPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckDifficulty> log;

        public PrerequisiteCheckDifficulty(
            ILogger<PrerequisiteCheckDifficulty> log)
        {
            this.log = log;
        }

        #endregion

        public bool Meets(IUnitEntity subject, PrerequisiteComparison comparison, uint value, uint objectId, IPrerequisiteParameters parameters)
        {
            const uint difficulty = 0u;

            switch (comparison)
            {
                case PrerequisiteComparison.Equal:
                    return difficulty == value;
                case PrerequisiteComparison.NotEqual:
                    return !(difficulty == value);
                default:
                    log.LogWarning($"Unhandled PrerequisiteComparison {comparison} for {PrerequisiteType.Difficulty}!");
                    return false;
            }
        }
    }
}
