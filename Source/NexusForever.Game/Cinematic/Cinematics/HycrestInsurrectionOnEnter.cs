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
    /// Not built from packet captures. The retail intro cinematic (GC217) also has a flying ship and Dominion actors whose
    /// origin and timings are unknown; this uses its camera creature and subtitle texts (683169-683172). Subtitles are only
    /// shown when cinematic subtitles are enabled in the client options.
    /// </remarks>
    public class HycrestInsurrectionOnEnter : CinematicBase, IHycrestInsurrectionOnEnter
    {
        private const uint ActorCamera = 70555u; // GC217 - Hycrest Adventure Intro - Camera

        // the players' arrival spot inside the intro ship (HycrestShipLayout.PlayerSpots, measured), eye height; the
        // camera looks from here when the black screen fades out, where the player stands by then
        private static readonly Vector3 CameraPosition = new(-2516.7417f, -872.3f, -1233.5726f);
        private const float CameraAngle = 0.6482017f;

        // black at once, held under the narration, faded out as it ends (start, hold and end durations in ms)
        private const ushort BlackHold    = 18000;
        private const ushort BlackFadeOut = 1500;

        // Transimulator Synchronization (spell 62968) visuals on the player as the black screen fades: green hologram
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

            StartTransition = new Transition(0, 1, 2, 0, BlackHold, BlackFadeOut);
            EndTransition   = new Transition(Duration - BlackFadeOut, 0, 0);

            IActor camera = new Actor(ActorCamera, 6, CameraAngle, new Position(CameraPosition));
            AddActor(camera, new List<IVisualEffect>());
            AddCamera(new Camera(camera, 7, 0, true, 0, 0, BlackHold, BlackFadeOut));

            AddText(683169, 500, 4500);
            AddText(683170, 4700, 8700);
            AddText(683171, 8900, 12700);
            AddText(683172, 12900, 17500);

            Keyframes.Add("Synchronisation", new List<IKeyframeAction>
            {
                new VisualEffect(SyncHologramVisualEffect, Player.Guid, initialDelay: BlackHold, duration: SyncDuration),
                new VisualEffect(SyncTeleportVisualEffect, Player.Guid, initialDelay: BlackHold, duration: SyncDuration)
            });
        }
    }
}
