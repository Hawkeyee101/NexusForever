using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Cinematic;
using NexusForever.Game.Abstract.Cinematic.Cinematics;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Script.Template.Filter;
using NexusForever.Shared;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    [ScriptFilterOwnerId(1149)]
    public class TheHycrestInsurrectionMapScript : EventBaseContentMapScript, IUpdate
    {
        public override uint PublicEventId => HycrestPublicEvent.Main;

        // arrival text: the text-only intro cinematic (683169-683172) is experimental, it is unknown whether the client
        // shows it without a camera; the default are the Caretaker story communicators (534606, 534607)
        public const bool UseCinematicTextIntro = false;

        private const uint NighttimeSkyboxSpell = 27236u;
        private const uint CaretakerMessage1    = 534606u;
        private const uint CaretakerMessage2    = 534607u;
        private const uint CaretakerMessageDurationMs = 10000u;

        private static readonly TimeSpan ArrivalSkyboxDelay   = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan ArrivalMessage1Delay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ArrivalMessage2Delay = TimeSpan.FromSeconds(12);

        private readonly TimedActionQueue arrivalQueue = new();

        #region Dependency Injection

        private readonly IFactory<ISpellParameters> spellParametersFactory;
        private readonly IStoryBuilder storyBuilder;
        private readonly ICinematicFactory cinematicFactory;

        public TheHycrestInsurrectionMapScript(
            IFactory<ISpellParameters> spellParametersFactory,
            IStoryBuilder storyBuilder,
            ICinematicFactory cinematicFactory)
        {
            this.spellParametersFactory = spellParametersFactory;
            this.storyBuilder           = storyBuilder;
            this.cinematicFactory       = cinematicFactory;
        }

        #endregion

        /// <summary>
        /// Invoked when <see cref="IGridEntity"/> is added to map.
        /// </summary>
        public override void OnAddToMap(IGridEntity entity)
        {
            base.OnAddToMap(entity);

            if (entity is not IPlayer player)
                return;

            // the intro event is created by the main event script, the base class only joins the main event
            HycrestPublicEvent.JoinPublicTeam(map.PublicEventManager.GetEvent(HycrestPublicEvent.Intro), player);

            StartArrival(player);
        }

        private void StartArrival(IPlayer player)
        {
            uint guid = player.Guid;

            // the adventure takes place at night, the night sky is a spell visual (WorldSky 531)
            arrivalQueue.Enqueue(ArrivalSkyboxDelay, () => WithPlayer(guid, p =>
            {
                ISpellParameters parameters = spellParametersFactory.Resolve();
                parameters.PrimaryTargetId = p.Guid;
                p.CastSpell(NighttimeSkyboxSpell, parameters);
            }));

            if (UseCinematicTextIntro)
            {
                player.CinematicManager.QueueCinematic(cinematicFactory.CreateCinematic<IHycrestInsurrectionOnEnter>());
                return;
            }

            arrivalQueue.Enqueue(ArrivalMessage1Delay, () => WithPlayer(guid, p =>
                storyBuilder.SendStoryCommunicator(CaretakerMessage1, (uint)PublicEventCreature.TheCaretaker, p, CaretakerMessageDurationMs)));
            arrivalQueue.Enqueue(ArrivalMessage2Delay, () => WithPlayer(guid, p =>
                storyBuilder.SendStoryCommunicator(CaretakerMessage2, (uint)PublicEventCreature.TheCaretaker, p, CaretakerMessageDurationMs)));
        }

        private void WithPlayer(uint guid, Action<IPlayer> action)
        {
            // the player may have left the map since the action was queued
            IPlayer player = map.GetEntity<IPlayer>(guid);
            if (player != null)
                action(player);
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public void Update(double lastTick)
        {
            arrivalQueue.Update(lastTick);

            // public event scripts don't receive ticks from the engine, forward them so event scripts can run timers
            foreach (IPublicEvent publicEvent in map.PublicEventManager.GetEvents().ToList())
                publicEvent.InvokeScriptCollection<IUpdate>(s => s.Update(lastTick));
        }
    }
}
