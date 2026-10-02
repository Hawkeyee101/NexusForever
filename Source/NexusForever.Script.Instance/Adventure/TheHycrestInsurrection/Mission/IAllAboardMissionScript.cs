using NexusForever.Game.Abstract.Entity;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// What the train's lever and drive system in All Aboard (432) pass on to the mission script.
    /// </summary>
    public interface IAllAboardMissionScript
    {
        /// <summary>
        /// <paramref name="player"/> pulled the Force Field Lever <paramref name="lever"/> at the back of the locomotive.
        /// </summary>
        void OnLeverPulled(IWorldEntity lever, IPlayer player);

        /// <summary>
        /// <paramref name="player"/> planted the bomb at the PEC Drive System <paramref name="driveSystem"/> inside the locomotive.
        /// </summary>
        void OnBombPlanted(IWorldEntity driveSystem, IPlayer player);
    }
}
