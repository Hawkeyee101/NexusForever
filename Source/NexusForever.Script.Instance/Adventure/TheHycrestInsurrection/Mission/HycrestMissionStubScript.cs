using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Script.Template.Filter;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// Placeholder for missions that aren't built yet: the mission starts with its objectives from the client tables, but
    /// nothing completes them. Finish it with the dev command "map eventfinish &lt;event id&gt;" to move on.
    /// </summary>
    /// <remarks>
    /// Remove a mission's id here once it has its own script.
    /// </remarks>
    [ScriptFilterOwnerId(421u, 422u, 423u, 424u, 425u, 426u, 427u, 428u, 429u, 430u, 431u, 432u, 433u, 434u)]
    public class HycrestMissionStubScript : HycrestMissionScript
    {
        #region Dependency Injection

        private readonly ILogger<HycrestMissionStubScript> log;

        public HycrestMissionStubScript(
            ILogger<HycrestMissionStubScript> log)
        {
            this.log = log;
        }

        #endregion

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public override void OnLoad(IPublicEvent owner)
        {
            base.OnLoad(owner);
            log.LogInformation($"Hycrest: mission {owner.Id} isn't built yet, finish it with \"!map eventfinish {owner.Id}\".");
        }
    }
}
