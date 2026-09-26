using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity.Trigger;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.Script.Template;
using NexusForever.Script.Template.Filter;
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

        // objective 2155 is a TimedWin, the script completes it just before the engine failure timer (FailureTimeMs) would fail it
        private static readonly TimeSpan BriefingMargin = TimeSpan.FromMilliseconds(500);

        private IPublicEvent publicEvent;
        private IMapInstance mapInstance;

        private UpdateTimer briefingTimer;
        private IVolumeGridTriggerEntity barnTrigger;

        #region Dependency Injection

        private readonly ILogger<TheHycrestInsurrectionIntroEventScript> log;

        public TheHycrestInsurrectionIntroEventScript(
            ILogger<TheHycrestInsurrectionIntroEventScript> log)
        {
            this.log = log;
        }

        #endregion

        /// <summary>
        /// Invoked when <see cref="IScript"/> is loaded.
        /// </summary>
        public void OnLoad(IPublicEvent owner)
        {
            publicEvent = owner;
            mapInstance = publicEvent.Map as IMapInstance;

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
            if (briefingTimer == null)
                return;

            briefingTimer.Update(lastTick);
            if (!briefingTimer.HasElapsed)
                return;

            briefingTimer = null;
            publicEvent.UpdateObjective(PublicEventObjective.ListenToDawson, 1);
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
            publicEvent.ActivateObjective(PublicEventObjective.ListenToDawson);

            IPublicEventObjective objective = publicEvent.GetTeams()
                .SelectMany(t => t.GetObjectives())
                .FirstOrDefault(o => o.Entry.Id == (uint)PublicEventObjective.ListenToDawson);

            TimeSpan duration = TimeSpan.FromMilliseconds(objective?.Entry.FailureTimeMs ?? 20000u) - BriefingMargin;
            briefingTimer = new UpdateTimer(duration);
        }

        private void StartMeetVesna()
        {
            publicEvent.ActivateObjective(PublicEventObjective.MeetVesnaTaranoft);

            barnTrigger = publicEvent.CreateEntity<IVolumeGridTriggerEntity>();
            barnTrigger.Initialise(BarnTriggerId, BarnTriggerRange, BarnTriggerObjectId);
            barnTrigger.AddToMap(mapInstance, BarnTriggerPosition);
        }

        private void FinishIntro()
        {
            // remove the trigger so it can't update other objectives with the same object id later ("Return to the Barn" in The Great Escape)
            if (barnTrigger?.InWorld == true)
                barnTrigger.RemoveFromMap();
            barnTrigger = null;

            publicEvent.Finish(PublicEventTeam.PublicTeam);
            log.LogInformation($"Hycrest: intro {HycrestPublicEvent.Intro} completed.");

            IPublicEvent mainEvent = mapInstance.PublicEventManager.GetEvent(HycrestPublicEvent.Main);
            mainEvent?.InvokeScriptCollection<TheHycrestInsurrectionEventScript>(s => s.OnIntroComplete());
        }
    }
}
