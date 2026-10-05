using NexusForever.Game.Abstract.Entity;
using NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Script
{
    /// <summary>
    /// The Shield Parts crates of Leveling the Field (creature 48056, activate spell 49499 "Stealing", 3 s): the mission
    /// script hands the parts to the player.
    /// </summary>
    [ScriptFilterCreatureId(48056u)]
    public class ShieldPartsEntityScript : IWorldEntityScript, IOwnedScript<IWorldEntity>
    {
        private IWorldEntity entity;

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public void OnLoad(IWorldEntity owner)
        {
            entity = owner;
        }

        /// <summary>
        /// Invoked when <see cref="IWorldEntity"/> is successfully activated by <see cref="IPlayer"/>.
        /// </summary>
        public void OnActivateSuccess(IPlayer activator)
        {
            entity.Map?.PublicEventManager?.GetEvent(HycrestPublicEvent.LevelingTheField)?
                .InvokeScriptCollection<ILevelingTheFieldMissionScript>(s => s.OnPartsStolen(entity, activator));
        }
    }
}
