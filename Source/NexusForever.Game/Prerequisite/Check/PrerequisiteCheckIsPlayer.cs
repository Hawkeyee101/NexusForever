using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// The unit is a player.
    /// </summary>
    /// <remarks>
    /// E.g. Snipe (27324) has separate effects for player and non-player targets (prerequisites 1050, 960).
    /// </remarks>
    [PrerequisiteCheck(PrerequisiteType.IsPlayer)]
    public class PrerequisiteCheckIsPlayer : IPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckIsPlayer> log;

        public PrerequisiteCheckIsPlayer(
            ILogger<PrerequisiteCheckIsPlayer> log)
        {
            this.log = log;
        }

        #endregion

        public bool Meets(IUnitEntity subject, PrerequisiteComparison comparison, uint value, uint objectId, IPrerequisiteParameters parameters)
        {
            switch (comparison)
            {
                case PrerequisiteComparison.Equal:
                    return subject is IPlayer;
                case PrerequisiteComparison.NotEqual:
                    return !(subject is IPlayer);
                default:
                    log.LogWarning($"Unhandled PrerequisiteComparison {comparison} for {PrerequisiteType.IsPlayer}!");
                    return false;
            }
        }
    }
}
