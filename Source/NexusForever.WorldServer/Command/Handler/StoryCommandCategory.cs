using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static;
using NexusForever.Game.Static.RBAC;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Shared;
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
        // local dev aid (not for upstream): identify voice lines (SoundEvent.tbl ids) by ear
        [Command(Permission.StoryCommunicator, "Dev: play a sound event (voice line). Mode 0 communicator with text, 1 communicator without text, 2 hidden story panel.", "sound")]
        public void HandleSound(ICommandContext context,
            [Parameter("SoundEvent id to play.")]
            uint soundEventId,
            [Parameter("Optional mode: 0 communicator with text (default), 1 communicator without text, 2 story panel hidden at once.")]
            uint? mode)
        {
            IPlayer player = context.GetTargetOrInvoker<IPlayer>();
            switch (mode ?? 0u)
            {
                case 1u:
                {
                    var storyMessage = new StoryMessage
                    {
                        GeneralVoId = soundEventId
                    };
                    player.Session.EnqueueMessageEncrypted(new ServerStoryCommunicatorShow
                    {
                        StoryMessage = storyMessage,
                        SoundEventId = 53309u,
                        DurationMs   = 1u
                    });
                    break;
                }
                case 2u:
                {
                    var storyMessage = new StoryMessage
                    {
                        GeneralVoId = soundEventId
                    };
                    player.Session.EnqueueMessageEncrypted(new ServerStoryPanelShow
                    {
                        StoryMessage = storyMessage
                    });
                    player.Session.EnqueueMessageEncrypted(new ServerStoryPanelHide());
                    break;
                }
                default:
                    StoryBuilder.Instance.SendStoryCommunicator(515639, 53309u, player, 3000, voiceSoundEventId: soundEventId);
                    break;
            }

            context.SendMessage($"Played sound event {soundEventId} (mode {mode ?? 0u}).");
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
