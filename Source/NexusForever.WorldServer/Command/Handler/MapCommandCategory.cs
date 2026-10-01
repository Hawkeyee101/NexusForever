using System.Linq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.Map.Lock;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.Shared;
using NexusForever.Shared.Configuration;
using NexusForever.Game.Configuration.Model;
using NexusForever.Game.Map;
using NexusForever.Game.Static.Map;
using NexusForever.Game.Static.RBAC;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Command.Convert;

using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Chat;
using NexusForever.Network.World.Message.Model.Shared;
using NexusForever.Game.Static.Chat;
using NexusForever.GameTable;
using NexusForever.Network.World.Message.Model.Map;
using NexusForever.Network.World.Message.Model.PublicEvent;

namespace NexusForever.WorldServer.Command.Handler
{
    [Command(Permission.Map, "A collection of commands to manage maps.", "map")]
    [CommandTarget(typeof(IPlayer))]
    public class MapCommandCategory : CommandCategory
    {
        [Command(Permission.MapUnload, "Unload current map instance.", "unload")]
        public void HandleMapUnload(ICommandContext context)
        {
            IPlayer player = context.GetTargetOrInvoker<IPlayer>();
            if (player.Map is not IMapInstance instance)
            {
                context.SendError("Current map is not an instance!");
                return;
            }

            instance.Unload();
        }

        // local dev aid (not for upstream): retest content that runs once per instance, such as the Hycrest intro
        [Command(Permission.MapUnload, "Move into a fresh instance of the current world (drops your solo lock for it).", "fresh")]
        public void HandleMapFresh(ICommandContext context)
        {
            IPlayer player = context.GetTargetOrInvoker<IPlayer>();
            if (player.Map is not IMapInstance)
            {
                context.SendError("Current map is not an instance!");
                return;
            }

            ushort worldId = (ushort)player.Map.Entry.Id;

            // TODO: replace with dependency injection once commands system is refactored
            var mapLockManager = LegacyServiceProvider.Provider.GetService<IMapLockManager>();
            mapLockManager.RemoveSoloLock(player.Identity, worldId);

            player.TeleportTo(worldId, player.Position.X, player.Position.Y, player.Position.Z);
            context.SendMessage($"Moving into a fresh instance of world {worldId}. The old instance is left empty.");
        }

        // local dev aids (not for upstream yet): inspect and drive public events, e.g. to skip missions that aren't built
        [Command(Permission.MapUnload, "List the public events on the current map with their phase and active objectives.", "events")]
        public void HandleMapEvents(ICommandContext context)
        {
            IPlayer player = context.GetTargetOrInvoker<IPlayer>();
            foreach (IPublicEvent publicEvent in player.Map.PublicEventManager.GetEvents())
            {
                string objectives = string.Join(", ", publicEvent.GetTeams()
                    .SelectMany(t => t.GetObjectives())
                    .Where(o => o.Status == PublicEventStatus.Active)
                    .Select(o => $"{o.Entry.Id} {o.Count}/{(o.DynamicMax > 0 ? o.DynamicMax : o.Entry.Count)}"));

                context.SendMessage($"Event {publicEvent.Id}: phase {publicEvent.Phase}{(publicEvent.HasFinished ? ", finished" : "")}; active: {(objectives.Length > 0 ? objectives : "none")}");
            }
        }

        [Command(Permission.MapUnload, "Finish a public event on the current map as a success.", "eventfinish")]
        public void HandleMapEventFinish(ICommandContext context,
            [Parameter("Public event id.")]
            uint eventId)
        {
            IPlayer player = context.GetTargetOrInvoker<IPlayer>();
            IPublicEvent publicEvent = player.Map.PublicEventManager.GetEvent(eventId);
            if (publicEvent == null || publicEvent.HasFinished)
            {
                context.SendError($"Public event {eventId} isn't running on this map!");
                return;
            }

            publicEvent.Finish(PublicEventTeam.PublicTeam);
            context.SendMessage($"Finished public event {eventId}.");
        }

        [Command(Permission.MapUnload, "Update an objective of a public event on the current map.", "eventobjective")]
        public void HandleMapEventObjective(ICommandContext context,
            [Parameter("Public event id.")]
            uint eventId,
            [Parameter("Objective id.")]
            uint objectiveId,
            [Parameter("Optional count to add (default 1).")]
            uint? count)
        {
            IPlayer player = context.GetTargetOrInvoker<IPlayer>();
            IPublicEvent publicEvent = player.Map.PublicEventManager.GetEvent(eventId);
            if (publicEvent == null || publicEvent.HasFinished)
            {
                context.SendError($"Public event {eventId} isn't running on this map!");
                return;
            }

            publicEvent.UpdateObjective(objectiveId, (int)(count ?? 1u));
            context.SendMessage($"Updated objective {objectiveId} of public event {eventId}.");
        }

        [Command(Permission.MapUnload, "Set the phase of a public event on the current map (spawns the phase's entities, scripts hear it).", "eventphase")]
        public void HandleMapEventPhase(ICommandContext context,
            [Parameter("Public event id.")]
            uint eventId,
            [Parameter("Phase.")]
            uint phase)
        {
            IPublicEvent publicEvent = GetRunningEvent(context, eventId);
            if (publicEvent == null)
                return;

            publicEvent.SetPhase(phase);
            context.SendMessage($"Public event {eventId} set to phase {phase}.");
        }

        [Command(Permission.MapUnload, "Show a WorldLocation2 marker for an objective of a public event on the current map (0 clears).", "eventlocation")]
        public void HandleMapEventLocation(ICommandContext context,
            [Parameter("Public event id.")]
            uint eventId,
            [Parameter("Objective id.")]
            uint objectiveId,
            [Parameter("WorldLocation2 id, 0 clears.")]
            uint worldLocation2Id)
        {
            IPublicEvent publicEvent = GetRunningEvent(context, eventId);
            if (publicEvent == null)
                return;

            publicEvent.SetObjectiveLocations(objectiveId, worldLocation2Id == 0u ? [] : [worldLocation2Id]);
            context.SendMessage($"Set location {worldLocation2Id} on objective {objectiveId} of public event {eventId}.");
        }

        [Command(Permission.MapUnload, "Dev: send a raw public event location update to yourself. Operation 0 add to event, 1 remove from event, 2 add to objective, 3 remove from objective.", "locationupdate")]
        public void HandleMapLocationUpdate(ICommandContext context,
            [Parameter("Public event id (operations 0/1) or objective id (operations 2/3).")]
            uint objectId,
            [Parameter("Operation: 0 AddToEvent, 1 RemoveFromEvent, 2 AddToObjective, 3 RemoveFromObjective.")]
            uint operation,
            [Parameter("WorldLocation2 id.")]
            uint worldLocation2Id,
            [Parameter("Optional width of the id in bits (default 32, the confirmed one).")]
            uint? bits)
        {
            IPlayer player = context.GetTargetOrInvoker<IPlayer>();
            player.Session.EnqueueMessageEncrypted(new ServerPublicEventLocationUpdate
            {
                ObjectId         = objectId,
                Operation        = (PublicEventOperationType)operation,
                WorldLocation2Id = worldLocation2Id,
                ObjectIdBits     = bits ?? 32u
            });
            context.SendMessage($"Sent location update: {(PublicEventOperationType)operation} {objectId}, location {worldLocation2Id}, id {bits ?? 32u} bits.");
        }

        [Command(Permission.MapUnload, "Dev: show or hide a map hex group (MapZoneHexGroup) on your map, e.g. the Hycrest groups 10-20.", "hexgroup")]
        public void HandleMapHexGroup(ICommandContext context,
            [Parameter("MapZoneHexGroup id.")]
            uint hexGroupId,
            [Parameter("1 shows it (default), 0 hides it.")]
            uint? visible,
            [Parameter("Colour as a number (default 0xFF00FF00, green; format not known yet).")]
            uint? color,
            [Parameter("Optional tooltip text id.")]
            uint? tooltipTextId)
        {
            IPlayer player = context.GetTargetOrInvoker<IPlayer>();
            player.Session.EnqueueMessageEncrypted(new ServerMapUpdateHexGroup
            {
                MapZoneHexGroupId      = hexGroupId,
                TooltipLocalizedTextId = tooltipTextId ?? 0u,
                Color                  = color ?? 0xFF00FF00u,
                IsVisible              = (visible ?? 1u) != 0u
            });
            context.SendMessage($"Sent hex group {hexGroupId} (visible {(visible ?? 1u) != 0u}, colour 0x{color ?? 0xFF00FF00u:X8}).");
        }

        [Command(Permission.MapUnload, "Dev: send yourself a time of day (e.g. to see how a sky looks in the morning). The clock runs on from there at the realm's day length.", "timeofday")]
        public void HandleMapTimeOfDay(ICommandContext context,
            [Parameter("Hour (0-23).")]
            uint hour,
            [Parameter("Minute (0-59, default 0).")]
            uint? minute)
        {
            IPlayer player = context.GetTargetOrInvoker<IPlayer>();
            uint seconds = (hour % 24u) * 3600u + (minute ?? 0u) % 60u * 60u;
            player.Session.EnqueueMessageEncrypted(new ServerTimeOfDay
            {
                TimeOfDay   = seconds,
                // the realm's day length, as sent at login: with a 30 day length every hour showed as night
                LengthOfDay = SharedConfiguration.Instance.Get<RealmConfig>().LengthOfInGameDay is > 0u and uint length ? length : 12600u
            });
            context.SendMessage($"Sent time of day {hour % 24u:00}:{(minute ?? 0u) % 60u:00}.");
        }

        private static IPublicEvent GetRunningEvent(ICommandContext context, uint eventId)
        {
            IPlayer player = context.GetTargetOrInvoker<IPlayer>();
            IPublicEvent publicEvent = player.Map.PublicEventManager.GetEvent(eventId);
            if (publicEvent == null || publicEvent.HasFinished)
            {
                context.SendError($"Public event {eventId} isn't running on this map!");
                return null;
            }

            return publicEvent;
        }

        [Command(Permission.MapPlayerRemove, "Remove player from current map instance.", "remove")]
        public void HandleMapPlayerRemove(ICommandContext context,
            [Parameter("Removal reason.", converter: typeof(EnumParameterConverter<WorldRemovalReason>))]
            WorldRemovalReason removalReason)
        {
            IPlayer player = context.GetTargetOrInvoker<IPlayer>();
            if (player.Map is not IMapInstance instance)
            {
                context.SendError("Current map is not an instance!");
                return;
            }

            instance.EnqueuePendingRemoval(player, removalReason);
        }

        [Command(Permission.MapPlayerRemoveCancel, "Cancel removal of player from current map instance.", "cancel")]
        public void HandleMapPlayerRemoveCancel(ICommandContext context)
        {
            IPlayer player = context.GetTargetOrInvoker<IPlayer>();
            if (player.Map is not IMapInstance instance)
            {
                context.SendError("Current map is not an instance!");
                return;
            }

            instance.CancelPendingRemoval(player);
        }
    }
}
