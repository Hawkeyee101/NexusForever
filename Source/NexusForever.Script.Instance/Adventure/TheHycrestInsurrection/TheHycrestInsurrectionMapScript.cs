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

        // retail: arriving players see the Caretaker's narration (683169-683172) on a black screen, which also hides
        // the move onto the ship; the Caretaker's story communicators follow (intro event script, after boarding)
        public const bool UseCinematicTextIntro = true;

        private const uint NighttimeSkyboxSpell = 27236u;

        private static readonly TimeSpan ArrivalSkyboxDelay = TimeSpan.FromSeconds(1);

        private readonly TimedActionQueue arrivalQueue = new();

        // a teleport within the map removes and re-adds the player, the arrival only plays on the first add
        private readonly HashSet<ulong> arrivedCharacters = [];

        #region Dependency Injection

        private readonly IFactory<ISpellParameters> spellParametersFactory;
        private readonly ICinematicFactory cinematicFactory;

        public TheHycrestInsurrectionMapScript(
            IFactory<ISpellParameters> spellParametersFactory,
            ICinematicFactory cinematicFactory)
        {
            this.spellParametersFactory = spellParametersFactory;
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

            if (arrivedCharacters.Add(player.CharacterId))
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
                player.CinematicManager.QueueCinematic(cinematicFactory.CreateCinematic<IHycrestInsurrectionOnEnter>());
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
