using NexusForever.Database.Character;
using NexusForever.Game.Static.Reputation;

namespace NexusForever.Game.Abstract.Reputation
{
    public interface IReputationManager : IDatabaseCharacter, IEnumerable<IReputation>
    {
        /// <summary>
        /// Update <see cref="IReputation"/> for supplied <see cref="Faction"/> and value.
        /// </summary>
        /// <remarks>
        /// Value is a delta, if a negative value is supplied it will be deducted from the existing reputation if any.
        /// </remarks>
        void UpdateReputation(Faction factionId, float value);

        /// <summary>
        /// Get <see cref="IReputation"/> for supplied <see cref="Faction"/>.
        /// </summary>
        IReputation GetReputation(Faction factionId);

        /// <summary>
        /// Temporarily treat the reputation with <see cref="Faction"/> as <paramref name="value"/> (e.g. a disguise), without
        /// changing or saving the real reputation.
        /// </summary>
        void AddOverride(Faction factionId, float value);

        /// <summary>
        /// Remove the temporary reputation with <see cref="Faction"/>, see <see cref="AddOverride"/>.
        /// </summary>
        void RemoveOverride(Faction factionId);

        /// <summary>
        /// The temporary reputation with <see cref="Faction"/>, if one is set (see <see cref="AddOverride"/>).
        /// </summary>
        float? GetOverride(Faction factionId);
    }
}