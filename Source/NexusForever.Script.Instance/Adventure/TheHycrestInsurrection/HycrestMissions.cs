namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    /// <summary>
    /// The three mission tracks; each tier offers one mission per track, vote options are always in this order.
    /// </summary>
    public enum HycrestTrack
    {
        Merciful = 0, // Ayita Sinnatus
        Tactical = 1, // Vesna Taranoft
        Militant = 2  // Lysion Sinnatus
    }

    /// <summary>
    /// The missions of The Hycrest Insurrection and how a run moves through them.
    /// </summary>
    /// <remarks>
    /// A run is five missions, one per tier: tier 1 and 2 are voted (45, 46), tier 3 (the interlude) follows from the tracks
    /// chosen before, tier 4 is voted (47, or 48/49 with one option removed) and the finale follows the tier 4 track. Players
    /// regroup at a hideout after tiers 1 to 3.
    /// </remarks>
    public static class HycrestMissions
    {
        public const int TierCount = 5;
        public const int InterludeTier = 2;
        public const int FinaleTier = 4;

        /// <summary>
        /// Mission public event ids by tier (0-4) and track.
        /// </summary>
        private static readonly uint[,] missions =
        {
            { 420u, 421u, 422u }, // The Farmer's Daughter, The Science of Revenge, Leveling the Field
            { 423u, 424u, 425u }, // The Great Escape, The Keymaster, Jailbreak
            { 426u, 427u, 428u }, // Breach of Protocol, The Thresher Initiative, Midnight Counterstrike
            { 429u, 430u, 431u }, // Clearance, Raid Warning, The Underground
            { 432u, 433u, 434u }  // All Aboard, Priority Target, Coup de Grace
        };

        public static uint GetMission(int tier, HycrestTrack track)
        {
            return missions[tier, (int)track];
        }

        public static bool TryGetTierAndTrack(uint missionId, out int tier, out HycrestTrack track)
        {
            for (tier = 0; tier < TierCount; tier++)
            {
                for (int t = 0; t < 3; t++)
                {
                    if (missions[tier, t] != missionId)
                        continue;

                    track = (HycrestTrack)t;
                    return true;
                }
            }

            tier  = -1;
            track = HycrestTrack.Tactical;
            return false;
        }

        public static bool IsMission(uint eventId)
        {
            return TryGetTierAndTrack(eventId, out _, out _);
        }

        /// <summary>
        /// The tier 3 interlude, from the tracks of tier 1 and 2: Merciful counts -1, Tactical 0, Militant +1.
        /// </summary>
        /// <remarks>
        /// Inferred, it fits every route recorded in the guides (e.g. Farmer's Daughter + Keymaster gives Breach of Protocol,
        /// Science of Revenge + Jailbreak gives Midnight Counterstrike, Farmer's Daughter + Jailbreak gives Thresher).
        /// </remarks>
        public static HycrestTrack GetInterludeTrack(HycrestTrack tier1, HycrestTrack tier2)
        {
            int score = Score(tier1) + Score(tier2);
            return score < 0 ? HycrestTrack.Merciful : score > 0 ? HycrestTrack.Militant : HycrestTrack.Tactical;

            static int Score(HycrestTrack track) => (int)track - 1;
        }

        /// <summary>
        /// The mission vote of a tier; tier 4 depends on the interlude: after a Merciful interlude the Militant option is
        /// gone (48), after a Militant one the Merciful option (49).
        /// </summary>
        public static uint GetVote(int tier, HycrestTrack interludeTrack)
        {
            return tier switch
            {
                0 => 45u,
                1 => 46u,
                3 => interludeTrack switch
                {
                    HycrestTrack.Merciful => 48u,
                    HycrestTrack.Militant => 49u,
                    _                     => 47u
                },
                _ => 0u
            };
        }

        /// <summary>
        /// The track of a vote option; votes 48 and 49 have two options.
        /// </summary>
        public static HycrestTrack GetVoteTrack(uint voteId, uint option)
        {
            return voteId switch
            {
                48u => option == 0u ? HycrestTrack.Merciful : HycrestTrack.Tactical, // Clearance, Raid Warning
                49u => option == 0u ? HycrestTrack.Militant : HycrestTrack.Tactical, // The Underground, Raid Warning
                _   => (HycrestTrack)Math.Min(option, 2u)
            };
        }

        /// <summary>
        /// Regroup (public event 445) objective after a mission: the hideout the mission unlocked or ended at. The trigger
        /// object id and WorldLocation2 point come from the objective.
        /// </summary>
        /// <remarks>
        /// Inferred from the missions' endings and payoff texts; the interludes without a known hideout use the Abandoned Barn.
        /// </remarks>
        public static readonly IReadOnlyDictionary<uint, uint> RegroupAfter = new Dictionary<uint, uint>
        {
            [420u] = RegroupSinnatusBarn, // retail video; the payoff text offers the Arcwulff farmhouse
            [421u] = RegroupSinnatusBarn,
            [422u] = RegroupSinnatusBarn,
            [423u] = RegroupAbandonedBarn, // retail video: its last objective returns to the (empty) barn
            [424u] = RegroupAyitasTavern,
            [425u] = RegroupArcwulffFarm,
            [426u] = RegroupArcwulffFarm, // retail video: "Meet us at the nearby safehouse" (Vesna, 461016)
            [427u] = RegroupAbandonedBarn,
            [428u] = RegroupAbandonedBarn,
            [429u] = RegroupBellFarmhouse  // Clearance: "Meet with Ayita Sinnatus in the nearby farmhouse" (objective 2167)
        };

        /// <summary>
        /// Missions whose own last objective already brings the players to their hideout (<see cref="RegroupAfter"/>): no
        /// separate regroup, the barn closes as soon as the mission is done.
        /// </summary>
        public static readonly IReadOnlySet<uint> EndsAtHideout = new HashSet<uint>
        {
            423u, // The Great Escape: "Return to the Barn"
            429u  // Clearance: "Meet with Ayita Sinnatus in the nearby farmhouse"
        };

        public const uint RegroupAbandonedBarn = 1772u;
        public const uint RegroupAyitasTavern  = 1773u;
        public const uint RegroupBellFarmhouse = 1774u;
        public const uint RegroupSinnatusBarn  = 1775u;
        public const uint RegroupArcwulffFarm  = 1776u;
    }
}
