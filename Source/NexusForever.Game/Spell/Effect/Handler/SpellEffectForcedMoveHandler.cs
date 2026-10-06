using System.Numerics;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement.Force;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Effect;
using NexusForever.Game.Abstract.Spell.Effect.Data;
using NexusForever.Game.Abstract.Spell.Target;
using NexusForever.Game.Static.Entity.Movement.Command.State;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Static.Spell.Effect;
using NexusForever.Shared;

namespace NexusForever.Game.Spell.Effect.Handler
{
    [SpellEffectHandler(SpellEffectType.ForcedMove)]
    public class SpellEffectForcedMoveHandler : ISpellEffectApplyHandler<ISpellEffectForcedMoveData>
    {
        #region Dependency Injection

        private readonly IForcedMovementGenerator forcedMovementGenerator;

        public SpellEffectForcedMoveHandler(
            IForcedMovementGenerator forcedMovementGenerator)
        {
            this.forcedMovementGenerator = forcedMovementGenerator;
        }

        #endregion

        public SpellEffectExecutionResult Apply(ISpellExecutionContext executionContext, IUnitEntity target, ISpellTargetEffectInfo info, ISpellEffectForcedMoveData data)
        {
            float distance = data.MinDistance + ((float)Random.Shared.NextDouble() * (data.MaxDistance - data.MinDistance));
            float gravity  = data.Gravity;
            float data06   = data.Unknown6;

            var v20 = (float)data.FlightTime.TotalSeconds;
            if (data06 == 0)
                data06 = ((gravity * 0.125f) * v20) * v20;
            else
                gravity = (data06 * 8.0f) / (v20 * v20);

            IUnitEntity mover;
            if ((data.Flags & SpellEffectForcedMoveFlags.Target) != 0)
                mover = target;
            else
                mover = executionContext.Spell.Caster;

            Vector3 position = Vector3.Zero;
            float angle = 0f;

            switch (data.MoveType)
            {
                case SpellEffectForcedMoveType.PositionForward:
                case SpellEffectForcedMoveType.KeyForward:
                case SpellEffectForcedMoveType.Unknown11:
                {
                    if (mover == target)
                    {
                        // TODO
                    }

                    angle = -target.Rotation.X - MathF.PI / 2;
                    if (data.Angle != 0f)
                        angle -= data.Angle.ToRadians();

                    position = target.Position.GetPoint2D(angle, distance);
                    break;
                }
                case SpellEffectForcedMoveType.PositionBackward:
                case SpellEffectForcedMoveType.KeyBackward:
                case SpellEffectForcedMoveType.Unknown12:
                {
                    angle = -target.Rotation.X + MathF.PI / 2;
                    if (data.Angle != 0f)
                        angle += data.Angle.ToRadians();

                    position = target.Position.GetPoint2D(angle, distance);
                    break;
                }
                case SpellEffectForcedMoveType.PositionRandom:
                case SpellEffectForcedMoveType.KeyRandom:
                case SpellEffectForcedMoveType.Unknown13:
                {
                    angle = (float)Random.Shared.NextDouble() * (MathF.PI * 2f);
                    position = target.Position.GetPoint2D(angle, distance);
                    break;
                }
                // velocity knock
                case SpellEffectForcedMoveType.KeyVelocity:
                case SpellEffectForcedMoveType.PositionVelocity:
                case SpellEffectForcedMoveType.Unknown14:
                {
                    // TODO
                    StateFlags stateFlags = target.MovementManager.GetState();
                    break;
                }
                case SpellEffectForcedMoveType.Unknown8:  // 60331 - Teleport Target to Caster
                case SpellEffectForcedMoveType.Unknown9:  // 2039 - Get Over Here
                case SpellEffectForcedMoveType.Unknown10: // 4206 - Grapple, 46658 - Rocketeering
                case SpellEffectForcedMoveType.Unknown15:
                {
                    // move to a unit (client FUN_1405d6c10): the other side of the move, the caster when the target is
                    // moved (Target flag), otherwise the spell's primary target; unless flag 2 ("land exactly") the mover
                    // stops short by both hit radii plus the distance value (local reading of the client, 6 Oct 2026)
                    IUnitEntity destination = mover == target && mover != executionContext.Spell.Caster
                        ? executionContext.Spell.Caster
                        : GetPrimaryTarget(executionContext.Spell);
                    if (destination == null || destination == mover)
                        return SpellEffectExecutionResult.PreventEffect;

                    position = destination.Position;
                    if ((data.Flags & SpellEffectForcedMoveFlags.Unknown2) == 0)
                    {
                        Vector3 back = mover.Position - destination.Position;
                        back.Y = 0f;
                        float shortBy = destination.HitRadius + mover.HitRadius + data.MinDistance;
                        if (back.Length() > shortBy)
                            position += Vector3.Normalize(back) * shortBy;
                        else
                            position = mover.Position;
                    }

                    Vector3 along = position - mover.Position;
                    angle = MathF.Atan2(-along.X, -along.Z);
                    break;
                }
            }

            if (position == Vector3.Zero)
                return SpellEffectExecutionResult.PreventEffect;

            // TODO
            if ((data.Flags & SpellEffectForcedMoveFlags.Unknown4) != 0)
            {
            }

            // TODO
            if ((data.Flags & SpellEffectForcedMoveFlags.Facing) != 0)
            {
            }

            // TODO: this needs to be smarter and take other geometry into account
            // once LoS maps are implemented might be able to use a ray instead
            float? terrainHeight = mover.Map.GetTerrainHeight(position.X, position.Z);
            if (position.Y < terrainHeight)
                position.Y = terrainHeight.Value;

            // the client carries out a forced move on its own player itself (FUN_1405d6c10 returns early for any other
            // mover): moving the player on the server as well fought the client's flight, the server follows its
            // position updates instead (local reading, 6 Oct 2026)
            if (mover is IPlayer && data.MoveType is SpellEffectForcedMoveType.Unknown8 or SpellEffectForcedMoveType.Unknown9
                or SpellEffectForcedMoveType.Unknown10 or SpellEffectForcedMoveType.Unknown15)
                return SpellEffectExecutionResult.Ok;

            switch (data.MoveType)
            {
                case SpellEffectForcedMoveType.PositionForward:
                case SpellEffectForcedMoveType.PositionBackward:
                case SpellEffectForcedMoveType.PositionRandom:
                case SpellEffectForcedMoveType.PositionVelocity:
                case SpellEffectForcedMoveType.Unknown8:
                {
                    if (mover is IPlayer player)
                        player.TeleportToLocal(position, false);
                    else
                        mover.MovementManager.SetPosition(position, false);
                    break;
                }
                case SpellEffectForcedMoveType.KeyForward:
                case SpellEffectForcedMoveType.KeyBackward:
                case SpellEffectForcedMoveType.KeyRandom:
                case SpellEffectForcedMoveType.KeyVelocity:
                case SpellEffectForcedMoveType.Unknown9:
                case SpellEffectForcedMoveType.Unknown10:
                {
                    //float speed = distance / (float)data.FlightTime.TotalSeconds;
                    float spin = (data.Spin * MathF.PI * 2) / v20;
                    forcedMovementGenerator.ForceMove(mover, position, new Vector3(angle, 0f, 0f), data.FlightTime, gravity, spin);
                    break;
                }
                // seems like high jumps?? Trampoline?
                case SpellEffectForcedMoveType.Unknown11:
                case SpellEffectForcedMoveType.Unknown12:
                case SpellEffectForcedMoveType.Unknown13:
                case SpellEffectForcedMoveType.Unknown14:
                {
                    break;
                }
                case SpellEffectForcedMoveType.Unknown15:
                {
                    float spin = (data.Spin * MathF.PI * 2) / v20;
                    forcedMovementGenerator.ForceMove(mover, position, new Vector3(angle, 0f, 0f), data.FlightTime, gravity, spin);
                    break;
                }
            }

            return SpellEffectExecutionResult.Ok;
        }

        private static IUnitEntity GetPrimaryTarget(ISpell spell)
        {
            if (spell.Parameters.PrimaryTargetId == 0u)
                return null;

            return spell.Caster.GetVisible<IGridEntity>(spell.Parameters.PrimaryTargetId) as IUnitEntity;
        }
    }
}