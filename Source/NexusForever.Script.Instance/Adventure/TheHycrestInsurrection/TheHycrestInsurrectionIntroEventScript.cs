using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Trigger;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Abstract.Spell;
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

        // retail: the Caretaker's hologram stands in the ship; after the Caretaker's two messages (2 s and 12 s after
        // arriving, 10 s each) Dawson comes out of the door where the hologram was
        private static readonly TimeSpan DawsonAppearDelay = TimeSpan.FromSeconds(22);

        // the delay above runs from the map add, while the client is still loading; players get to see the hologram on
        // board for at least this long
        private const double HologramMinOnBoard = 10d;
        private const uint DawsonPhase = 1u;

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

        public TheHycrestInsurrectionIntroEventScript(
            ILogger<TheHycrestInsurrectionIntroEventScript> log,
            IGameTableManager gameTableManager,
            IFactory<ISpellParameters> spellParametersFactory)
        {
            this.log                    = log;
            this.spellParametersFactory = spellParametersFactory;
            dialogue = new HycrestDialogue(gameTableManager);
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
                case PublicEventCreature.DominionDropship:
                    dropShip.ShipGuid = worldEntity.Guid;
                    break;
                case PublicEventCreature.DropshipDoorRight:
                case PublicEventCreature.DropshipDoorLeft:
                    dropShip.DoorGuids.Add(worldEntity.Guid);
                    break;
                case PublicEventCreature.ViceMarshalDawson:
                    dawsonGuid          = worldEntity.Guid;
                    dropShip.DawsonGuid = worldEntity.Guid;
                    break;
                case PublicEventCreature.CaretakerHologram:
                    hologramGuid = worldEntity.Guid;
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
                actionQueue.Enqueue(delay, () => dialogue.Say(mapInstance.GetEntity<IWorldEntity>(dawsonGuid), textId, gesture: false));
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

            if (dawsonAppearQueued)
                return;

            dawsonAppearQueued = true;
            actionQueue.Enqueue(DawsonAppearDelay, ShowDawson);
        }

        private void ShowDawson()
        {
            double onBoard = dropShip.SinceFirstBoard ?? 0d;
            if (onBoard < HologramMinOnBoard)
            {
                actionQueue.Enqueue(TimeSpan.FromSeconds(HologramMinOnBoard - onBoard), ShowDawson);
                return;
            }

            // the hologram makes way and Dawson (phase 1 spawn) appears in its place
            mapInstance.GetEntity<IWorldEntity>(hologramGuid)?.RemoveFromMap();
            hologramGuid = 0u;

            publicEvent.SetPhase(DawsonPhase);
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
            // the briefing is over: open the doors, players jump out themselves with the slow-burn jetpack
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
