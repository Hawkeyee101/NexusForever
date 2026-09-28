using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Database;
using NexusForever.Database.World;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Abstract.PublicEvent;

namespace NexusForever.Game.PublicEvent
{
    public class PublicEventEntityFactory : IPublicEventEntityFactory
    {
        private IPublicEvent publicEvent;

        private Dictionary<uint, List<EntityModel>> entityModels;
        private readonly HashSet<IGridEntity> entities = [];

        #region Dependency Injection

        private readonly ILogger<PublicEventEntityFactory> log;

        private readonly IDatabaseManager databaseManager;
        private readonly IEntityFactory entityFactory;
        private readonly ICreatureInfoManager creatureInfoManager;

        public PublicEventEntityFactory(
            ILogger<PublicEventEntityFactory> log,
            IDatabaseManager databaseManager,
            IEntityFactory entityFactory,
            ICreatureInfoManager creatureInfoManager)
        {
            this.log             = log;
            this.databaseManager = databaseManager;
            this.entityFactory   = entityFactory;
            this.creatureInfoManager = creatureInfoManager;
        }

        #endregion

        /// <summary>
        /// Initalise the <see cref="PublicEventEntityFactory"/> with the specified <see cref="IPublicEvent"/>.
        /// </summary>
        public void Initialise(IPublicEvent publicEvent)
        {
            if (this.publicEvent != null)
                throw new InvalidOperationException();

            this.publicEvent = publicEvent;

            entityModels = databaseManager.GetDatabase<WorldDatabase>()
                .GetEntitiesPublicEvent(publicEvent.Id)
                .GroupBy(e => e.EntityEvent.Phase)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>
        /// Spawn entities for the specified <see cref="IPublicEvent"/> phase.
        /// </summary>
        public void SpawnEntities(uint phase)
        {
            if (!entityModels.TryGetValue(phase, out List<EntityModel> models))
                return;

            foreach (EntityModel model in models)
            {
                ICreatureInfo creatureInfo = creatureInfoManager.GetCreatureInfo(model.Creature);
                if (creatureInfo == null)
                    continue;

                IWorldEntity entity = entityFactory.CreateWorldEntity(model.Type);
                entity.Initialise(creatureInfo, model);
                entity.Rotation = new Vector3(model.Rx, model.Ry, model.Rz);
                entity.AddToMap(publicEvent.Map, new Vector3(model.X, model.Y, model.Z));

                entities.Add(entity);
            }

            log.LogTrace($"Spawned entities for public event {publicEvent.Id} phase {phase}.");
        }

        /// <summary>
        /// Remove all entities spawned for the <see cref="IPublicEvent"/>.
        /// </summary>
        public void RemoveEntities()
        {
            foreach (IGridEntity entity in entities)
                if (entity.InWorld)
                    entity.RemoveFromMap();

            entities.Clear();

            log.LogTrace($"Removed entities from public event {publicEvent.Id}.");
        }

        /// <summary>
        /// Create a new <see cref="IGridEntity"/> that belongs to the <see cref="IPublicEvent"/>.
        /// </summary>
        public T CreateEntity<T>() where T : IGridEntity
        {
            T entity = entityFactory.CreateEntity<T>();
            entities.Add(entity);
            return entity;
        }

        /// <summary>
        /// Return all entities spawned or created for the <see cref="IPublicEvent"/>.
        /// </summary>
        public IEnumerable<IGridEntity> GetEntities()
        {
            return entities;
        }

        /// <summary>
        /// Return true if the <see cref="IGridEntity"/> was spawned or created for the <see cref="IPublicEvent"/>.
        /// </summary>
        public bool Contains(IGridEntity entity)
        {
            return entities.Contains(entity);
        }
    }
}
