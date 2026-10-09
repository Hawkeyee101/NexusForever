using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.Game.Static.Entity.Movement.Command.State;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Prerequisite.Check
{
    /// <summary>
    /// Whether the unit is moving (1) or not (0), compared with the value: "NotEqual 0" is moving (e.g. 11119, the target
    /// check of the Esper's tier 4 buff removals 77174/77238 cast by their BeginMoving procs).
    /// </summary>
    /// <remarks>
    /// Moving is read from the movement state's Move flag (local, 5 Oct 2026).
    /// </remarks>
    [PrerequisiteCheck(PrerequisiteType.Moving)]
    public class PrerequisiteCheckMoving : IPrerequisiteCheck
    {
        #region Dependency Injection

        private readonly ILogger<PrerequisiteCheckMoving> log;

        public PrerequisiteCheckMoving(
            ILogger<PrerequisiteCheckMoving> log)
        {
            this.log = log;
        }

        #endregion

        public bool Meets(IUnitEntity subject, PrerequisiteComparison comparison, uint value, uint objectId, IPrerequisiteParameters parameters)
        {
            // a player's movement comes from the client (velocity), an NPC's from the server's move state
            bool isMoving = subject.MovementManager.HasVelocity || (subject.MovementManager.GetState() & StateFlags.Move) != 0;
            uint moving = isMoving ? 1u : 0u;
            switch (comparison)
            {
                case PrerequisiteComparison.Equal:
                    return moving == value;
                case PrerequisiteComparison.NotEqual:
                    return moving != value;
                default:
                    log.LogWarning($"Unhandled PrerequisiteComparison {comparison} for {PrerequisiteType.Moving}!");
                    return false;
            }
        }
    }
}
