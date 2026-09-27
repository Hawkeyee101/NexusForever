using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Network.World.Message.Model;
using NexusForever.Shared;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    /// <summary>
    /// The intro drop ship: boards arriving players, gives players who leave it the slow-burn jetpack (Rocket Fall)
    /// until they land, and sends the ship away.
    /// </summary>
    public class HycrestDropShip
    {
        private enum DropState
        {
            OnBoard,
            Falling,
            Landed
        }

        private class PlayerDrop
        {
            public DropState State;
            public Vector3 Spot;
            public bool Boarded;
            public double SinceLoaded;
            public double SinceBoarded;
            public bool OnPlatformConfirmed;
            public bool ReachedDeck;
            public double SinceCast;
            public float SampleY;
            public double SinceSample;
            public double SinceFall;
        }

        // boarding waits this long after the client has finished loading the map (it can teleport again), and the
        // teleport shows a loading screen; teleporting in the same tick as ClientEnteredWorld left a client in an empty
        // void once (27 Sep 2026)
        public const double BoardAfterLoad = 1.5d;

        // after boarding, position and platform reports are ignored this long: reports sent before the client applied
        // the teleport (e.g. still falling from the login position) must not count as leaving the ship
        private const double BoardGracePeriod = 2d;

        // Rocket Fall: GravityMultiplier 0.1 and no fall damage for 3 s, jetpack/flame visuals
        // re-applied just before it runs out so there is no gap
        private const uint RocketFallSpell = 47734u;
        private const double RocketFallRecast = 2.9d;
        // a cast that didn't take (e.g. the player was still casting) is tried again this soon
        private const double RocketFallRetry = 0.25d;

        // fallback when the platform attachment isn't reported: a player this far below the deck has left the ship
        private const float LeaveDistance = 3f;

        // landed: within LandedHeight of the terrain (map file; props like roofs aren't in it), or no longer falling
        // (moved less than LandedMaxDrop vertically within LandedWindow) once the player has fallen for at least
        // MinFallTime; right after leaving the ship the fall is still too slow to tell (27 Sep 2026: Rocket Fall stopped
        // after one cast because the first window counted as landed)
        private const float LandedHeight   = 2f;
        private const float LandedMaxDrop  = 0.3f;
        private const double LandedWindow  = 1d;
        private const double MinFallTime   = 5d;
        private const double MaxFallTime   = 120d;

        // the Imperium Transport (17722) always spawns with both doorways open and its ramps out; like retail, the door
        // entities (Right 18338, Left 28509, platforms) close the doorways. States are driven like DoorEntity (StandState
        // stat + emote, the models' AP_State sequences): ship State1 hovering (engines shake), State2 "jump away" (13 s);
        // doors State0 closed, State1 open. Only the right door opens, the left one stays closed.
        private static readonly TimeSpan DepartRemoveDelay = TimeSpan.FromSeconds(14);

        private readonly IMapInstance map;
        private readonly IFactory<ISpellParameters> spellParametersFactory;
        private readonly ILogger log;
        private readonly TimedActionQueue actionQueue;

        // keyed by character id: a teleport within the map removes and re-adds the player, possibly with a new guid
        private readonly Dictionary<ulong, PlayerDrop> players = [];
        private int nextSpot;

        public uint ShipGuid { get; set; }
        public uint RightDoorGuid { get; set; }
        public uint LeftDoorGuid { get; set; }
        public uint DawsonGuid { get; set; }
        public uint HologramGuid { get; set; }

        public bool FlewIn { get; private set; }

        /// <summary>
        /// The ship is at its hover point (flown in, or never had to). Players are only put on board then: carrying players
        /// on a moving platform made them fall through the floor (27 Sep 2026).
        /// </summary>
        public bool Arrived { get; private set; }

        public bool DoorsOpen { get; private set; }
        public bool Departed { get; private set; }

        /// <summary>
        /// Seconds since the first player was put on board (the client had finished loading), null until then.
        /// </summary>
        public double? SinceFirstBoard { get; private set; }

        /// <summary>
        /// Show a loading screen for the boarding teleport; not needed while the intro text's black screen hides it.
        /// </summary>
        public bool BoardWithLoadingScreen { get; set; } = true;

        /// <summary>
        /// Invoked when a player waiting to board has finished loading the map (the boarding teleport follows after
        /// <see cref="BoardAfterLoad"/>).
        /// </summary>
        public event Action<IPlayer> PlayerLoaded;

        /// <summary>
        /// Invoked when a player has been put on board.
        /// </summary>
        public event Action<IPlayer> PlayerBoarded;

        public HycrestDropShip(IMapInstance map, IFactory<ISpellParameters> spellParametersFactory, ILogger log, TimedActionQueue actionQueue)
        {
            this.map                    = map;
            this.spellParametersFactory = spellParametersFactory;
            this.log                    = log;
            this.actionQueue            = actionQueue;
        }

        /// <summary>
        /// Put <paramref name="player"/> on the ship's deck, unless the ship has already left or the player has boarded before.
        /// </summary>
        /// <remarks>
        /// The group finder entrance has to be a WorldLocation2 point and there is none on the moved ship, so arriving
        /// players are teleported onto the deck. The teleport waits until the client has finished loading the map,
        /// a local teleport is refused while the map transfer is still pending.
        /// </remarks>
        public void Board(IPlayer player)
        {
            if (Departed || players.ContainsKey(player.CharacterId))
                return;

            Vector3 spot = HycrestShipLayout.PlayerSpots[nextSpot++ % HycrestShipLayout.PlayerSpots.Length];
            players[player.CharacterId] = new PlayerDrop
            {
                State   = DropState.OnBoard,
                Spot    = spot,
                SampleY = spot.Y
            };
        }

        /// <summary>
        /// Set the stand state of <paramref name="entity"/>, which plays its model's AP_State transition, like <c>DoorEntity</c>.
        /// </summary>
        public static void SetState(IWorldEntity entity, StandState state)
        {
            if (entity == null)
                return;

            entity.StandState = state;
            entity.EnqueueToVisible(new ServerEmote
            {
                Guid       = entity.Guid,
                StandState = state
            });
        }

        /// <summary>
        /// Open the exit door so players can walk down the ramp and jump; the other door stays closed.
        /// </summary>
        public void OpenDoors()
        {
            if (DoorsOpen)
                return;

            DoorsOpen = true;
            // the exit is the ramp on the ship's right (towards the barn); in game the "Left" door entity (28509) is the
            // one that closes that doorway (27 Sep 2026: opening 18338 opened the other side)
            SetState(map.GetEntity<IWorldEntity>(LeftDoorGuid), StandState.State1);

            log.LogInformation("Hycrest: drop ship exit door opened.");
        }

        /// <summary>
        /// Fly the ship from its start point to its hover point, before anyone is on board (see <see cref="Arrived"/>).
        /// The doors and the hologram aren't attached to it, so they fly the same path at the same speed.
        /// </summary>
        public void FlyIn()
        {
            if (FlewIn || Departed)
                return;

            FlewIn = true;

            IWorldEntity ship = map.GetEntity<IWorldEntity>(ShipGuid);
            if (ship == null)
            {
                Arrived = true;
                return;
            }

            Vector3 delta = HycrestShipLayout.Origin - ship.Position;
            if (delta.Length() < 1f)
            {
                Arrived = true;
                return;
            }

            // a moment of margin for the spline to settle before players are put on board
            actionQueue.Enqueue(TimeSpan.FromSeconds(delta.Length() / HycrestShipLayout.FlyInSpeed + 0.5d), () => Arrived = true);

            foreach (uint guid in new[] { ShipGuid, RightDoorGuid, LeftDoorGuid, HologramGuid })
            {
                IWorldEntity entity = map.GetEntity<IWorldEntity>(guid);
                entity?.MovementManager.SetPositionPath([entity.Position, entity.Position + delta], SplineType.Linear,
                    SplineMode.OneShot, HycrestShipLayout.FlyInSpeed);
            }

            log.LogInformation($"Hycrest: drop ship flying in ({delta.Length():0} m).");
        }

        /// <summary>
        /// Send the ship away ("jump away", State2). Players still on board are moved to the drop point first.
        /// </summary>
        public void Depart()
        {
            if (Departed)
                return;

            Departed = true;
            OpenDoors();

            foreach (IPlayer player in map.GetPlayers())
            {
                if (!players.TryGetValue(player.CharacterId, out PlayerDrop drop)
                    || drop.State != DropState.OnBoard
                    || !drop.Boarded)
                    continue;

                StartFalling(player, drop);
                player.TeleportToLocal(HycrestShipLayout.DropPoint, false);
            }

            map.GetEntity<IWorldEntity>(DawsonGuid)?.RemoveFromMap();

            // the doors don't belong to the ship model and would stay behind in the air
            map.GetEntity<IWorldEntity>(RightDoorGuid)?.RemoveFromMap();
            map.GetEntity<IWorldEntity>(LeftDoorGuid)?.RemoveFromMap();

            IWorldEntity ship = map.GetEntity<IWorldEntity>(ShipGuid);
            if (ship != null)
            {
                SetState(ship, StandState.State2);
                ship.MovementManager.SetPositionPath([ship.Position, ship.Position + HycrestShipLayout.DepartOffset],
                    SplineType.Linear, SplineMode.OneShot, HycrestShipLayout.DepartSpeed);
            }

            actionQueue.Enqueue(DepartRemoveDelay, () => map.GetEntity<IWorldEntity>(ShipGuid)?.RemoveFromMap());

            log.LogInformation("Hycrest: drop ship departed.");
        }

        /// <summary>
        /// Returns if every player in the instance has left the ship.
        /// </summary>
        public bool EveryoneOff()
        {
            return map.GetPlayers().All(p => !players.TryGetValue(p.CharacterId, out PlayerDrop drop) || drop.State != DropState.OnBoard);
        }

        /// <summary>
        /// Invoked each tick: detect players leaving the ship, keep Rocket Fall on them until they land.
        /// </summary>
        public void Update(double lastTick)
        {
            if (SinceFirstBoard.HasValue)
                SinceFirstBoard += lastTick;

            foreach (IPlayer player in map.GetPlayers())
            {
                if (!players.TryGetValue(player.CharacterId, out PlayerDrop drop))
                    continue;

                if (!drop.Boarded)
                {
                    if (Departed)
                    {
                        // never made it on board, nothing to fall from
                        drop.State = DropState.Landed;
                        continue;
                    }

                    if (!player.CanTeleport())
                    {
                        drop.SinceLoaded = 0d;
                        continue;
                    }

                    if (!Arrived)
                    {
                        // loaded, waiting under the narration's black screen for the ship to arrive
                        if (drop.SinceLoaded == 0d)
                            PlayerLoaded?.Invoke(player);
                        drop.SinceLoaded = Math.Max(drop.SinceLoaded, BoardAfterLoad);
                        continue;
                    }

                    if (drop.SinceLoaded == 0d)
                        PlayerLoaded?.Invoke(player);

                    drop.SinceLoaded += lastTick;
                    if (drop.SinceLoaded < BoardAfterLoad)
                        continue;

                    drop.Boarded = true;
                    SinceFirstBoard ??= 0d;
                    // spots are given for the ship at its hover point; it may still be at its start point or flying in
                    IWorldEntity ship = map.GetEntity<IWorldEntity>(ShipGuid);
                    Vector3 spot = ship != null ? HycrestShipLayout.OnShip(drop.Spot, ship.Position) : drop.Spot;
                    player.TeleportToLocal(spot, BoardWithLoadingScreen);
                    PlayerBoarded?.Invoke(player);
                    continue;
                }

                switch (drop.State)
                {
                    case DropState.OnBoard:
                        drop.SinceBoarded += lastTick;
                        if (drop.SinceBoarded < BoardGracePeriod)
                            break;
                        UpdateOnBoard(player, drop);
                        break;
                    case DropState.Falling:
                        UpdateFalling(player, drop, player.Position.Y, lastTick);
                        break;
                }
            }
        }

        private void UpdateOnBoard(IPlayer player, PlayerDrop drop)
        {
            // the client reports the platform it stands on; once the player has stood on the ship, stepping off it is
            // leaving the ship, detected on the next tick
            bool onShip = ShipGuid != 0u && player.MovementManager.GetPlatform() == ShipGuid;
            if (onShip)
            {
                drop.OnPlatformConfirmed = true;
                drop.ReachedDeck         = true;
                return;
            }

            // right after boarding the teleport hasn't completed yet and the position is still on the ground
            float y = player.Position.Y;
            if (!drop.ReachedDeck)
            {
                if (y > HycrestShipLayout.FloorY - 1f)
                    drop.ReachedDeck = true;
                return;
            }

            if (drop.OnPlatformConfirmed || y < HycrestShipLayout.FloorY - LeaveDistance)
                StartFalling(player, drop);
        }

        private void StartFalling(IPlayer player, PlayerDrop drop)
        {
            drop.State       = DropState.Falling;
            drop.SampleY     = player.Position.Y;
            drop.SinceSample = 0d;
            drop.SinceFall   = 0d;
            CastRocketFall(player, drop);
        }

        private void UpdateFalling(IPlayer player, PlayerDrop drop, float y, double lastTick)
        {
            drop.SinceFall   += lastTick;
            drop.SinceSample += lastTick;

            float? ground = map.GetTerrainHeight(player.Position.X, player.Position.Z);
            if ((ground.HasValue && y - ground.Value < LandedHeight) || drop.SinceFall >= MaxFallTime)
            {
                drop.State = DropState.Landed;
                return;
            }

            if (drop.SinceSample >= LandedWindow)
            {
                if (drop.SinceFall >= MinFallTime && Math.Abs(y - drop.SampleY) < LandedMaxDrop)
                {
                    drop.State = DropState.Landed;
                    return;
                }

                drop.SampleY     = y;
                drop.SinceSample = 0d;
            }

            drop.SinceCast += lastTick;
            if (drop.SinceCast >= RocketFallRecast)
                CastRocketFall(player, drop);
        }

        private void CastRocketFall(IPlayer player, PlayerDrop drop)
        {
            drop.SinceCast = 0d;

            // cast by the script, not the player: skips the cooldown and global cooldown checks (a player cast just before
            // the jump put Rocket Fall on the global cooldown and the cast failed)
            ISpellParameters parameters = spellParametersFactory.Resolve();
            parameters.PrimaryTargetId        = player.Guid;
            parameters.UserInitiatedSpellCast = false;
            player.CastSpell(RocketFallSpell, parameters);

            if (player.GetSpellBySpellId(RocketFallSpell) == null)
                drop.SinceCast = RocketFallRecast - RocketFallRetry;
        }
    }
}
