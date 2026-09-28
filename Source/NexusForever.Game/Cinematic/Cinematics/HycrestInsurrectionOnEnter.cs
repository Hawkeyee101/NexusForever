using System.Numerics;
using NexusForever.Game.Abstract.Cinematic;
using NexusForever.Game.Abstract.Cinematic.Cinematics;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map.Search;
using NexusForever.Network.World.Entity;

namespace NexusForever.Game.Cinematic.Cinematics
{
    /// <summary>
    /// Arrival in The Hycrest Insurrection: the black screen under the Caretaker's narration (story windows), which also hides the
    /// move onto the intro ship, then the players "synchronise" into the simulation (green glow).
    /// </summary>
    /// <remarks>
    /// Not built from packet captures. Live retail showed the narration on black (spell 53052 "Adventure Intros Generic
    /// Cinematic Disable" suggests the fly-in intros were turned off). The camera is the GC217 intro camera: like
    /// EvilFromTheEtherOnCreate, the camera actor sits at the set origin and plays its whole baked timeline (visual 45237,
    /// Cinematic_Misc_00), and the view is attached to its camera bones, one per shot (7, 8 at 6.0 s, 9 at 12.93 s, the
    /// starts of its Cinematic_Misc_01/02/03). Without the animation the bones stay in the rest pose, far below the map.
    /// The set origin is taken as the intro set ship's spawn position (it is the same set). Subtitles are only shown
    /// when cinematic subtitles are enabled in the client options.
    /// </remarks>
    public class HycrestInsurrectionOnEnter : CinematicBase, IHycrestInsurrectionOnEnter
    {
        private const uint ActorCamera = 70555u; // GC217 - Hycrest Adventure Intro - Camera

        // local dev aid ("!story hycrestintro <flags> [cancel]"): the story texts don't show over the cinematic with flags 7,
        // other flag values are tried in game
        public static ushort? FlagsOverride { get; set; }
        public static ushort? CancelModeOverride { get; set; }

        // GC217 set origin: where the intro set ship (70557) used to spawn, identity rotation; only the final fade-in
        // shows the camera's view
        private static readonly Vector3 SetOrigin = new(-2520.6f, -873.6975f, -1240f);
        private const float SetAngle = 0f;

        private const uint CinematicTimeline = 45237u; // plays Cinematic_Misc_00 (the whole timeline) on an actor

        // retail: as everyone appears, the whole ship glows green (synchronisation); the players get the spell outside the
        // cinematic, the ship (a platform, it can't be a spell target here) gets the green hologram overlay of spell 62968
        private const uint ShipCreature             = 17722u;
        private const uint SyncHologramVisualEffect = 20846u;
        private const uint SyncDuration             = 3000u;

        // retail: a black screen with the narration typed in. The camera's fade goes to black at once, holds under the
        // narration and fades the view in as it ends. The green synchronisation glow follows outside the cinematic, like
        // retail (intro event script)
        // held until just before the cinematic ends: a fade-in showed the camera's view (under the map) for a moment.
        // Short: players enter the map standing on the ship, the black only covers everything settling into place (the
        // narration now follows as portrait pop-ups)
        private const uint   BlackDuration  = 3000u;
        private const uint   FadeInAt       = BlackDuration - 200u;
        private const ushort BlackHold      = (ushort)FadeInAt;
        private const ushort BlackFadeIn    = 200;
        private const uint   FadeTransition = 2u; // 3 held white (the hold works), 1 didn't fade at all

        private const uint CaretakerIntroVoice = 34515u;
        private const uint CaretakerIntroAt    = 1000u;


        protected override void Setup()
        {
            Duration          = BlackDuration;
            InitialFlags      = FlagsOverride ?? 7;
            InitialCancelMode = CancelModeOverride ?? 0; // retail can't be skipped (2 showed "Esc to skip")
            CinematicId       = 0;

            // a 0.1 s start fade gave no black screen at all (28 Sep 2026); the 1.5 s fade shows the world for a moment
            StartTransition = new Transition(0, 1, 2, 1500, 0, 1500);
            EndTransition   = new Transition(FadeInAt, 0, 0);

            var origin = new Position(SetOrigin);
            IActor camera = new Actor(ActorCamera, 6, SetAngle, origin);
            AddActor(camera, [new VisualEffect(CinematicTimeline)]);

            ICamera view = new Camera(camera, 7, 0, true, FadeTransition, 0, BlackHold, BlackFadeIn);
            AddCamera(view);

            IWorldEntity ship = Player.Map?
                .Search(Player.Position, 300f, new CreatureSearchCheck(ShipCreature))
                .FirstOrDefault();
            if (ship != null)
            {
                Keyframes.Add("ShipSynchronisation",
                [
                    new VisualEffect(SyncHologramVisualEffect, ship.Guid, initialDelay: FadeInAt, duration: SyncDuration)
                ]);
            }

            // the Caretaker's arrival voice, sound only (visual 34515 plays Play_AdventureVO_General_Caretaker_Intro_02,
            // "Do not think that you are impervious to harm, simply because this is a simulation.")
            Keyframes.Add("CaretakerIntro",
            [
                new VisualEffect(CaretakerIntroVoice, Player.Guid, initialDelay: CaretakerIntroAt)
            ]);

            // no subtitles: the intro event script sends the Caretaker's narration after the black screen, as story
            // communicators (a cinematic hides story windows)
        }

        private class CreatureSearchCheck : ISearchCheck<IWorldEntity>
        {
            private readonly uint creatureId;

            public CreatureSearchCheck(uint creatureId)
            {
                this.creatureId = creatureId;
            }

            public bool CheckEntity(IWorldEntity entity)
            {
                return entity.CreatureId == creatureId;
            }
        }
    }
}
