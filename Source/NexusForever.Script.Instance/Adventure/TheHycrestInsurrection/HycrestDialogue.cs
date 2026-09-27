using System.Text.RegularExpressions;
using NexusForever.Game.Abstract.Entity;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    /// <summary>
    /// Plays scripted NPC lines from en-US.bin as NPC say (speech bubble and chat).
    /// </summary>
    /// <remarks>
    /// There is no chat message that carries a text id, so lines are resolved to English text on the server.
    /// </remarks>
    public partial class HycrestDialogue
    {
        // talking gesture used by Vesna's and Lysion's lines in en-US.bin ($(self.visual=5701)), 4 s, model sequence 278
        public const uint TalkGestureVisualEffect = 5701u;

        [GeneratedRegex(@"\$\(self\.visual=(\d+)\)")]
        private static partial Regex SelfVisualRegex();

        [GeneratedRegex(@"\$[pm]?\(creature=(\d+)\)")]
        private static partial Regex CreatureRegex();

        private static uint visualHandle = 0x48590000u;

        private readonly IGameTableManager gameTableManager;

        public HycrestDialogue(IGameTableManager gameTableManager)
        {
            this.gameTableManager = gameTableManager;
        }

        /// <summary>
        /// Resolve a text id to displayable English text: creature references are replaced by names, visual tags removed.
        /// </summary>
        public string GetText(uint textId)
        {
            string text = gameTableManager.TextEnglish.GetEntry(textId) ?? string.Empty;
            text = SelfVisualRegex().Replace(text, string.Empty);
            text = CreatureRegex().Replace(text, m =>
            {
                Creature2Entry entry = gameTableManager.Creature2.GetEntry(uint.Parse(m.Groups[1].Value));
                return entry != null ? gameTableManager.TextEnglish.GetEntry(entry.LocalizedTextIdName) ?? m.Value : m.Value;
            });

            return text.Trim();
        }

        /// <summary>
        /// Let <paramref name="speaker"/> say the line with <paramref name="textId"/>, optionally with the talking gesture.
        /// </summary>
        /// <remarks>
        /// When <paramref name="gesture"/> is null the gesture is played if the line has a $(self.visual=...) tag.
        /// </remarks>
        public void Say(IWorldEntity speaker, uint textId, bool? gesture = null)
        {
            if (speaker?.Map == null)
                return;

            string raw = gameTableManager.TextEnglish.GetEntry(textId) ?? string.Empty;
            speaker.NpcSay(GetText(textId));

            if (gesture ?? SelfVisualRegex().IsMatch(raw))
                PlayVisualEffect(speaker, TalkGestureVisualEffect);
        }

        /// <summary>
        /// Play a visual effect on <paramref name="entity"/> for every player that can see it.
        /// </summary>
        public static uint PlayVisualEffect(IWorldEntity entity, uint visualEffectId)
        {
            uint handle = Interlocked.Increment(ref visualHandle);
            entity.EnqueueToVisible(new ServerCinematicVisualEffect
            {
                VisualHandle      = handle,
                VisualEffectId    = visualEffectId,
                UnitId            = entity.Guid,
                Position          = new Position(entity.Position),
                RemoveOnCameraEnd = false
            }, true);

            return handle;
        }

        /// <summary>
        /// End a visual effect started with <see cref="PlayVisualEffect"/> for every player that can see <paramref name="entity"/>.
        /// </summary>
        public static void EndVisualEffect(IWorldEntity entity, uint handle)
        {
            entity.EnqueueToVisible(new ServerCinematicVisualEffectEnd
            {
                VisualHandle = handle
            }, true);
        }
    }
}
