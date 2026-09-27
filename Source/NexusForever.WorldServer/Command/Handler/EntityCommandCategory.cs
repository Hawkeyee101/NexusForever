using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Abstract.Map.Search;
using NexusForever.Game.Static;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.RBAC;
using NexusForever.Game.Static.Reputation;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Message.Model;
using NexusForever.Shared;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Command.Convert;
using NexusForever.WorldServer.Command.Shared;

namespace NexusForever.WorldServer.Command.Handler
{
    [Command(Permission.Entity, "A collection of commands to modify and query information about an entity.", "entity")]
    [CommandTarget(typeof(IWorldEntity))]
    public class EntityCommandCategory : CommandCategory
    {
        [Command(Permission.EntityModify, "A collection of commands to modify an entity.", "modify")]
        public class EntityModifyCommandCategory : CommandCategory
        {
            [Command(Permission.EntityModifyDisplayInfo, "Modify the display info of the target entity.", "displayinfo")]
            public void HandleEntityModifyDisplayInfo(ICommandContext context,
                uint displayInfo)
            {
                Creature2DisplayInfoEntry entry = GameTableManager.Instance.Creature2DisplayInfo.GetEntry(displayInfo);
                if (displayInfo != 0 && entry == null)
                {
                    context.SendMessage($"Invalid display info id {displayInfo}!");
                    return;
                }

                context.GetTargetOrInvoker<IWorldEntity>().CreatureDisplayEntry = entry;
            }

            private static uint visualHandle = 0x47500000u;

            // local dev aid (not for upstream): play a visual effect (e.g. a model sequence) on an entity that can't be
            // targeted, such as a platform the player stands on
            [Command(Permission.EntityModify, "Play a visual effect on the target, or on the nearest entity with the given creature id.", "visual")]
            public void HandleEntityModifyVisual(ICommandContext context,
                [Parameter("VisualEffect id to play.")]
                uint visualEffectId,
                [Parameter("Optional creature id: play on the nearest entity with this creature id instead of the target.")]
                uint? creatureId)
            {
                if (GameTableManager.Instance.VisualEffect.GetEntry(visualEffectId) == null)
                {
                    context.SendMessage($"Invalid visual effect id {visualEffectId}!");
                    return;
                }

                IWorldEntity entity;
                if (creatureId.HasValue)
                {
                    IWorldEntity invoker = context.Invoker;
                    entity = invoker.Map
                        .Search(invoker.Position, 300f, new CreatureSearchCheck(creatureId.Value))
                        .OrderBy(e => Vector3.DistanceSquared(e.Position, invoker.Position))
                        .FirstOrDefault();
                    if (entity == null)
                    {
                        context.SendMessage($"No entity with creature id {creatureId.Value} within 300 m!");
                        return;
                    }
                }
                else
                    entity = context.GetTargetOrInvoker<IWorldEntity>();

                entity.EnqueueToVisible(new ServerCinematicVisualEffect
                {
                    VisualHandle      = Interlocked.Increment(ref visualHandle),
                    VisualEffectId    = visualEffectId,
                    UnitId            = entity.Guid,
                    Position          = new Position(entity.Position),
                    RemoveOnCameraEnd = false
                }, true);

                context.SendMessage($"Played visual effect {visualEffectId} on entity {entity.Guid} (creature {entity.CreatureId}).");
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

        [Command(Permission.EntitySummon, "Summon an entity.", "sum", "summon")]
        public void HandleEntityCreate(
            ICommandContext context,
            [Parameter("CreatureId of the entity to summon.")]
            uint creatureId,
            [Parameter("Temporary entities will be removed when the summoner is removed from map. Defaults to true.")]
            bool? temporary,
            [Parameter("Entity type of the entity to summon. Defaults to game table value if not supplied.", converter: typeof(EnumParameterConverter<EntityType>))]
            EntityType? entityType)
        {
            // TODO: replace with dependency injection once commands system is refactored
            var creatureInfoManager = LegacyServiceProvider.Provider.GetService<ICreatureInfoManager>();
            var entityFactory = LegacyServiceProvider.Provider.GetService<IEntityFactory>();

            ICreatureInfo creatureInfo = creatureInfoManager.GetCreatureInfo(creatureId);
            if (creatureInfo == null)
            {
                context.SendMessage($"Invalid creature id {creatureId}!");
                return;
            }

            if (temporary ?? true)
                context.Invoker.SummonFactory.Summon(creatureInfo, entityType ?? creatureInfo.Entry.CreationTypeEnum, context.Invoker.Position, context.Invoker.Rotation);
            else
            {
                IWorldEntity entity = entityFactory.CreateWorldEntity(entityType ?? creatureInfo.Entry.CreationTypeEnum);
                entity.Initialise(creatureInfo);
                entity.AddToMap(context.Invoker.Map, context.Invoker.Position);
            }
        }

        [Command(Permission.EntityInfo, "Get information about the target entity.", "i", "info")]
        public void HandleEntityInfo(ICommandContext context)
        {
            IWorldEntity entity = context.GetTargetOrInvoker<IWorldEntity>();

            var builder = new StringBuilder();
            EntityUtility.BuildHeader(builder, entity, context.Language);

            builder.AppendLine($"XYZ: {entity.Position.X}, {entity.Position.Y}, {entity.Position.Z}");
            builder.AppendLine($"Rotation: {entity.Rotation.X}, {entity.Rotation.Y}, {entity.Rotation.Z}");
            builder.AppendLine($"HP: {entity.Health}/({entity.MaxHealth}) | Shield: {entity.Shield}/({entity.MaxShieldCapacity})");

            Disposition faction1Disposition = context.Invoker.GetDispositionTo(entity.Faction1);
            Disposition faction2Disposition = context.Invoker.GetDispositionTo(entity.Faction2);
            builder.AppendLine($"Disposition To Me: {entity.Faction1},{faction1Disposition}, {entity.Faction2},{faction2Disposition}");

            context.SendMessage(builder.ToString());
        }

        [Command(Permission.EntityProperties, "Get information about the properties for the target entity.", "p", "properties")]
        public void HandleEntityProperties(ICommandContext context)
        {
            IWorldEntity entity = context.GetTargetOrInvoker<IWorldEntity>();

            var builder = new StringBuilder();
            EntityUtility.BuildHeader(builder, entity, context.Language);

            var properties = entity.GetProperties().ToList();
            if (properties.Count == 0)
                builder.AppendLine("No properties found!");
            else
            {
                foreach (IPropertyValue value in properties.OrderBy(p => p.Property))
                    builder.AppendLine($"{value.Property} - Base: {value.BaseValue} - Value: {value.Value}");
            }

            context.SendMessage(builder.ToString());
        }

        [Command(Permission.EntityProperties, "Get information about the vitals for the target entity.", "v", "vitals")]
        public void HandleEntityVitals(ICommandContext context)
        {
            IWorldEntity entity = context.GetTargetOrInvoker<IWorldEntity>();

            var builder = new StringBuilder();
            EntityUtility.BuildHeader(builder, entity, context.Language);

            foreach (VitalEntry vital in GameTableManager.Instance.Vital.Entries)
                builder.AppendLine($"{(Vital)vital.Id} - {entity.GetVitalValue((Vital)vital.Id)}");

            context.SendMessage(builder.ToString());
        }

        [Command(Permission.EntityThreat, "A collection of commands to modify threat for this entity.", "threat")]
        public class EntityThreatCommandCategory : EntityCommandCategory
        {
            [Command(Permission.EntityThreatAdjust, "Adjust threat between the target and yourself.", "a", "adjust")]
            public void HandleEntityThreatAdjust(ICommandContext context,
                [Parameter("Amount to adjust by. This can be a negative or positive number.")]
                int threat)
            {
                IWorldEntity entity = context.GetTargetOrInvoker<IWorldEntity>();
                if (entity == context.Invoker)
                {
                    context.SendMessage($"You must have a target other than yourself.");
                    return;
                }

                if (!(entity is IUnitEntity unit))
                {
                    context.SendMessage($"Only entities of a Unit type can have threat.");
                    return;
                }

                unit.ThreatManager.UpdateThreat(context.Invoker as IUnitEntity, threat);
            }

            [Command(Permission.EntityThreatList, "Prints the target's threat list.", "l", "list")]
            public void HandleEntityThreatList(ICommandContext context)
            {
                IWorldEntity entity = context.GetTargetOrInvoker<IWorldEntity>();

                if (!(entity is IUnitEntity unit))
                {
                    context.SendMessage($"Only entities of a Unit type can have threat.");
                    return;
                }

                var builder = new StringBuilder();
                EntityUtility.BuildHeader(builder, unit, context.Language);
                builder.AppendLine("=============================");
                builder.AppendLine("# | GUID | Name | Threat");
                builder.AppendLine("-----------------------------");

                int i = 1;
                List<IHostileEntity> hostiles = unit.ThreatManager.ToList();
                if (hostiles.Count == 0u)
                    builder.AppendLine("No threat targets.");

                foreach (IHostileEntity hostile in hostiles)
                {
                    IUnitEntity hostileEntity = entity.GetVisible<IUnitEntity>(hostile.HatedUnitId);
                    builder.AppendLine($"{i++} | ({hostileEntity.Guid}) | {EntityUtility.GetName(hostileEntity, Language.English)} | {hostile.Threat}");
                }

                context.SendMessage(builder.ToString());
            }

            [Command(Permission.EntityThreatClear, "Clears the target's threat list.", "c", "clear")]
            public void HandleEntityThreatClear(ICommandContext context)
            {
                IWorldEntity entity = context.GetTargetOrInvoker<IWorldEntity>();

                if (!(entity is IUnitEntity unit))
                {
                    context.SendMessage($"Only entities of a Unit type can have threat.");
                    return;
                }

                unit.ThreatManager.ClearThreatList();
            }

            [Command(Permission.EntityThreatRemove, "Remove an entity from the target's threat list.", "r", "remove")]
            public void HandleEntityThreatRemove(ICommandContext context,
                [Parameter("UnitId of the Entity to remove from the Target. Ommitting this defaults to you being the target.")]
                uint? unitId)
            {
                IWorldEntity entity = context.GetTargetOrInvoker<IWorldEntity>();

                if (!(entity is IUnitEntity unit))
                {
                    context.SendMessage($"Only entities of a Unit type can have threat.");
                    return;
                }

                unitId ??= context.Invoker.Guid;

                if (unit.Guid == unitId)
                {
                    if (unit.Guid == context.Invoker.Guid)
                        context.SendMessage($"You must have a target other than yourself.");
                    else
                        context.SendMessage($"You cannot remove a target from its own threat list.");
                    return;
                }

                unit.ThreatManager.RemoveHostile((uint)unitId);
            }
        }
    }
}
