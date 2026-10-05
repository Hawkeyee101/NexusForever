using NexusForever.Game.Abstract.Entity;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// What the jetpacks and the Chua Warblimp's deck of Jailbreak (425) pass on to the mission script.
    /// </summary>
    public interface IJailbreakMissionScript
    {
        /// <summary>
        /// <paramref name="player"/> used a Jetpack (its activate cast finished): launch them up to the blimp.
        /// </summary>
        void OnJetpackUsed(IWorldEntity jetpack, IPlayer player);

        /// <summary>
        /// <paramref name="player"/> activated the Navigation Array: the stabilizers have to be overloaded in time.
        /// </summary>
        void OnNavigationArrayActivated(IWorldEntity array, IPlayer player);

        /// <summary>
        /// <paramref name="player"/> overloaded a Balance Stabilizer (its activate cast finished).
        /// </summary>
        void OnStabilizerOverloaded(IWorldEntity stabilizer, IPlayer player);
    }
}
