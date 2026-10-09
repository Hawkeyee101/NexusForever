using System.Numerics;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Reputation;
using NexusForever.Shared;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    /// <summary>
    /// "Hide in Crowds" in Hycrest City: out of combat within 10 m of a Hycrest Citizen a player is disguised as a citizen
    /// and the Hycrest Watch leaves them alone; stepping away fades the disguise in 5 s, combat ends it at once.
    /// </summary>
    /// <remarks>
    /// From the tables (research/hycrest-city-population.md): "Hidden in Plain Sight" 60752 is the disguise (Disguise as
    /// citizen 15628 male / 15632 female, ReputationModify Hycrest Watch 759 to 32,000 = Beloved), "Fading Disguise" 60678
    /// the 5 s fade (a visual only), text 474914 "While you are within 10m of a Hycrest Citizen, you will blend in with the
    /// crowd...". Which unit cast them in retail isn't in the tables (the citizens' aura 52963 is the likely source); the
    /// map script checks the distances itself, which needs no aura target updates. Pets aren't dismissed.
    /// </remarks>
    public class HycrestCrowds
    {
        private const uint HiddenInPlainSight = 60752u;
        private const uint FadingDisguise     = 60678u;

        private const float CrowdRange = 10f;
        private static readonly TimeSpan FadeTime = TimeSpan.FromSeconds(5);
        private const double CheckInterval = 0.5d;

        // the guards that would see through nothing but the disguise: an undisguised player in their range when a
        // disguise ends is pulled in (their own aggro only looks when someone comes into range)
        private const float GuardRange = 15f;

        private static readonly HashSet<uint> Citizens =
        [
            17733u, 17734u, 17735u, 17736u, 17737u, 17738u,  // Hycrest Citizen ("Hide in Crowds")
            18131u, 18132u, 18133u, 18134u, 18135u           // Hycrest Citizen (Coup de Grace's crowd)
        ];

        private static readonly HashSet<uint> Guards =
        [
            17929u, // Hycrest Watchman
            17745u, // Shatterforce Trooper
            18465u, // Shatterforce Enforcer
            17804u, // Dominion Commando
            19095u, // Dominion Herald
            52719u  // Shatterforce Vanguard
        ];

        private class CrowdState
        {
            public bool Disguised;
            public double? FadeLeft;
        }

        private readonly IMapInstance map;
        private readonly IFactory<ISpellParameters> spellParametersFactory;

        private readonly HashSet<uint> citizens = [];
        private readonly HashSet<uint> guards = [];
        private readonly Dictionary<uint, CrowdState> states = [];   // player guid
        private double checkTimer;

        public HycrestCrowds(IMapInstance map, IFactory<ISpellParameters> spellParametersFactory)
        {
            this.map                    = map;
            this.spellParametersFactory = spellParametersFactory;
        }

        /// <summary>
        /// Keep track of the citizens and guards as they appear on the map.
        /// </summary>
        public void OnAddToMap(IGridEntity entity)
        {
            if (entity is not IUnitEntity unit || unit is IPlayer)
                return;

            if (Citizens.Contains(unit.CreatureId))
                citizens.Add(unit.Guid);
            else if (Guards.Contains(unit.CreatureId))
                guards.Add(unit.Guid);
        }

        public void Update(double lastTick)
        {
            checkTimer -= lastTick;
            if (checkTimer > 0d)
                return;
            checkTimer = CheckInterval;

            List<IUnitEntity> crowd = Units(citizens);

            foreach (IPlayer player in map.GetPlayers().ToList())
            {
                if (!states.TryGetValue(player.Guid, out CrowdState state))
                {
                    if (crowd.Count == 0)
                        continue;
                    state = new CrowdState();
                    states.Add(player.Guid, state);
                }

                bool inCrowd = player.IsAlive && !player.InCombat
                    && crowd.Any(c => Vector3.Distance(c.Position, player.Position) <= CrowdRange);

                if (inCrowd)
                {
                    if (state.FadeLeft.HasValue)
                    {
                        state.FadeLeft = null;
                        player.GetSpellBySpellId(FadingDisguise)?.Finish();
                    }
                    if (!state.Disguised || player.GetSpellBySpellId(HiddenInPlainSight) == null)
                    {
                        state.Disguised = true;
                        Cast(player, HiddenInPlainSight);
                    }
                    continue;
                }

                if (!state.Disguised)
                    continue;

                // combat or death: gone at once; walking away: it fades
                if (!player.IsAlive || player.InCombat)
                {
                    EndDisguise(player, state);
                    continue;
                }

                if (!state.FadeLeft.HasValue)
                {
                    state.FadeLeft = FadeTime.TotalSeconds;
                    Cast(player, FadingDisguise);
                    continue;
                }

                state.FadeLeft -= CheckInterval;
                if (state.FadeLeft <= 0d)
                    EndDisguise(player, state);
            }

            // players that left the map
            foreach (uint guid in states.Keys.Where(g => map.GetEntity<IPlayer>(g) == null).ToList())
                states.Remove(guid);
        }

        private void EndDisguise(IPlayer player, CrowdState state)
        {
            state.Disguised = false;
            state.FadeLeft  = null;
            player.GetSpellBySpellId(FadingDisguise)?.Finish();
            player.GetSpellBySpellId(HiddenInPlainSight)?.Finish();

            if (!player.IsAlive)
                return;

            foreach (IUnitEntity guard in Units(guards))
            {
                if (!guard.IsAlive || guard.InCombat || Vector3.Distance(guard.Position, player.Position) > GuardRange)
                    continue;
                if (player.GetDispositionTo(guard.Faction1) != Disposition.Hostile)
                    continue;

                guard.ThreatManager.UpdateThreat(player, 1);
            }
        }

        private void Cast(IPlayer player, uint spell4Id)
        {
            ISpellParameters parameters = spellParametersFactory.Resolve();
            parameters.PrimaryTargetId        = player.Guid;
            parameters.UserInitiatedSpellCast = false;
            player.CastSpell(spell4Id, parameters);
        }

        private List<IUnitEntity> Units(HashSet<uint> guids)
        {
            List<IUnitEntity> units = [];
            foreach (uint guid in guids.ToList())
            {
                IUnitEntity unit = map.GetEntity<IUnitEntity>(guid);
                if (unit is { InWorld: true })
                    units.Add(unit);
                else
                    guids.Remove(guid);
            }
            return units;
        }
    }
}
