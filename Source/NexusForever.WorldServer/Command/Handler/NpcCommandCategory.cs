using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Chat;
using NexusForever.Game.Static.RBAC;
using NexusForever.GameTable;
using NexusForever.Network.World.Message.Model.Chat;
using NexusForever.Network.World.Message.Model.Shared;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Command.Static;

namespace NexusForever.WorldServer.Command.Handler
{
    /// <summary>
    /// Dev commands on a targeted NPC (the map category only accepts player targets).
    /// </summary>
    [Command(Permission.Map, "Dev commands for a targeted NPC.", "npc")]
    [CommandTarget(typeof(IWorldEntity))]
    public class NpcCommandCategory : CommandCategory
    {
        [Command(Permission.MapUnload, "Make your target say a test line to you on a chat channel number (ChatChannelType, e.g. 20 NPCSay, 21 NPCYell, 22 NPCWhisper, 23 Datachron, 24 Combat, 27 AnimatedEmote, 28 ActionEmote), to find one that shows a speech bubble without a chat line.", "say")]
        public void HandleNpcSay(ICommandContext context,
            [Parameter("Chat channel type number.")]
            uint channel,
            [Parameter("Optional single word to say (default: the channel number).", ParameterFlags.Optional)]
            string text)
        {
            // the invoker hears it, the target speaks
            if (context.Invoker is not IPlayer player)
                return;

            IWorldEntity speaker = context.Target;
            if (speaker == null || speaker == player)
            {
                context.SendError("Target an NPC first.");
                return;
            }

            player.Session.EnqueueMessageEncrypted(new ServerChat
            {
                Channel   = new Channel { ChatChannelId = (ChatChannelType)channel },
                From      = new Identity(),
                FromName  = speaker.CreatureInfo != null
                    ? GameTableManager.Instance.TextEnglish.GetEntry(speaker.CreatureInfo.Entry.LocalizedTextIdName) ?? "NPC"
                    : "NPC",
                FromRealm = string.Empty,
                Text      = string.IsNullOrEmpty(text) ? $"Testing channel {channel} ({(ChatChannelType)channel})" : $"{text} (channel {channel})",
                UnitId    = speaker.Guid
            });
            context.SendMessage($"Sent a line from {speaker.Guid} on channel {channel} ({(ChatChannelType)channel}).");
        }
    }
}
