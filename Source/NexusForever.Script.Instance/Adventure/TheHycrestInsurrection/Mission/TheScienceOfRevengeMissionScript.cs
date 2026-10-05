using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Entity.Movement.Command.Mode;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.GameTable;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.PublicEvent;
using NexusForever.Script.Template.Filter;
using NexusForever.Shared;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection.Mission
{
    /// <summary>
    /// The Science of Revenge (public event 421, tier 1 Tactical): kill "Twitchy" Groo's assistants in the Sunripe Fields
    /// until one of them gives him up, then kill Groo near Sinnatus's Barn.
    /// </summary>
    /// <remarks>
    /// Research and sources: docs research/science-of-revenge.md. Three assistant sites (WorldLocation2 13151, 13114, 13152;
    /// Jabbithole scientist and farmer sightings), one Dominion Scientist working on a Cornered Farmer at each. Objective 202
    /// (count 3) is a script count. Groo (phase 1) appears when it completes; 1947 (Exterminate, TG 6715) picks him up on
    /// its own. Teun's retail video check (3 Oct 2026): the tracker counts x/3 scientists, then "1 Remaining" for Groo; the
    /// markers are on the scientists, then on Groo; the scientists and farmers talk (bubbles); a Recon Specialist stands
    /// near the first spotlight and calls reinforcements, there is no alarm every few kills; Vesna's communicators come in
    /// the order 444087, 448087, 161849, 461028; the regroup is Sinnatus's Barn as after The Farmer's Daughter. Inferred:
    /// the farmers cowering, the last assistant giving Groo up at low health and running off, and Groo's ability rhythm.
    /// </remarks>
    [ScriptFilterOwnerId(421u)]
    public class TheScienceOfRevengeMissionScript : HycrestFieldMissionScript
    {
        private const uint KillAssistants = 202u;
        private const uint KillGroo       = 1947u;

        private const uint TwitchyGroo     = 23503u;
        private const uint ScientistMale   = 50465u;
        private const uint ScientistFemale = 50466u;
        private const uint FarmerMale      = 50473u;
        private const uint FarmerFemale    = 50476u;
        private const uint VesnaTaranoft   = 17778u;
        private const uint ReconSpecialist = 17823u;

        private const uint GrooPhase = 1u;

        // the assistant sites, then Groo's spot (Sinnatus Farmstead). Retail marks the scientists and Groo themselves
        // (Teun's video): tracked units on the map (ServerMapTrackedUnitUpdate) for the objective, updated as they move.
        // The site points stay as the objective's area
        private static readonly uint[] AssistantLocations = [13151u, 13114u, 13152u];
        private static readonly Vector3[] AssistantSites =
        [
            new(-2379.4f, -918.3f, -1389.7f),
            new(-2447.6f, -922.7f, -1394.8f),
            new(-2385.5f, -922.5f, -1302.0f)
        ];
        private readonly List<int> sitesLeft = [0, 1, 2];
        private const uint GrooLocation = 40368u;
        private const double TrackedUnitInterval = 1d;

        private const uint VesnaSecondSetup = 448087u; // communicator: "We believe the Chua scientist, "Twitchy" Groo..."
        private const uint ScientistGivesUp = 461989u; // "I'm just doing my job. It's "Twitchy" Groo you want!..."
        private const uint VesnaPayoff      = 444048u; // communicator: "Always feels good to settle a score..."
        private const uint VesnaSpotlights  = 161849u; // communicator: "Watch out for the sharpshooters nested in that watchtower..."

        // the Recon Specialist: Vesna warns about the sharpshooters when he opens fire (Teun's videos: her third pop-up, also
        // in Leveling the Field "after a snipe"), he calls reinforcements a moment later (her fourth)
        private static readonly TimeSpan SpecialistCallDelay = TimeSpan.FromSeconds(4);

        // the first setup pop-up (444087) comes with the vote outcome (main event script); the second followed it in both
        // retail videos (6 and 15 s later)
        private static readonly TimeSpan SecondSetupDelay = TimeSpan.FromSeconds(10);

        // Cornered Farmers' pleas (no table link, inferred), barked while a player is near and their scientist lives
        private static readonly uint[] FarmerPleas = [456821u, 456822u, 456823u, 456824u, 456825u, 456826u, 456827u, 456828u, 456829u, 456830u];
        // the scientist and his farmer talk while a player is this close, in a fight too (Teun's video: the scientist's line
        // came with his health bar up); the first exchange right when a player comes into range
        private const float FarmerCallRange = 25f;
        private const double FarmerPleaInterval = 8d;

        // the scientist at work (Teun's video: bubbles from both): he says one of these, his farmer answers with a plea
        private static readonly uint[] ScientistLines = [462355u, 462356u, 462357u, 462358u]; // "Don't be afraid, you are sacrificing yourself..." / "Your cadaver is of more importance..." / ...
        private static readonly TimeSpan FarmerAnswerDelay = TimeSpan.FromSeconds(3);

        // the farmers cower (emote 239, "cower", looping with stand state Emote); resent so players arriving later see it
        private const uint CowerEmote = 239u;
        private const double CowerResendInterval = 10d;

        // a scientist's farmer: the nearest one at its site
        private const float SiteRadius = 8f;

        // freed farmers run off, away from where their scientist fell, and go
        private const float FarmerRunDistance = 20f;
        private const float FarmerRunSpeed = 8f;

        // the last assistant gives Groo up instead of dying: at this much health he stops fighting, says where Groo is and
        // runs off (inferred: "until one of them gives him up", and a dead scientist can't talk)
        private const float GiveUpHealth = 0.3f;
        private static readonly TimeSpan GiveUpCountDelay = TimeSpan.FromSeconds(4);
        private const float GiveUpRunDistance = 30f;
        private const float GiveUpRunSpeed = 8f;

        // Groo's abilities (Jabbithole; spells in research/science-of-revenge.md), one every few seconds in combat. Shell
        // Storm is a cone at 3-17 m, Longshot a line to 20 m, so the close one is used when the target is near
        private const uint MortarBarrage       = 66283u;
        private const uint ShellStorm          = 66281u;
        private const uint Longshot            = 66282u;
        private const uint ElectricFlechettes  = 30216u;
        private const uint ShockingDoT         = 30219u;
        private const float ShellStormRange    = 15f;
        private static readonly TimeSpan FirstAbility    = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan AbilityInterval = TimeSpan.FromSeconds(7);
        private enum GrooAbility { Mortar, Close, Flechettes }
        private static readonly GrooAbility[] GrooRotation = [GrooAbility.Mortar, GrooAbility.Close, GrooAbility.Flechettes, GrooAbility.Close];

        // Electric Flechettes' slow lands through RavelSignal, which the engine doesn't handle: the script puts Shocking
        // DoT on the players in its cone (20 m, 60 degrees) once the 1.5 s cast and its 0.3 s delay are over
        private static readonly TimeSpan FlechettesHit = TimeSpan.FromSeconds(1.8);
        private const float FlechettesRange = 20f;
        private const float FlechettesHalfAngle = MathF.PI / 6f;

        private const uint HostileFaction = 1452u;

        private static readonly TimeSpan MissionEndDelay = TimeSpan.FromSeconds(4);

        private class Farmer
        {
            public IWorldEntity Entity;
            public bool Cornered = true;
            public double NextPlea;
            public bool PlayerNear;
        }

        // tracked by reference: an entity's guid is only assigned once it is actually on the map
        private readonly List<IUnitEntity> scientists = [];
        private readonly List<Farmer> farmers = [];
        private IUnitEntity groo;
        private IUnitEntity specialist;
        private bool specialistCalled;
        private readonly Dictionary<uint, uint> tracked = [];   // guid -> objective, the units marked on the map
        private double trackedTimer;

        private IUnitEntity givingUp;
        private int assistantsDown;
        private bool grooRevealed;
        private bool grooInCombat;
        private double grooAbilityTimer;
        private int grooRotationIndex;
        private double cowerResend;
        private bool ending;

        private readonly TimedActionQueue actionQueue = new();
        private HycrestAlarm alarm;

        #region Dependency Injection

        private readonly ILogger<TheScienceOfRevengeMissionScript> log;
        private readonly IStoryBuilder storyBuilder;
        private readonly IFactory<ISpellParameters> spellParametersFactory;
        private readonly ICreatureInfoManager creatureInfoManager;
        private readonly HycrestDialogue dialogue;

        public TheScienceOfRevengeMissionScript(
            ILogger<TheScienceOfRevengeMissionScript> log,
            IGameTableManager gameTableManager,
            IStoryBuilder storyBuilder,
            IFactory<ISpellParameters> spellParametersFactory,
            ICreatureInfoManager creatureInfoManager)
            : base(log, gameTableManager, spellParametersFactory)
        {
            this.log                    = log;
            this.storyBuilder           = storyBuilder;
            this.spellParametersFactory = spellParametersFactory;
            this.creatureInfoManager    = creatureInfoManager;
            dialogue = new HycrestDialogue(gameTableManager, actionQueue);
        }

        #endregion

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public override void OnLoad(IPublicEvent owner)
        {
            base.OnLoad(owner);

            publicEvent.SetObjectiveLocations(KillAssistants, AssistantLocations);
            alarm = new HycrestAlarm(log, publicEvent, mapInstance, creatureInfoManager, spellParametersFactory,
                storyBuilder, dialogue);

            actionQueue.Enqueue(SecondSetupDelay, () => Communicator(VesnaSecondSetup, VesnaTaranoft));
            log.LogInformation("Hycrest: The Science of Revenge started.");
        }

        /// <summary>
        /// Invoked when a <see cref="IGridEntity"/> is added to the map the public event is on.
        /// </summary>
        public override void OnAddToMap(IGridEntity entity)
        {
            if (entity is not IWorldEntity worldEntity || !IsOwnEntity(entity))
                return;

            if (worldEntity is IUnitEntity { InCombat: false } && worldEntity.CreatureId != SpotlightTarget)
                worldEntity.Sheathed = true;

            if (OnAddFieldUnit(worldEntity))
                return;

            switch (worldEntity.CreatureId)
            {
                case ReconSpecialist:
                    if (worldEntity is IUnitEntity recon && specialist == null)
                        specialist = recon;
                    break;
                case ScientistMale:
                case ScientistFemale:
                    if (worldEntity is IUnitEntity scientist)
                        scientists.Add(scientist);
                    break;
                case FarmerMale:
                case FarmerFemale:
                    farmers.Add(new Farmer { Entity = worldEntity, NextPlea = Random.Shared.NextDouble() * FarmerPleaInterval });
                    cowerResend = 0d;
                    break;
                case TwitchyGroo:
                    if (worldEntity is IUnitEntity unit)
                        groo = unit;
                    break;
            }
        }

        /// <summary>
        /// Invoked when a creature's AI picks a script-handled spell: a Recon Specialist's Call Reinforcements from the
        /// creature spell list goes to this mission's alarm (once per specialist, not while the alarm is running).
        /// </summary>
        public override void OnCreatureSpell(IUnitEntity caster, IUnitEntity target, uint spell4Id)
        {
            if (!publicEvent.HasFinished)
                alarm.OnCreatureSpell(caster, target, spell4Id);
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public override void Update(double lastTick)
        {
            actionQueue.Update(lastTick);
            UpdateWalks(lastTick);
            UpdateSpotlights(lastTick);

            if (publicEvent.HasFinished)
                return;

            UpdateFarmers(lastTick);
            UpdateGiveUp();
            UpdateGroo(lastTick);
            UpdateSpecialist();
            UpdateTrackedUnits(lastTick);
            alarm.Update(lastTick);
        }

        // --- map markers on the units -----------------------------------------------------------------------------------

        private void UpdateTrackedUnits(double lastTick)
        {
            trackedTimer -= lastTick;
            if (trackedTimer > 0d)
                return;
            trackedTimer = TrackedUnitInterval;

            List<(IUnitEntity Unit, uint Objective)> marked = grooRevealed
                ? groo is { InWorld: true, IsAlive: true } ? [(groo, KillGroo)] : []
                : scientists.Where(s => s.InWorld && s.IsAlive && s != givingUp).Select(s => (s, KillAssistants)).ToList();

            foreach (uint guid in tracked.Keys.Except(marked.Select(m => m.Unit.Guid)).ToList())
            {
                tracked.Remove(guid);
                foreach (IPlayer player in mapInstance.GetPlayers())
                    player.Session.EnqueueMessageEncrypted(new ServerMapTrackedUnitDisable { TrackedUnitId = guid });
            }

            foreach ((IUnitEntity unit, uint objective) in marked)
            {
                tracked[unit.Guid] = objective;
                foreach (IPlayer player in mapInstance.GetPlayers())
                    player.Session.EnqueueMessageEncrypted(new ServerMapTrackedUnitUpdate
                    {
                        TrackedUnitId          = unit.Guid,
                        Position               = unit.Position,
                        PublicEventObjectiveId = objective
                    });
            }
        }

        // --- the Recon Specialist and the spotlights --------------------------------------------------------------------

        /// <summary>
        /// The Recon Specialist standing near the first spotlight: Vesna's warning when he opens fire, then his call.
        /// </summary>
        private void UpdateSpecialist()
        {
            if (specialistCalled || specialist is not { InWorld: true, IsAlive: true, InCombat: true })
                return;

            specialistCalled = true;
            Communicator(VesnaSpotlights, VesnaTaranoft);

            IUnitEntity recon = specialist;
            actionQueue.Enqueue(SpecialistCallDelay, () =>
            {
                if (!recon.InWorld || !recon.IsAlive || publicEvent.HasFinished)
                    return;

                IPlayer target = (recon.TargetGuid.HasValue ? mapInstance.GetEntity<IPlayer>(recon.TargetGuid.Value) : null)
                    ?? NearestPlayer(recon.Position);
                if (target != null)
                    alarm.TriggerWith(recon, target);
            });
        }

        // --- the assistants and their farmers ---------------------------------------------------------------------------

        private void UpdateFarmers(double lastTick)
        {
            cowerResend -= lastTick;
            bool resend = cowerResend <= 0d;
            if (resend)
                cowerResend = CowerResendInterval;

            foreach (Farmer farmer in farmers.Where(f => f.Cornered && f.Entity.InWorld))
            {
                if (resend)
                    SendStandState(farmer.Entity, StandState.Emote, CowerEmote);

                bool near = IsPlayerNear(farmer.Entity.Position, FarmerCallRange);
                if (near && !farmer.PlayerNear)
                    farmer.NextPlea = Math.Min(farmer.NextPlea, 0.5d);
                farmer.PlayerNear = near;

                farmer.NextPlea -= lastTick;
                if (farmer.NextPlea > 0d || !near)
                    continue;

                farmer.NextPlea = FarmerPleaInterval * (0.75d + Random.Shared.NextDouble() * 0.5d);

                // the scientist speaks first, the farmer answers; a farmer whose scientist is gone just pleads
                IUnitEntity scientist = scientists
                    .Where(s => s.InWorld && s.IsAlive && s != givingUp
                        && Vector3.Distance(s.Position, farmer.Entity.Position) <= SiteRadius)
                    .OrderBy(s => Vector3.Distance(s.Position, farmer.Entity.Position))
                    .FirstOrDefault();
                IWorldEntity farmerEntity = farmer.Entity;
                uint plea = FarmerPleas[Random.Shared.Next(FarmerPleas.Length)];
                if (scientist == null)
                {
                    dialogue.Bark(farmerEntity, plea);
                    continue;
                }

                dialogue.Bark(scientist, ScientistLines[Random.Shared.Next(ScientistLines.Length)]);
                actionQueue.Enqueue(FarmerAnswerDelay, () =>
                {
                    if (farmer.Cornered && farmerEntity.InWorld)
                        dialogue.Bark(farmerEntity, plea);
                });
            }
        }

        /// <summary>
        /// The last assistant standing gives Groo up when he is nearly beaten.
        /// </summary>
        private void UpdateGiveUp()
        {
            if (givingUp != null || grooRevealed)
                return;

            List<IUnitEntity> alive = scientists.Where(s => s.InWorld && s.IsAlive).ToList();
            if (alive.Count != 1 || scientists.Count < AssistantLocations.Length)
                return;

            IUnitEntity last = alive[0];
            if (last.MaxHealth == 0u || (float)last.Health / last.MaxHealth > GiveUpHealth)
                return;

            givingUp = last;
            last.IsInvulnerable = true;
            last.SetInRangeCheck(0f);
            last.ThreatManager.ClearThreatList();
            last.Sheathed = true;
            dialogue.Say(last, ScientistGivesUp, true);
            ReleaseFarmer(last.Position);
            ClearSite(last.Position);
            log.LogInformation("Hycrest: The Science of Revenge, the last assistant gives Groo up.");

            actionQueue.Enqueue(GiveUpCountDelay, () =>
            {
                CountAssistant();
                RunOff(last, GiveUpRunDistance, GiveUpRunSpeed);
            });
        }

        /// <summary>
        /// The site around <paramref name="position"/> is done: its area marker goes (the others stay).
        /// </summary>
        private void ClearSite(Vector3 position)
        {
            int site = sitesLeft
                .OrderBy(i => Vector3.Distance(AssistantSites[i], position))
                .DefaultIfEmpty(-1)
                .First();
            if (site < 0 || Vector3.Distance(AssistantSites[site], position) > 30f)
                return;

            sitesLeft.Remove(site);
            if (sitesLeft.Count > 0)
                publicEvent.SetObjectiveLocations(KillAssistants, [.. sitesLeft.Select(i => AssistantLocations[i])]);
        }

        private void CountAssistant()
        {
            assistantsDown++;
            publicEvent.UpdateObjective(KillAssistants, 1);
        }

        /// <summary>
        /// The farmer at the site around <paramref name="position"/> is free: stops cowering and runs off.
        /// </summary>
        private void ReleaseFarmer(Vector3 position)
        {
            Farmer farmer = farmers
                .Where(f => f.Cornered && f.Entity.InWorld && Vector3.Distance(f.Entity.Position, position) <= SiteRadius)
                .OrderBy(f => Vector3.Distance(f.Entity.Position, position))
                .FirstOrDefault();
            if (farmer == null)
                return;

            farmer.Cornered = false;
            SendStandState(farmer.Entity, StandState.Stand, 0u);
            IWorldEntity entity = farmer.Entity;
            actionQueue.Enqueue(TimeSpan.FromSeconds(1.5), () => RunOff(entity, FarmerRunDistance, FarmerRunSpeed, position));
        }

        /// <summary>
        /// Run <paramref name="distance"/> metres away (from <paramref name="from"/>, or the nearest player), then leave the map.
        /// </summary>
        private void RunOff(IWorldEntity entity, float distance, float speed, Vector3? from = null)
        {
            if (!entity.InWorld)
                return;

            Vector3 away = entity.Position - (from ?? NearestPlayer(entity.Position)?.Position ?? entity.Position);
            away.Y = 0f;
            away = away.LengthSquared() > 0.01f ? Vector3.Normalize(away) : new Vector3(0f, 0f, 1f);

            Vector3 target = entity.Position + away * distance;
            float? height = mapInstance.GetTerrainHeight(target.X, target.Z);
            if (height.HasValue)
                target.Y = height.Value;

            entity.MovementManager.SetMode(ModeType.Walk);
            entity.MovementManager.LaunchSpline([entity.Position, target], SplineType.Linear, SplineMode.OneShot, speed);
            actionQueue.Enqueue(TimeSpan.FromSeconds(distance / speed + 0.5f), () =>
            {
                if (entity.InWorld)
                    entity.RemoveFromMap();
            });
        }

        private static void SendStandState(IWorldEntity entity, StandState standState, uint emoteId)
        {
            entity.EnqueueToVisible(new ServerEmote
            {
                Guid       = entity.Guid,
                StandState = standState,
                EmoteId    = emoteId
            });
        }

        // --- Groo -------------------------------------------------------------------------------------------------------

        private void RevealGroo()
        {
            if (grooRevealed)
                return;

            grooRevealed = true;
            publicEvent.SetPhase(GrooPhase);
            publicEvent.ActivateObjective(KillGroo);
            publicEvent.SetObjectiveLocations(KillGroo, GrooLocation);

            // the rebels go ahead to Sinnatus's Barn for the regroup, 60 m from Groo
            mapInstance.PublicEventManager.GetEvent(HycrestPublicEvent.Main)?
                .InvokeScriptCollection<IHycrestMainEventScript>(s => s.PrepareHideout(publicEvent.Id));
            log.LogInformation("Hycrest: The Science of Revenge, Groo revealed.");
        }

        private void UpdateGroo(double lastTick)
        {
            if (groo == null || !groo.InWorld || !groo.IsAlive)
                return;

            if (groo.InCombat != grooInCombat)
            {
                grooInCombat = groo.InCombat;
                if (grooInCombat)
                {
                    dialogue.BarkCombat(groo, CombatBark.EnterCombat);
                    grooAbilityTimer  = FirstAbility.TotalSeconds;
                    grooRotationIndex = 0;
                }
            }

            if (!grooInCombat)
                return;

            grooAbilityTimer -= lastTick;
            if (grooAbilityTimer > 0d || groo.IsCasting())
                return;

            grooAbilityTimer = AbilityInterval.TotalSeconds;
            CastGrooAbility(GrooRotation[grooRotationIndex++ % GrooRotation.Length]);
        }

        private void CastGrooAbility(GrooAbility ability)
        {
            IUnitEntity target = groo.TargetGuid.HasValue ? mapInstance.GetEntity<IUnitEntity>(groo.TargetGuid.Value) : null;
            if (target == null)
                return;

            switch (ability)
            {
                case GrooAbility.Mortar:
                {
                    // the mortar lands on anyone in the fight, not only his target
                    IPlayer victim = mapInstance.GetPlayers()
                        .Where(p => p.IsAlive && Vector3.Distance(p.Position, groo.Position) <= 30f)
                        .OrderBy(_ => Random.Shared.Next())
                        .FirstOrDefault();
                    CastAt(victim ?? target, MortarBarrage);
                    break;
                }
                case GrooAbility.Close:
                    CastAt(target, Vector3.Distance(target.Position, groo.Position) <= ShellStormRange ? ShellStorm : Longshot);
                    break;
                case GrooAbility.Flechettes:
                {
                    CastAt(target, ElectricFlechettes);
                    IUnitEntity caster = groo;
                    actionQueue.Enqueue(FlechettesHit, () => ShockPlayersInCone(caster));
                    break;
                }
            }
        }

        private void CastAt(IUnitEntity target, uint spell4Id)
        {
            groo.MovementManager.SetRotationFaceUnit(target.Guid);

            ISpellParameters parameters = spellParametersFactory.Resolve();
            parameters.PrimaryTargetId        = target.Guid;
            parameters.UserInitiatedSpellCast = false;
            groo.CastSpell(spell4Id, parameters);
            log.LogDebug($"Hycrest: Groo casts {spell4Id} at {target.Guid}.");
        }

        private void ShockPlayersInCone(IUnitEntity caster)
        {
            if (!caster.InWorld || !caster.IsAlive)
                return;

            float yaw = caster.Rotation.X;
            var forward = new Vector2(-MathF.Sin(yaw), -MathF.Cos(yaw));
            foreach (IPlayer player in mapInstance.GetPlayers().Where(p => p.IsAlive))
            {
                var offset = new Vector2(player.Position.X - caster.Position.X, player.Position.Z - caster.Position.Z);
                float distance = offset.Length();
                if (distance > FlechettesRange || distance < 0.01f)
                    continue;

                float angle = MathF.Acos(Math.Clamp(Vector2.Dot(forward, offset / distance), -1f, 1f));
                if (angle > FlechettesHalfAngle)
                    continue;

                ISpellParameters parameters = spellParametersFactory.Resolve();
                parameters.PrimaryTargetId        = player.Guid;
                parameters.UserInitiatedSpellCast = false;
                caster.CastSpell(ShockingDoT, parameters);
            }
        }

        // --- kills, objectives and the end ------------------------------------------------------------------------------

        /// <summary>
        /// Invoked when a unit on the map has been killed.
        /// </summary>
        public override void OnEntityKilled(IUnitEntity unit)
        {
            if (ending || publicEvent.HasFinished)
                return;

            if (unit is IPlayer)
            {
                if (groo is { IsAlive: true, InCombat: true })
                    dialogue.BarkCombat(groo, CombatBark.Kill);
                return;
            }

            if (!IsOwnEntity(unit))
                return;

            if (unit.CreatureId is ScientistMale or ScientistFemale && unit != givingUp && !grooRevealed)
            {
                ReleaseFarmer(unit.Position);
                ClearSite(unit.Position);
                CountAssistant();
            }
            else if (unit == groo)
                dialogue.BarkCombat(unit, CombatBark.Death);

        }

        /// <summary>
        /// Invoked when the status of an objective changes.
        /// </summary>
        public override void OnPublicEventObjectiveStatus(IPublicEventObjective objective)
        {
            if (objective.Status != PublicEventStatus.Succeeded)
                return;

            switch (objective.Entry.Id)
            {
                case KillAssistants:
                    RevealGroo();
                    break;
                case KillGroo:
                    OnGrooKilled();
                    break;
            }
        }

        private void OnGrooKilled()
        {
            if (ending)
                return;

            ending = true;
            foreach (uint guid in tracked.Keys)
                foreach (IPlayer player in mapInstance.GetPlayers())
                    player.Session.EnqueueMessageEncrypted(new ServerMapTrackedUnitDisable { TrackedUnitId = guid });
            tracked.Clear();
            Communicator(VesnaPayoff, VesnaTaranoft);
            actionQueue.Enqueue(MissionEndDelay, CompleteMission);
            log.LogInformation("Hycrest: The Science of Revenge, Groo is dead.");
        }

        private void Communicator(uint textId, uint creatureId)
        {
            foreach (IPlayer player in mapInstance.GetPlayers())
                storyBuilder.SendStoryCommunicator(textId, creatureId, player);
        }

        private IPlayer NearestPlayer(Vector3 position)
        {
            return mapInstance.GetPlayers()
                .Where(p => p.IsAlive)
                .OrderBy(p => Vector3.Distance(p.Position, position))
                .FirstOrDefault();
        }

        private bool IsPlayerNear(Vector3 position, float range)
        {
            return mapInstance.GetPlayers().Any(p => Vector3.Distance(p.Position, position) <= range);
        }

        /// <summary>
        /// Invoked when the hideout's door closes after the mission: the units are gone, forget them.
        /// </summary>
        public override void OnHideoutClosed()
        {
            base.OnHideoutClosed();
            scientists.Clear();
            farmers.Clear();
            groo       = null;
            specialist = null;
        }

        /// <summary>
        /// Return true if <paramref name="entity"/> stays in the world after the mission: the Dominion's field units, until
        /// the hideout's barn closes.
        /// </summary>
        protected override bool KeepAfterMission(IGridEntity entity)
        {
            return entity is IUnitEntity unit && (uint)unit.Faction1 == HostileFaction;
        }
    }
}
