using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Static.Entity;

namespace NexusForever.Game.Entity.Creature
{
    /// <summary>
    /// Creature → spell rows kept in code until there is a database table for them (local, Hycrest enemies first).
    /// </summary>
    /// <remarks>
    /// Spells and creatures from the tables and Jabbithole's ability lists (docs research/enemy-spells-plan.md and
    /// enemy-spells-list.md). The timing isn't in the client tables (Spell4 cooldowns are almost always 0): every "first"
    /// and "every" below is a guess from enemy-spells-list.md ("Guess: first use / every"), to tune after Teun's video
    /// check. Each use is spread by <see cref="FirstJitter"/> / <see cref="EveryJitter"/> so a group doesn't fire in step.
    /// </remarks>
    public class LocalCreatureSpellSource : ICreatureSpellSource
    {
        // spread around the guessed times, in seconds (a guess too)
        private const double FirstJitter = 1d;
        private const double EveryJitter = 2d;

        private static ICreatureSpell AutoAttack(uint creatureId, uint spell4Id, string comment)
        {
            return new CreatureSpell
            {
                CreatureId = creatureId,
                Spell4Id   = spell4Id,
                Flags      = CreatureSpellFlags.AutoAttack,
                Comment    = comment
            };
        }

        /// <summary>
        /// A special: first use <paramref name="first"/> seconds after the pull, then every <paramref name="every"/> seconds,
        /// when the target is within <paramref name="range"/> metres (0: the spell's own range).
        /// </summary>
        private static ICreatureSpell Special(uint creatureId, uint spell4Id, double first, double every, float range,
            CreatureSpellTarget target, byte priority, string comment, float minRange = 0f,
            CreatureSpellFlags flags = CreatureSpellFlags.None, uint tip = 0u)
        {
            return new CreatureSpell
            {
                CreatureId      = creatureId,
                Spell4Id        = spell4Id,
                Flags           = flags,
                TipTextId       = tip,
                InitialDelayMin = Milliseconds(first - FirstJitter),
                InitialDelayMax = Milliseconds(first + FirstJitter),
                IntervalMin     = Milliseconds(every - EveryJitter),
                IntervalMax     = Milliseconds(every + EveryJitter),
                MinRange        = minRange,
                MaxRange        = range,
                Target          = target,
                Priority        = priority,
                Comment         = comment
            };
        }

        /// <summary>
        /// A spell the map's scripts handle instead of a cast, once per fight, <paramref name="first"/> seconds after the
        /// pull, when the target is within <paramref name="range"/> metres.
        /// </summary>
        private static ICreatureSpell ScriptHandledOnce(uint creatureId, uint spell4Id, double first, float range, byte priority,
            string comment)
        {
            return new CreatureSpell
            {
                CreatureId      = creatureId,
                Spell4Id        = spell4Id,
                Flags           = CreatureSpellFlags.ScriptHandled,
                // first 0: at once on aggro, before any auto attack (no spread)
                InitialDelayMin = first <= 0d ? 0u : Milliseconds(first - FirstJitter),
                InitialDelayMax = first <= 0d ? 0u : Milliseconds(first + FirstJitter),
                MaxRange        = range,
                Target          = CreatureSpellTarget.Victim,
                Priority        = priority,
                Comment         = comment
            };
        }

        private static uint Milliseconds(double seconds)
        {
            return (uint)Math.Max(0d, seconds * 1000d);
        }

        // Hycrest enemies (scripted ones stay out: the spotlights, Groo's ability rotation (only his auto attack is here),
        // All Aboard's roof snipers 26360 and the train's Suppression Specialists 17742)
        private const uint DominionScout              = 17856u;
        private const uint DominionSoldier            = 17857u;
        private const uint DominionEngineeringOfficer = 17949u;
        private const uint DominionShocktrooper       = 17858u;
        private const uint DominionScientist          = 17824u;
        private const uint DominionScientist50465     = 50465u;
        private const uint DominionScientist50466     = 50466u;
        private const uint ReconSpecialist            = 17823u;
        private const uint PredatorDrone              = 51026u;
        private const uint RapidResponseDrone         = 50834u;
        private const uint RapidResponseCommando      = 50378u;
        private const uint DominionGatekeeper         = 26853u;
        private const uint ShatterforceResponsebot    = 18509u;
        private const uint DominionAmbusher           = 17954u;
        private const uint DominionTechsmith          = 17859u;
        private const uint FieldIntelligenceAgent     = 18445u;
        private const uint RecoveryAgentWaveOne       = 19115u;
        private const uint RecoveryLeaderWaveOne      = 19130u;
        private const uint RecoveryAgentWaveTwo       = 50676u;
        private const uint RecoveryLeaderWaveTwo      = 50677u;
        private const uint HycrestWatchman            = 17929u;
        private const uint ShatterforceTrooper        = 17745u;
        private const uint ShatterforceEnforcer       = 18465u;
        private const uint DominionCommando           = 17804u;
        private const uint DominionRocketeer          = 48255u;
        private const uint DominionGuardbot           = 49118u;
        private const uint ArsenaxAmbush              = 48373u;
        private const uint SuppressionbotMk2          = 50758u;
        private const uint TwitchyGroo                = 23503u;

        private const uint PlasmaShot      = 65717u;
        private const uint ShellStorm      = 66281u;
        private const uint Longshot        = 66282u;
        private const uint MortarBarrage   = 66283u;
        private const uint CommandoMortar  = 69344u;   // the Commando's Mortar Barrage (range 60, telegraph 7107)
        private const uint Snipe           = 27324u;
        private const uint CallReinforcements = 48499u;
        private const uint Slice           = 65715u;
        private const uint Dice            = 65716u;
        private const uint SlashingStrikes = 66353u;
        private const uint FeetOfFury      = 66354u;
        private const uint Flametorch      = 66020u;
        private const uint NapalmGrenade   = 66273u;
        private const uint Blast           = 65794u;
        private const uint Shot            = 65795u;
        private const uint PrecisionBlast  = 78165u;
        private const uint WildBarrage     = 66359u;
        private const uint Bite            = 65945u;
        private const uint Rip             = 65946u;
        private const uint EnergyLeap      = 66244u;
        private const uint GatekeeperSlice = 65950u;
        private const uint GatekeeperSlash = 65951u;
        private const uint TripleThreat    = 66275u;
        private const uint DemolitionShout = 66279u;
        private const uint Smash           = 65779u;
        private const uint Swipe           = 65780u;
        private const uint Eradicate       = 66395u;
        private const uint Exterminate     = 66397u;
        private const uint RockEm          = 66398u;
        private const uint Clobber         = 65952u;
        private const uint HammerSmash     = 65953u;
        private const uint CrushingFlurry  = 66259u;
        private const uint ElectricShot    = 65735u;
        private const uint ChainGun        = 66148u;
        private const uint AttackSequence  = 66352u;
        private const uint Onslaught       = 66105u;

        private static readonly ICreatureSpell[] spells =
        [
            // Dominion Scout: rifle, Shell Storm (cone 17 m / 34°), Longshot (line of boxes, 20 m), Mortar Barrage (ground
            // circles at the target, 30 m), Snipe (2.25 s aim, 6-60 m, knockdown; table cooldown 4 s)
            AutoAttack(DominionScout, PlasmaShot, "Plasma Shot"),
            Special(DominionScout, ShellStorm, first: 6d, every: 12d, range: 15f, CreatureSpellTarget.Victim, 1, "Shell Storm"),
            Special(DominionScout, Longshot, first: 10d, every: 18d, range: 20f, CreatureSpellTarget.Victim, 0, "Longshot"),
            Special(DominionScout, MortarBarrage, first: 8d, every: 20d, range: 30f, CreatureSpellTarget.VictimPosition, 0, "Mortar Barrage"),
            Special(DominionScout, Snipe, first: 1d, every: 20d, range: 60f, CreatureSpellTarget.Victim, 0, "Snipe (opens the fight; a dash interrupts it: Teun, 5 Oct 2026)", minRange: 6f,
                flags: CreatureSpellFlags.InterruptOnTargetDash, tip: 249356u), // "Dash to interrupt a sniper's aim!"

            // Dominion Soldier: rifle, Shell Storm, Mortar Barrage
            AutoAttack(DominionSoldier, PlasmaShot, "Plasma Shot"),
            Special(DominionSoldier, ShellStorm, first: 6d, every: 12d, range: 15f, CreatureSpellTarget.Victim, 1, "Shell Storm"),
            Special(DominionSoldier, MortarBarrage, first: 8d, every: 20d, range: 30f, CreatureSpellTarget.VictimPosition, 0, "Mortar Barrage"),

            // Dominion Engineering Officer (Leveling the Field): as the Soldier
            AutoAttack(DominionEngineeringOfficer, PlasmaShot, "Plasma Shot"),
            Special(DominionEngineeringOfficer, ShellStorm, first: 6d, every: 12d, range: 15f, CreatureSpellTarget.Victim, 1, "Shell Storm"),
            Special(DominionEngineeringOfficer, MortarBarrage, first: 8d, every: 20d, range: 30f, CreatureSpellTarget.VictimPosition, 0, "Mortar Barrage"),

            // Dominion Shocktrooper: claws, Slashing Strikes (two cones 4.5 m), Feet of Fury (kicking line 17 m)
            AutoAttack(DominionShocktrooper, Slice, "Slice"),
            AutoAttack(DominionShocktrooper, Dice, "Dice"),
            Special(DominionShocktrooper, SlashingStrikes, first: 5d, every: 10d, range: 5f, CreatureSpellTarget.Victim, 1, "Slashing Strikes"),
            Special(DominionShocktrooper, FeetOfFury, first: 8d, every: 15d, range: 15f, CreatureSpellTarget.Victim, 0, "Feet of Fury"),

            // Dominion Scientists: Flametorch (cone 11 m, sets on fire: Burning), Napalm Grenade (fire circles at the target)
            AutoAttack(DominionScientist, PlasmaShot, "Plasma Shot"),
            Special(DominionScientist, Flametorch, first: 5d, every: 15d, range: 6f, CreatureSpellTarget.Victim, 1, "Flametorch"),
            Special(DominionScientist, NapalmGrenade, first: 10d, every: 20d, range: 20f, CreatureSpellTarget.VictimPosition, 0, "Napalm Grenade"),
            AutoAttack(DominionScientist50465, PlasmaShot, "Plasma Shot"),
            Special(DominionScientist50465, Flametorch, first: 5d, every: 15d, range: 6f, CreatureSpellTarget.Victim, 1, "Flametorch"),
            Special(DominionScientist50465, NapalmGrenade, first: 10d, every: 20d, range: 20f, CreatureSpellTarget.VictimPosition, 0, "Napalm Grenade"),
            AutoAttack(DominionScientist50466, PlasmaShot, "Plasma Shot"),
            Special(DominionScientist50466, Flametorch, first: 5d, every: 15d, range: 6f, CreatureSpellTarget.Victim, 1, "Flametorch"),
            Special(DominionScientist50466, NapalmGrenade, first: 10d, every: 20d, range: 20f, CreatureSpellTarget.VictimPosition, 0, "Napalm Grenade"),

            // Dominion Recon Specialist: pistols, Precision Blast (narrow cone 26 m), Wild Barrage (circles at the target),
            // Call Reinforcements (the flare): script handled, the Hycrest missions route it to their alarm (the engine
            // can't summon the flare, drill and Rapid Response Team); once per fight
            AutoAttack(ReconSpecialist, Blast, "Blast"),
            AutoAttack(ReconSpecialist, Shot, "Shot"),
            ScriptHandledOnce(ReconSpecialist, CallReinforcements, first: 0d, range: 40f, 0, "Call Reinforcements (the alarm, at once on aggro, before any attack: Teun, 7 Oct 2026)"),
            Special(ReconSpecialist, PrecisionBlast, first: 6d, every: 12d, range: 26f, CreatureSpellTarget.Victim, 1, "Precision Blast"),
            Special(ReconSpecialist, WildBarrage, first: 10d, every: 15d, range: 25f, CreatureSpellTarget.VictimPosition, 0, "Wild Barrage"),

            // Predator Drone, Rapid Response Drone: bites, Energy Leap (leaps onto the target's spot)
            AutoAttack(PredatorDrone, Bite, "Bite"),
            AutoAttack(PredatorDrone, Rip, "Rip"),
            Special(PredatorDrone, EnergyLeap, first: 6d, every: 15d, range: 20f, CreatureSpellTarget.VictimPosition, 0, "Energy Leap"),
            AutoAttack(RapidResponseDrone, Bite, "Bite"),
            AutoAttack(RapidResponseDrone, Rip, "Rip"),
            Special(RapidResponseDrone, EnergyLeap, first: 6d, every: 15d, range: 20f, CreatureSpellTarget.VictimPosition, 0, "Energy Leap"),

            // Rapid Response Commando (the alarm's team): rifle, Flametorch
            AutoAttack(RapidResponseCommando, PlasmaShot, "Plasma Shot"),
            Special(RapidResponseCommando, Flametorch, first: 5d, every: 15d, range: 6f, CreatureSpellTarget.Victim, 0, "Flametorch"),

            // Dominion Gatekeeper: 2H sword, Triple Threat (three sweeps, cones 5 m), Demolition Shout (around himself, armor
            // -20 % for 15 s)
            AutoAttack(DominionGatekeeper, GatekeeperSlice, "Slice"),
            AutoAttack(DominionGatekeeper, GatekeeperSlash, "Slash"),
            Special(DominionGatekeeper, TripleThreat, first: 6d, every: 12d, range: 5f, CreatureSpellTarget.Victim, 1, "Triple Threat"),
            Special(DominionGatekeeper, DemolitionShout, first: 10d, every: 20d, range: 6f, CreatureSpellTarget.Victim, 0, "Demolition Shout"),

            // Shatterforce Responsebot: Smash/Swipe, Eradicate (eye beam cone 15 m), Exterminate (circles at the target),
            // Rock 'Em (hit + disorient)
            AutoAttack(ShatterforceResponsebot, Smash, "Smash"),
            AutoAttack(ShatterforceResponsebot, Swipe, "Swipe"),
            Special(ShatterforceResponsebot, Eradicate, first: 5d, every: 15d, range: 15f, CreatureSpellTarget.Victim, 2, "Eradicate"),
            Special(ShatterforceResponsebot, Exterminate, first: 12d, every: 25d, range: 15f, CreatureSpellTarget.VictimPosition, 0, "Exterminate"),
            Special(ShatterforceResponsebot, RockEm, first: 8d, every: 20d, range: 15f, CreatureSpellTarget.Victim, 1, "Rock 'Em"),

            // Dominion Ambusher (Breach of Protocol's ambush): Jabbithole lists Shell Storm and Mortar Barrage, so a rifle
            // like the Soldier (Plasma Shot is a guess)
            AutoAttack(DominionAmbusher, PlasmaShot, "Plasma Shot"),
            Special(DominionAmbusher, ShellStorm, first: 6d, every: 12d, range: 15f, CreatureSpellTarget.Victim, 1, "Shell Storm"),
            Special(DominionAmbusher, MortarBarrage, first: 8d, every: 20d, range: 30f, CreatureSpellTarget.VictimPosition, 0, "Mortar Barrage"),

            // Dominion Techsmith (Jailbreak): 2H hammer, Clobber/Smash, Crushing Flurry (3.5 s, several hits, 10 m)
            AutoAttack(DominionTechsmith, Clobber, "Clobber"),
            AutoAttack(DominionTechsmith, HammerSmash, "Smash"),
            Special(DominionTechsmith, CrushingFlurry, first: 6d, every: 14d, range: 10f, CreatureSpellTarget.Victim, 0, "Crushing Flurry"),

            // Field Intelligence Agent (The Keymaster): 2H sword like the Gatekeeper (Jabbithole)
            AutoAttack(FieldIntelligenceAgent, GatekeeperSlice, "Slice"),
            AutoAttack(FieldIntelligenceAgent, GatekeeperSlash, "Slash"),
            Special(FieldIntelligenceAgent, TripleThreat, first: 6d, every: 12d, range: 5f, CreatureSpellTarget.Victim, 1, "Triple Threat"),
            Special(FieldIntelligenceAgent, DemolitionShout, first: 10d, every: 20d, range: 6f, CreatureSpellTarget.Victim, 0, "Demolition Shout"),

            // Intelligence Recovery Agents (The Keymaster's waves): Jabbithole merges all four ids under one name with a
            // sword kit and a pistol kit; which id has which is a guess: the leaders swords, the agents pistols
            AutoAttack(RecoveryLeaderWaveOne, GatekeeperSlice, "Slice"),
            AutoAttack(RecoveryLeaderWaveOne, GatekeeperSlash, "Slash"),
            Special(RecoveryLeaderWaveOne, TripleThreat, first: 6d, every: 12d, range: 5f, CreatureSpellTarget.Victim, 1, "Triple Threat"),
            Special(RecoveryLeaderWaveOne, DemolitionShout, first: 10d, every: 20d, range: 6f, CreatureSpellTarget.Victim, 0, "Demolition Shout"),
            AutoAttack(RecoveryLeaderWaveTwo, GatekeeperSlice, "Slice"),
            AutoAttack(RecoveryLeaderWaveTwo, GatekeeperSlash, "Slash"),
            Special(RecoveryLeaderWaveTwo, TripleThreat, first: 6d, every: 12d, range: 5f, CreatureSpellTarget.Victim, 1, "Triple Threat"),
            Special(RecoveryLeaderWaveTwo, DemolitionShout, first: 10d, every: 20d, range: 6f, CreatureSpellTarget.Victim, 0, "Demolition Shout"),
            AutoAttack(RecoveryAgentWaveOne, Blast, "Blast"),
            AutoAttack(RecoveryAgentWaveOne, Shot, "Shot"),
            Special(RecoveryAgentWaveOne, PrecisionBlast, first: 6d, every: 12d, range: 26f, CreatureSpellTarget.Victim, 1, "Precision Blast"),
            Special(RecoveryAgentWaveOne, WildBarrage, first: 10d, every: 15d, range: 25f, CreatureSpellTarget.VictimPosition, 0, "Wild Barrage"),
            AutoAttack(RecoveryAgentWaveTwo, Blast, "Blast"),
            AutoAttack(RecoveryAgentWaveTwo, Shot, "Shot"),
            Special(RecoveryAgentWaveTwo, PrecisionBlast, first: 6d, every: 12d, range: 26f, CreatureSpellTarget.Victim, 1, "Precision Blast"),
            Special(RecoveryAgentWaveTwo, WildBarrage, first: 10d, every: 15d, range: 25f, CreatureSpellTarget.VictimPosition, 0, "Wild Barrage"),

            // Hycrest City's watch (Jabbithole kits; timings like the agents', not measured)
            // Hycrest Watchman: pistols, Precision Blast, Wild Barrage
            AutoAttack(HycrestWatchman, Blast, "Blast"),
            AutoAttack(HycrestWatchman, Shot, "Shot"),
            Special(HycrestWatchman, PrecisionBlast, first: 6d, every: 12d, range: 26f, CreatureSpellTarget.Victim, 1, "Precision Blast"),
            Special(HycrestWatchman, WildBarrage, first: 10d, every: 15d, range: 25f, CreatureSpellTarget.VictimPosition, 0, "Wild Barrage"),
            // Shatterforce Trooper and Enforcer (Elite Enforcer / Officer pages): Slice, Slash, Triple Threat, Demolition Shout
            AutoAttack(ShatterforceTrooper, GatekeeperSlice, "Slice"),
            AutoAttack(ShatterforceTrooper, GatekeeperSlash, "Slash"),
            Special(ShatterforceTrooper, TripleThreat, first: 6d, every: 12d, range: 5f, CreatureSpellTarget.Victim, 1, "Triple Threat"),
            Special(ShatterforceTrooper, DemolitionShout, first: 10d, every: 20d, range: 6f, CreatureSpellTarget.Victim, 0, "Demolition Shout"),
            AutoAttack(ShatterforceEnforcer, GatekeeperSlice, "Slice"),
            AutoAttack(ShatterforceEnforcer, GatekeeperSlash, "Slash"),
            Special(ShatterforceEnforcer, TripleThreat, first: 6d, every: 12d, range: 5f, CreatureSpellTarget.Victim, 1, "Triple Threat"),
            Special(ShatterforceEnforcer, DemolitionShout, first: 10d, every: 20d, range: 6f, CreatureSpellTarget.Victim, 0, "Demolition Shout"),
            // Dominion Commando: Plasma Shot, Shell Storm, Mortar Barrage 69344
            AutoAttack(DominionCommando, PlasmaShot, "Plasma Shot"),
            Special(DominionCommando, ShellStorm, first: 6d, every: 12d, range: 15f, CreatureSpellTarget.Victim, 1, "Shell Storm"),
            Special(DominionCommando, CommandoMortar, first: 8d, every: 20d, range: 30f, CreatureSpellTarget.VictimPosition, 0, "Mortar Barrage"),

            // Dominion Rocketeer (Jailbreak's drop-ins on the blimp): pistols, Precision Blast, Wild Barrage (Jabbithole)
            AutoAttack(DominionRocketeer, Blast, "Blast"),
            AutoAttack(DominionRocketeer, Shot, "Shot"),
            Special(DominionRocketeer, PrecisionBlast, first: 6d, every: 12d, range: 26f, CreatureSpellTarget.Victim, 1, "Precision Blast"),
            Special(DominionRocketeer, WildBarrage, first: 10d, every: 15d, range: 25f, CreatureSpellTarget.VictimPosition, 0, "Wild Barrage"),

            // Dominion Guardbot (All Aboard's waves): Electric Shot, Chain Gun (cone 15 m, 1.75 s), Default Attack Sequence
            // (3.5 s, several hits, 12 m)
            AutoAttack(DominionGuardbot, ElectricShot, "Electric Shot"),
            Special(DominionGuardbot, ChainGun, first: 5d, every: 12d, range: 15f, CreatureSpellTarget.Victim, 1, "Chain Gun"),
            Special(DominionGuardbot, AttackSequence, first: 8d, every: 16d, range: 12f, CreatureSpellTarget.Victim, 0, "Default Attack Sequence"),

            // Arsenax Severus in Breach of Protocol's ambush: 2H sword, Onslaught (half circle in front, 6 m, several hits);
            // his health floor and leaving stay in ArsenaxAmbushEntityScript / the mission script
            AutoAttack(ArsenaxAmbush, GatekeeperSlice, "Slice"),
            AutoAttack(ArsenaxAmbush, GatekeeperSlash, "Slash"),
            Special(ArsenaxAmbush, Onslaught, first: 5d, every: 10d, range: 6f, CreatureSpellTarget.Victim, 0, "Onslaught"),

            // Suppressionbot Mk. II (All Aboard's last wave): the Responsebot's kit (Jabbithole; Carpet Bombing and the second
            // Eradicate/Exterminate are their proxies), a little faster as the boss (a guess); the orbital barrages stay
            // scripted in the mission
            AutoAttack(SuppressionbotMk2, Smash, "Smash"),
            AutoAttack(SuppressionbotMk2, Swipe, "Swipe"),
            Special(SuppressionbotMk2, Eradicate, first: 5d, every: 12d, range: 15f, CreatureSpellTarget.Victim, 2, "Eradicate"),
            Special(SuppressionbotMk2, Exterminate, first: 10d, every: 20d, range: 15f, CreatureSpellTarget.VictimPosition, 0, "Exterminate"),
            Special(SuppressionbotMk2, RockEm, first: 8d, every: 16d, range: 15f, CreatureSpellTarget.Victim, 1, "Rock 'Em"),

            // "Twitchy" Groo: only his rifle auto attack (Jabbithole); his abilities stay in The Science of Revenge's rotation
            AutoAttack(TwitchyGroo, PlasmaShot, "Plasma Shot"),
        ];

        public IEnumerable<ICreatureSpell> GetCreatureSpells()
        {
            return spells;
        }
    }
}
