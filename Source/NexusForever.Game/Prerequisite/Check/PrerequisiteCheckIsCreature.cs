using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// The unit is the creature given as the value (Creature2 id).
    /// </summary>
    /// <remarks>
    /// E.g. Call Reinforcements (48499) can only be cast by the Dominion Recon Specialist 17823 (prerequisite 20600).
    /// </remarks>
    [PrerequisiteCheck(PrerequisiteType.IsCreature)]
    public class PrerequisiteCheckIsCreature : IPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckIsCreature> log;

        public PrerequisiteCheckIsCreature(
            ILogger<PrerequisiteCheckIsCreature> log)
        {
            this.log = log;
        }

        #endregion

        public bool Meets(IUnitEntity subject, PrerequisiteComparison comparison, uint value, uint objectId, IPrerequisiteParameters parameters)
        {
            switch (comparison)
            {
                case PrerequisiteComparison.Equal:
                    return subject.CreatureId == value;
                case PrerequisiteComparison.NotEqual:
                    return !(subject.CreatureId == value);
                default:
                    log.LogWarning($"Unhandled PrerequisiteComparison {comparison} for {PrerequisiteType.IsCreature}!");
                    return false;
            }
        }
    }
}
