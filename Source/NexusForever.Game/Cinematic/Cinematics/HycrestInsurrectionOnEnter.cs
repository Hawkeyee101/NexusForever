using System.Numerics;
using NexusForever.Game.Abstract.Cinematic;
using NexusForever.Game.Abstract.Cinematic.Cinematics;
using NexusForever.Network.World.Entity;

namespace NexusForever.Game.Cinematic.Cinematics
{
    /// <summary>
    /// Arrival in The Hycrest Insurrection: the Caretaker's four narration lines on a black screen, which also hides the
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

        // GC217 set origin: where the intro set ship (70557) used to spawn, identity rotation; only the final fade-in
        // shows the camera's view
        private static readonly Vector3 SetOrigin = new(-2520.6f, -873.6975f, -1240f);
        private const float SetAngle = 0f;

        private const uint CinematicTimeline = 45237u; // plays Cinematic_Misc_00 (the whole timeline) on an actor

        // retail: a black screen with the narration typed in. The camera's fade (transition type 3, as between shots in
        // NoviceTutorialOnEnter) goes to black at once, holds under the narration and fades the view in as it ends
        private const uint   FadeInAt       = 17500u;
        private const ushort BlackHold      = 17000;
        private const ushort BlackFadeIn    = 1500;
        private const uint   FadeTransition = 3u;

        // Transimulator Synchronization (spell 62968) visuals on the player as the view fades in: green hologram
        // overlay (3 s) and the green Eldan teleporter effect (3 s)
        private const uint SyncHologramVisualEffect = 20846u;
        private const uint SyncTeleportVisualEffect = 24604u;
        private const uint SyncDuration             = 3000u;

        protected override void Setup()
        {
            Duration          = 20000;
            InitialFlags      = 7;
            InitialCancelMode = 2;
            CinematicId       = 0;

            StartTransition = new Transition(0, 1, 2, 1500, 0, 1500);
            EndTransition   = new Transition(Duration - 1500, 0, 0);

            var origin = new Position(SetOrigin);
            IActor camera = new Actor(ActorCamera, 6, SetAngle, origin);
            AddActor(camera, [new VisualEffect(CinematicTimeline)]);

            ICamera view = new Camera(camera, 7, 0, true, FadeTransition, 0, BlackHold, BlackFadeIn);
            // shot changes without their own transition (a cut could end the black hold early)
            view.AddAttach(6000, 8);
            view.AddAttach(12933, 9);
            AddCamera(view);

            AddText(683169, 500, 4500);
            AddText(683170, 4700, 8700);
            AddText(683171, 8900, 12700);
            AddText(683172, 12900, 17000);

            Keyframes.Add("Synchronisation", new List<IKeyframeAction>
            {
                new VisualEffect(SyncHologramVisualEffect, Player.Guid, initialDelay: FadeInAt, duration: SyncDuration),
                new VisualEffect(SyncTeleportVisualEffect, Player.Guid, initialDelay: FadeInAt, duration: SyncDuration)
            });
        }
    }
}
