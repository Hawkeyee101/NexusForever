using NexusForever.Game.Abstract.Entity;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// What the Shield Parts crates of Leveling the Field (422) pass on to the mission script.
    /// </summary>
    public interface ILevelingTheFieldMissionScript
    {
        /// <summary>
        /// <paramref name="player"/> stole the parts in <paramref name="crate"/> (its Stealing cast finished).
        /// </summary>
        void OnPartsStolen(IWorldEntity crate, IPlayer player);
    }
}
