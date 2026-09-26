using NexusForever.Game.Abstract.Cinematic.Cinematics;

namespace NexusForever.Game.Cinematic.Cinematics
{
    /// <summary>
    /// Text-only intro for The Hycrest Insurrection: the Caretaker's four narration lines over a faded screen.
    /// </summary>
    /// <remarks>
    /// Not built from packet captures. The retail intro cinematic (GC217) also has a camera, a flying ship and
    /// Dominion actors whose origin and timings are unknown; this only uses its subtitle texts (683169-683172).
    /// </remarks>
    public class HycrestInsurrectionOnEnter : CinematicBase, IHycrestInsurrectionOnEnter
    {
        protected override void Setup()
        {
            Duration          = 20000;
            InitialFlags      = 7;
            InitialCancelMode = 2;
            CinematicId       = 0;

            StartTransition = new Transition(0, 1, 2, 1500, 0, 1500);
            EndTransition   = new Transition(18500, 0, 0);

            AddText(683169, 1500, 5500);
            AddText(683170, 5700, 9700);
            AddText(683171, 9900, 13500);
            AddText(683172, 13700, 18000);
        }
    }
}
