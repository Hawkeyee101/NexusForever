using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game;
using NexusForever.Game.Abstract.Cinematic;
using NexusForever.Game.Abstract.Cinematic.Cinematics;
using NexusForever.Game.Cinematic.Cinematics;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static;
using NexusForever.Game.Static.RBAC;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Shared;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Command.Convert;
using NexusForever.WorldServer.Command.Static;

namespace NexusForever.WorldServer.Command.Handler
{
    [Command(Permission.Story, "A collection of commands to send story content to characters.", "story")]
    [CommandTarget(typeof(IPlayer))]
    public class StoryCommandCategory : CommandCategory
    {
        // local dev aid (not for upstream): find cinematic flags that keep story text visible over the Hycrest black screen
        [Command(Permission.StoryCommunicator, "Dev: play the Hycrest arrival black screen with the given cinematic flags, plus the first narration story text.", "hycrestintro")]
        public void HandleHycrestIntro(ICommandContext context,
            [Parameter("Cinematic initial flags (retail-like cinematics use 7).")]
            uint flags,
            [Parameter("Optional cancel mode (0 = can't skip, 2 = Esc to skip).")]
            uint? cancelMode)
        {
            IPlayer player = context.GetTargetOrInvoker<IPlayer>();

            HycrestInsurrectionOnEnter.FlagsOverride      = (ushort)flags;
            HycrestInsurrectionOnEnter.CancelModeOverride = (ushort)(cancelMode ?? 0u);

            // TODO: replace with dependency injection once commands system is refactored
            var cinematicFactory = LegacyServiceProvider.Provider.GetService<ICinematicFactory>();
            player.CinematicManager.QueueCinematic(cinematicFactory.CreateCinematic<IHycrestInsurrectionOnEnter>());

            StoryBuilder.Instance.SendStoryCommunicator(534606, 53309, player, 8000, StoryPanelType.Default, (WindowType)2);

            HycrestInsurrectionOnEnter.FlagsOverride      = null;
            HycrestInsurrectionOnEnter.CancelModeOverride = null;
            context.SendMessage($"Played the Hycrest arrival black screen with flags {flags}, cancel mode {cancelMode ?? 0u}.");
        }

        [Command(Permission.StoryPanel, "Send a story panel to a character.", "panel", "p")]
        public void HandleStoryPanel(ICommandContext context,
            [Parameter("Story panel entry to send to character.")]
            uint storyPanelId)
        {
            StoryPanelEntry entry = GameTableManager.Instance.StoryPanel.GetEntry(storyPanelId);
            if (entry == null)
            {
                context.SendError($"Invalid story panel entry {storyPanelId}!");
                return;
            }

            StoryBuilder.Instance.SendStoryPanel(entry, context.GetTargetOrInvoker<IPlayer>());
        }

        [Command(Permission.StoryCommunicator, "Send a story communicator window to a character.", "communicator", "c")]
        public void TestSubCommand(ICommandContext context,
            [Parameter("")]
            uint textId,
            [Parameter("")]
            uint creatureId,
            [Parameter("")]
            uint? duration,
            [Parameter("", ParameterFlags.None, typeof(EnumParameterConverter<StoryPanelType>))]
            StoryPanelType? storyPanelType,
            [Parameter("", ParameterFlags.None, typeof(EnumParameterConverter<WindowType>))]
            WindowType? windowType,
            [Parameter("")]
            uint? soundEvent,
            [Parameter("")]
            byte? priority)
        {
            duration       ??= 10000u;
            storyPanelType ??= StoryPanelType.Default;
            windowType     ??= WindowType.LeftAligned;
            soundEvent     ??= 0u;
            priority       ??= 0;

            StoryBuilder.Instance.SendStoryCommunicator(textId, creatureId, context.GetTargetOrInvoker<IPlayer>(),
                duration.Value, storyPanelType.Value, windowType.Value, soundEvent.Value, priority.Value);
        }
    }
}
