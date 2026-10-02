using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement.Command;
using NexusForever.Game.Abstract.Entity.Movement.Command.Position;

namespace NexusForever.Script.Template
{
    public interface IWorldEntityScript : IGridEntityScript
    {
        /// <summary>
        /// Invoked when <see cref="IWorldEntity"/> is successfully activated by <see cref="IPlayer"/>.
        /// </summary>
        void OnActivateSuccess(IPlayer activator)
        {
        }

        /// <summary>
        /// Invoked when <see cref="IWorldEntity"/> is unsuccessfully activated by <see cref="IPlayer"/>.
        /// </summary>
        void OnActivateFail(IPlayer activator)
        {
        }

        /// <summary>
        /// Invoked when the pilot of this vehicle uses a button of the vehicle's action bar: the client sends a spell cast
        /// with the vehicle as the caster and the button's index.
        /// </summary>
        void OnVehicleAbility(IPlayer pilot, ushort index, bool pressed)
        {
        }

        /// <summary>
        /// Invoked when an <see cref="IEntityCommand"/> has finialised for <see cref="IWorldEntity"/>.
        /// </summary>
        void OnEntityCommandFinalise(IEntityCommand command)
        {
        }

        /// <summary>
        /// Invoked when <see cref="IWorldEntity"/> enters a zone.
        /// </summary>
        void OnEnterZone(IWorldEntity entity, uint zone)
        {
        }

        /// <summary>
        /// Invoked when <see cref="IWorldEntity"/> summons another <see cref="IWorldEntity"/>.
        /// </summary>
        /// <param name="entity"></param>
        void OnSummon(IWorldEntity summoned)
        {
        }

        /// <summary>
        /// Invoked when <see cref="IWorldEntity"/> unsummons another <see cref="IWorldEntity"/>.
        /// </summary>
        void OnUnsummon(IWorldEntity summoned)
        {
        }
    }
}
