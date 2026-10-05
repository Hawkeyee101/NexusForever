using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// The unit's difficulty rank (Creature2Difficulty id: 1 Fodder, 2 Minion, ... 9 Grunt, 10 Challenger, 11 Superior, ...)
    /// compared with the prerequisite's objectId.
    /// </summary>
    /// <remarks>
    /// NPC spells carry one effect per rank, gated by this type through PrerequisiteIdCasterApply. Plasma Shot (65717): 29329
    /// "rank &lt;= 7", 29396 "rank &gt;= 8 and &lt;= 10", 29330 "rank &gt;= 11", which split the ranks without gaps or overlap.
    /// Every row of this type in 16042 compares objectId 1-12 with value 0. A unit without creature data (a player) has rank 0.
    /// </remarks>
    [PrerequisiteCheck(PrerequisiteType.CreatureDifficulty)]
    public class PrerequisiteCheckCreatureDifficulty : IPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckCreatureDifficulty> log;

        public PrerequisiteCheckCreatureDifficulty(
            ILogger<PrerequisiteCheckCreatureDifficulty> log)
        {
            this.log = log;
        }

        #endregion

        public bool Meets(IUnitEntity subject, PrerequisiteComparison comparison, uint value, uint objectId, IPrerequisiteParameters parameters)
        {
            uint difficulty = subject.CreatureInfo?.Entry?.Creature2DifficultyId ?? 0u;

            switch (comparison)
            {
                case PrerequisiteComparison.Equal:
                    return difficulty == objectId;
                case PrerequisiteComparison.NotEqual:
                    return difficulty != objectId;
                case PrerequisiteComparison.GreaterThan:
                    return difficulty > objectId;
                case PrerequisiteComparison.GreaterThanOrEqual:
                    return difficulty >= objectId;
                case PrerequisiteComparison.LessThan:
                    return difficulty < objectId;
                case PrerequisiteComparison.LessThanOrEqual:
                    return difficulty <= objectId;
                default:
                    log.LogWarning($"Unhandled PrerequisiteComparison {comparison} for {PrerequisiteType.CreatureDifficulty}!");
                    return false;
            }
        }
    }
}
