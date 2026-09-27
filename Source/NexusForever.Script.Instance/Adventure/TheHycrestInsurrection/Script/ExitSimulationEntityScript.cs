using NexusForever.Game.Abstract.Entity;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Script
{
    /// <summary>
    /// Exit Simulation portal (creature 36869): its activate spell 40574 "Leave Simulation - Adventures" (5 s cast) takes
    /// the player out of the adventure, back to where they entered it from.
    /// </summary>
    [ScriptFilterCreatureId((uint)PublicEventCreature.ExitSimulation)]
    public class ExitSimulationEntityScript : IWorldEntityScript, IOwnedScript<IWorldEntity>
    {
        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public void OnLoad(IWorldEntity owner)
        {
        }

        /// <summary>
        /// Invoked when a player successfully activates the portal.
        /// </summary>
        public void OnActivateSuccess(IPlayer activator)
        {
            if (activator.ReturnPosition == null)
            {
                // e.g. logged in inside the adventure: the server doesn't know where the player came from
                activator.SendSystemMessage("Leave Simulation: no return location is known for you, use a teleport instead.");
                return;
            }

            activator.TeleportTo(activator.ReturnPosition);
        }
    }
}
