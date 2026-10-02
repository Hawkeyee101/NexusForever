using NexusForever.Game.Abstract.Entity;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// What the Securitybots of Clearance (429) pass on to the mission script.
    /// </summary>
    public interface IClearanceMissionScript
    {
        /// <summary>
        /// <paramref name="player"/> hacked the dormant Securitybot <paramref name="bot"/>.
        /// </summary>
        void OnSecuritybotHacked(IWorldEntity bot, IPlayer player);

        /// <summary>
        /// The pilot of the Securitybot vehicle <paramref name="vehicle"/> pressed a button of its bar.
        /// </summary>
        void OnSecuritybotAbility(IVehicleEntity vehicle, IPlayer pilot, ushort index, bool pressed);

        /// <summary>
        /// The Securitybot vehicle <paramref name="vehicle"/> left the map (its pilot got out, it was destroyed).
        /// </summary>
        void OnSecuritybotGone(IVehicleEntity vehicle);
    }
}
