using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Cinematic;
using NexusForever.Game.Abstract.Cinematic.Cinematics;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Trigger;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.GameTable;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;
using NexusForever.Shared;
using NexusForever.Shared.Game;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    /// <summary>
    /// Intro: report to Vice-Marshal Dawson, listen to the briefing, meet Vesna Taranoft at the Abandoned Barn.
    /// </summary>
    [ScriptFilterOwnerId(HycrestPublicEvent.Intro)]
    public class TheHycrestInsurrectionIntroEventScript : IPublicEventScript, IOwnedScript<IPublicEvent>
    {
        // objective 189 is a ParticipantsInTriggerVolume objective with object id 1994 at WorldLocation2 13091 (radius 1)
        private const uint BarnTriggerId = 114902u;
        private const uint BarnTriggerObjectId = 1994u;
        private const float BarnTriggerRange = 8f;
        private static readonly Vector3 BarnTriggerPosition = new(-2526.80f, -925.82f, -1190.93f);

        // Vice-Marshal Dawson's briefing, spoken to the whole ship during the 20 s objective 2155
        private static readonly (TimeSpan Delay, uint TextId)[] DawsonBriefing =
        [
            (TimeSpan.FromSeconds(0.5), 162291u), // "You are here to assist Agent Vesna Taranoft..."
            (TimeSpan.FromSeconds(7),   162292u), // "Use your best judgment out there..."
            (TimeSpan.FromSeconds(13.5), 455316u) // "Your suits are equipped with a slow burn jetpack..."
        ];

        // the ship leaves once every player has jumped; this is the latest it waits after the doors open
        // (189 starts right after the briefing, so leaving "when 189 starts" would push everyone off at once)
        private static readonly TimeSpan ShipDepartDeadline = TimeSpan.FromSeconds(30);

        // arriving players are put on the ship's deck shortly after entering the map
        private static readonly TimeSpan BoardDelay = TimeSpan.FromSeconds(0.5);

        // arrival, timed from the moment a player's client has finished loading: the narration's black screen (20 s,
        // HycrestInsurrectionOnEnter; boarding happens under it once the ship has arrived) fades with the green
        // "synchronisation" glow, the Caretaker's two story communicators play (10 s each) while the hologram talks, then
        // Dawson comes out of the door where the hologram was
        private static readonly TimeSpan IntroTextRemaining = TimeSpan.FromSeconds(TheHycrestInsurrectionMapScript.UseCinematicTextIntro ? 20 : 1.5);
        private static readonly TimeSpan Message1Delay      = TimeSpan.FromSeconds(0.5);
        private static readonly TimeSpan Message2Delay      = TimeSpan.FromSeconds(8.8);
        private static readonly TimeSpan Repeat1Delay       = IntroTextRemaining + TimeSpan.FromSeconds(1);
        private static readonly TimeSpan Repeat2Delay       = Repeat1Delay + TimeSpan.FromSeconds(10);
        private static readonly TimeSpan DawsonAppearDelay  = Repeat2Delay + TimeSpan.FromSeconds(10.5);
        private const uint DawsonPhase = 1u;
        private static readonly TimeSpan DawsonTalkFallback = TimeSpan.FromSeconds(60);

        private const uint CaretakerMessage1          = 534606u;
        private const uint CaretakerMessage2          = 534607u;
        private const uint CaretakerMessageDurationMs = 8000u;

        // retail shows the two Caretaker messages as centred, typed-in story text on the black screen: the story
        // communicator's window type 2 (3 looks the same; 0 and 1 are the portrait pop-ups)
        private const WindowType NarrationWindow = (WindowType)2;

        // the players "synchronise" into the simulation as the narration's black screen fades: Transimulator
        // Synchronization (spell 62968; green hologram overlay and Eldan teleporter, 3 s). Its CC state DisableCinematic
        // has no duration (CancelOnly), so the spell is finished after SyncDuration
        private const uint SyncSpell = 62968u;

        // "Housing - 1x1 Tiki Lounge - Blackout": CC state Blind for 8 s, which blacks out the screen while the interface
        // (the story text) stays visible
        private const uint BlackoutSpell = 45014u;
        private static readonly TimeSpan[] BlackoutCasts = [TimeSpan.Zero, TimeSpan.FromSeconds(7.5), TimeSpan.FromSeconds(15)];
        private static readonly TimeSpan SyncDelay    = IntroTextRemaining;
        private static readonly TimeSpan SyncDuration = TimeSpan.FromSeconds(3);

        // after the black screen the hologram repeats the two messages as portrait pop-ups (10 s each) and talks (talk
        // emote, Default_Talk ~4 s) during them, then Dawson comes out
        private static readonly TimeSpan[] HologramTalkTimes = [TimeSpan.Zero, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];

        private IPublicEvent publicEvent;
        private IMapInstance mapInstance;

        private readonly TimedActionQueue actionQueue = new();
        private IVolumeGridTriggerEntity barnTrigger;
        private bool meetVesnaActive;
        private bool barnArrivalSent;

        private HycrestDropShip dropShip;
        private UpdateTimer shipDepartTimer;
        private uint dawsonGuid;
        private uint hologramGuid;
        private bool dawsonAppearQueued;

        #region Dependency Injection

        private readonly ILogger<TheHycrestInsurrectionIntroEventScript> log;
        private readonly HycrestDialogue dialogue;
        private readonly IFactory<ISpellParameters> spellParametersFactory;
        private readonly IStoryBuilder storyBuilder;
        private readonly ICinematicFactory cinematicFactory;

        public TheHycrestInsurrectionIntroEventScript(
            ILogger<TheHycrestInsurrectionIntroEventScript> log,
            IGameTableManager gameTableManager,
            IFactory<ISpellParameters> spellParametersFactory,
            IStoryBuilder storyBuilder,
            ICinematicFactory cinematicFactory)
        {
            this.log                    = log;
            this.spellParametersFactory = spellParametersFactory;
            this.storyBuilder           = storyBuilder;
            this.cinematicFactory       = cinematicFactory;
            dialogue = new HycrestDialogue(gameTableManager, actionQueue);
        }

        #endregion

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public void OnLoad(IPublicEvent owner)
        {
            publicEvent = owner;
            mapInstance = publicEvent.Map as IMapInstance;
            dropShip    = new HycrestDropShip(mapInstance, spellParametersFactory, log, actionQueue);
            dropShip.BoardWithLoadingScreen = !TheHycrestInsurrectionMapScript.UseCinematicTextIntro;
            dropShip.PlayerLoaded  += OnPlayerLoaded;

            // spawns Vice-Marshal Dawson, objective 2113 is initial and completed by talking to him
            publicEvent.SetPhase(0u);
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        /// <remarks>
        /// Forwarded by <see cref="TheHycrestInsurrectionMapScript"/>, public event scripts don't receive ticks from the engine.
        /// </remarks>
        public void Update(double lastTick)
        {
            actionQueue.Update(lastTick);
            dropShip.Update(lastTick);
            UpdateShipDeparture(lastTick);
            UpdateBarnArrival();
        }

        /// <summary>
        /// Invoked when a <see cref="IGridEntity"/> is added to the map the public event is on.
        /// </summary>
        public void OnAddToMap(IGridEntity entity)
        {
            switch (entity)
            {
                case IPlayer player:
                {
                    if (meetVesnaActive)
                        publicEvent.SetObjectiveDynamicMax(PublicEventObjective.MeetVesnaTaranoft, GetPartySize(joining: player));

                    OnPlayerArrival(player);
                    break;
                }
                case IWorldEntity worldEntity:
                    OnAddToMapWorldEntity(worldEntity);
                    break;
            }
        }

        private void OnAddToMapWorldEntity(IWorldEntity worldEntity)
        {
            switch ((PublicEventCreature)worldEntity.CreatureId)
            {
                case PublicEventCreature.IntroSetShip:
                    dropShip.ShipGuid = worldEntity.Guid;
                    break;
                case PublicEventCreature.DominionDropship:
                    // hovering, engines running; flies in from its start point before anyone is on board (the doors and
                    // the hologram, spawned in the same batch, are registered by then)
                    dropShip.ShipGuid = worldEntity.Guid;
                    HycrestDropShip.SetState(worldEntity, StandState.State1);
                    actionQueue.Enqueue(TimeSpan.FromSeconds(0.5), dropShip.FlyIn);
                    break;
                case PublicEventCreature.DropshipDoorRight:
                    dropShip.RightDoorGuid = worldEntity.Guid;
                    HycrestDropShip.SetState(worldEntity, StandState.State0);
                    break;
                case PublicEventCreature.DropshipDoorLeft:
                    dropShip.LeftDoorGuid = worldEntity.Guid;
                    HycrestDropShip.SetState(worldEntity, StandState.State0);
                    break;
                case PublicEventCreature.ViceMarshalDawson:
                    dawsonGuid          = worldEntity.Guid;
                    dropShip.DawsonGuid = worldEntity.Guid;
                    break;
                case PublicEventCreature.CaretakerHologram:
                    hologramGuid          = worldEntity.Guid;
                    dropShip.HologramGuid = worldEntity.Guid;
                    break;
            }
        }

        /// <summary>
        /// Invoked when a <see cref="IGridEntity"/> is removed from the map the public event is on.
        /// </summary>
        public void OnRemoveFromMap(IGridEntity entity)
        {
            if (entity is IPlayer && meetVesnaActive)
                publicEvent.SetObjectiveDynamicMax(PublicEventObjective.MeetVesnaTaranoft, GetPartySize());
        }

        /// <summary>
        /// Return the number of players in the instance.
        /// </summary>
        /// <remarks>
        /// Add and remove callbacks run while the player isn't in the map's player list: added after the callbacks,
        /// removed before them. A joining player is counted explicitly.
        /// </remarks>
        private uint GetPartySize(IPlayer joining = null)
        {
            uint count = (uint)mapInstance.GetPlayers().Count(p => p != joining);
            if (joining != null)
                count++;

            return Math.Max(count, 1u);
        }

        /// <summary>
        /// Invoked when the <see cref="IPublicEventObjective"/> status changes.
        /// </summary>
        public void OnPublicEventObjectiveStatus(IPublicEventObjective objective)
        {
            if (objective.Status != PublicEventStatus.Succeeded)
                return;

            switch ((PublicEventObjective)objective.Entry.Id)
            {
                case PublicEventObjective.ReportToDawson:
                    StartBriefing();
                    break;
                case PublicEventObjective.ListenToDawson:
                    StartMeetVesna();
                    break;
                case PublicEventObjective.MeetVesnaTaranoft:
                    FinishIntro();
                    break;
            }
        }

        private void StartBriefing()
        {
            // 2155 is a TimedWin: the engine completes it when its timer (FailureTimeMs, 20 s) ends
            publicEvent.ActivateObjective(PublicEventObjective.ListenToDawson);

            foreach ((TimeSpan delay, uint textId) in DawsonBriefing)
                actionQueue.Enqueue(delay, () => dialogue.Say(mapInstance.GetEntity<IWorldEntity>(dawsonGuid), textId, gesture: true));
        }

        private void OnPlayerArrival(IPlayer player)
        {
            uint guid = player.Guid;
            actionQueue.Enqueue(BoardDelay, () =>
            {
                IPlayer arrived = mapInstance.GetEntity<IPlayer>(guid);
                if (arrived != null)
                    dropShip.Board(arrived);
            });
        }

        private void OnPlayerLoaded(IPlayer player)
        {
            uint guid = player.Guid;

            // the narration on a black screen; it also hides the boarding teleport that follows. The black is a blind
            // (a cinematic's black hid the story text): spell 45014, 8 s, recast until the narration ends, then finished
            if (TheHycrestInsurrectionMapScript.UseCinematicTextIntro)
            {
                foreach (TimeSpan delay in BlackoutCasts)
                    actionQueue.Enqueue(delay, () => WithPlayer(guid, CastBlackout));
                actionQueue.Enqueue(IntroTextRemaining, () => WithPlayer(guid, p => p.GetSpellBySpellId(BlackoutSpell)?.Finish()));
            }

            actionQueue.Enqueue(SyncDelay, () => WithPlayer(guid, StartSync));
            actionQueue.Enqueue(SyncDelay + SyncDuration, () => WithPlayer(guid, p => p.GetSpellBySpellId(SyncSpell)?.Finish()));
            actionQueue.Enqueue(Message1Delay, () => WithPlayer(guid, p => PlayNarration(p, CaretakerMessage1)));
            actionQueue.Enqueue(Message2Delay, () => WithPlayer(guid, p => PlayNarration(p, CaretakerMessage2)));
            actionQueue.Enqueue(Repeat1Delay, () => WithPlayer(guid, p => PlayCaretakerMessage(p, CaretakerMessage1)));
            actionQueue.Enqueue(Repeat2Delay, () => WithPlayer(guid, p => PlayCaretakerMessage(p, CaretakerMessage2)));

            if (dawsonAppearQueued)
                return;

            dawsonAppearQueued = true;
            actionQueue.Enqueue(DawsonAppearDelay, ShowDawson);
        }

        private void WithPlayer(uint guid, Action<IPlayer> action)
        {
            // the player may have left the map since the action was queued
            IPlayer player = mapInstance.GetEntity<IPlayer>(guid);
            if (player != null)
                action(player);
        }

        private void CastBlackout(IPlayer player)
        {
            ISpellParameters parameters = spellParametersFactory.Resolve();
            parameters.PrimaryTargetId = player.Guid;
            player.CastSpell(BlackoutSpell, parameters);
        }

        private void StartSync(IPlayer player)
        {
            ISpellParameters parameters = spellParametersFactory.Resolve();
            parameters.PrimaryTargetId = player.Guid;
            player.CastSpell(SyncSpell, parameters);
        }

        private void PlayNarration(IPlayer player, uint textId)
        {
            storyBuilder.SendStoryCommunicator(textId, (uint)PublicEventCreature.TheCaretaker, player, CaretakerMessageDurationMs,
                windowTypeId: NarrationWindow);
        }

        private void PlayCaretakerMessage(IPlayer player, uint textId)
        {
            storyBuilder.SendStoryCommunicator(textId, (uint)PublicEventCreature.TheCaretaker, player, 10000u);
            PlayHologramTalk();
        }

        private void PlayHologramTalk()
        {
            foreach (TimeSpan delay in HologramTalkTimes)
            {
                actionQueue.Enqueue(delay, () =>
                {
                    IWorldEntity hologram = mapInstance.GetEntity<IWorldEntity>(hologramGuid);
                    if (hologram != null)
                        dialogue.PlayTalk(hologram);
                });
            }
        }

        private void ShowDawson()
        {
            // the hologram makes way and Dawson (phase 1 spawn) appears in its place
            mapInstance.GetEntity<IWorldEntity>(hologramGuid)?.RemoveFromMap();
            hologramGuid = 0u;

            publicEvent.SetPhase(DawsonPhase);

            // fallback: if nobody talks to Dawson (e.g. everyone fell off the ship), the briefing starts by itself
            actionQueue.Enqueue(DawsonTalkFallback, () =>
            {
                IPublicEventObjective objective = publicEvent.GetTeams()
                    .SelectMany(t => t.GetObjectives())
                    .FirstOrDefault(o => o.Entry.Id == (uint)PublicEventObjective.ReportToDawson);
                if (objective?.Status != PublicEventStatus.Active)
                    return;

                log.LogInformation("Hycrest: nobody talked to Dawson, starting the briefing.");
                publicEvent.UpdateObjective(PublicEventObjective.ReportToDawson, 1);
            });
        }

        private void UpdateBarnArrival()
        {
            if (!meetVesnaActive || barnArrivalSent)
                return;

            // the first player inside the barn trigger raises the count of 189; the main event plays Ayita's first line
            IPublicEventObjective objective = publicEvent.GetTeams()
                .SelectMany(t => t.GetObjectives())
                .FirstOrDefault(o => o.Entry.Id == (uint)PublicEventObjective.MeetVesnaTaranoft);
            if (objective == null || objective.Count == 0)
                return;

            barnArrivalSent = true;
            mapInstance.PublicEventManager.GetEvent(HycrestPublicEvent.Main)?
                .InvokeScriptCollection<TheHycrestInsurrectionEventScript>(s => s.OnFirstBarnArrival());
        }

        private void UpdateShipDeparture(double lastTick)
        {
            if (!dropShip.DoorsOpen || dropShip.Departed)
                return;

            shipDepartTimer?.Update(lastTick);
            if (dropShip.EveryoneOff() || shipDepartTimer?.HasElapsed == true)
                dropShip.Depart();
        }

        private void StartMeetVesna()
        {
            // the briefing is over: the right door opens, players walk down the ramp and jump
            dropShip.OpenDoors();
            shipDepartTimer = new UpdateTimer(ShipDepartDeadline);

            // every player in the instance has to gather in the barn, the client shows "Waiting for N more" from the max
            meetVesnaActive = true;
            publicEvent.ActivateObjective(PublicEventObjective.MeetVesnaTaranoft, GetPartySize());

            barnTrigger = publicEvent.CreateEntity<IVolumeGridTriggerEntity>();
            barnTrigger.Initialise(BarnTriggerId, BarnTriggerRange, BarnTriggerObjectId);
            barnTrigger.AddToMap(mapInstance, BarnTriggerPosition);
        }

        private void FinishIntro()
        {
            meetVesnaActive = false;

            // remove the trigger so it can't update other objectives with the same object id later ("Return to the Barn" in The Great Escape)
            if (barnTrigger?.InWorld == true)
                barnTrigger.RemoveFromMap();
            barnTrigger = null;

            publicEvent.Finish(PublicEventTeam.PublicTeam);
            log.LogInformation($"Hycrest: intro {HycrestPublicEvent.Intro} completed.");

            IPublicEvent mainEvent = mapInstance.PublicEventManager.GetEvent(HycrestPublicEvent.Main);

            // the last player to arrive can complete 189 before the per-tick arrival check ran (always the case solo)
            if (!barnArrivalSent)
            {
                barnArrivalSent = true;
                mainEvent?.InvokeScriptCollection<TheHycrestInsurrectionEventScript>(s => s.OnFirstBarnArrival());
            }

            mainEvent?.InvokeScriptCollection<TheHycrestInsurrectionEventScript>(s => s.OnIntroComplete());
        }
    }
}
