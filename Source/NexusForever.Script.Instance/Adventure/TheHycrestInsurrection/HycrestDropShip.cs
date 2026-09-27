using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Network.World.Entity;
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
        }

        // boarding waits this long after the client has finished loading the map (it can teleport again), and the
        // teleport shows a loading screen; teleporting in the same tick as ClientEnteredWorld left a client in an empty
        // void once (27 Sep 2026)
        private const double BoardAfterLoad = 1.5d;

        // after boarding, position and platform reports are ignored this long: reports sent before the client applied
        // the teleport (e.g. still falling from the login position) must not count as leaving the ship
        private const double BoardGracePeriod = 2d;

        // Rocket Fall: GravityMultiplier 0.1 and no fall damage for 3 s, jetpack/flame visuals
        // re-applied just before it runs out so there is no gap
        private const uint RocketFallSpell = 47734u;
        private const double RocketFallRecast = 2.9d;

        // fallback when the platform attachment isn't reported: a player this far below the deck has left the ship
        private const float LeaveDistance = 3f;

        // landed: moved less than LandedMaxDrop vertically within LandedWindow; a window is used because position
        // updates don't arrive every tick and the slow-burn descent is slow
        private const float LandedMaxDrop = 0.3f;
        private const double LandedWindow = 1d;

        // the door and the walkway are part of the Set Ship model (70557), not separate entities. The model has no
        // active prop states, only the intro cinematic's sequences on one timeline: Cinematic_Misc_01 (3.3-9.3 s),
        // Cinematic_Misc_02 (9.3-19.6 s), Cinematic_Misc_03 (19.7-26.3 s). In Misc_02 its two animated parts move from
        // their hidden rest position onto the deck and one slides ~23 m out (10.0-12.1 s): most likely the walkway
        // extending, with the door. ESTIMATE, to be confirmed in game with "!entity modify visual <id> 70557":
        // 11097 plays Misc_02 and holds (flags 4); alternatives 19807 (Misc_02 one-shot), 11098 (Misc_03), 45237 (Misc_00, all)
        public static readonly uint[] ShipOpenVisualEffects = [11097u];

        // the ship climbs away south over the fields and is removed once it's out of sight
        private static readonly Vector3 DepartOffset = new(0f, 80f, -250f);
        private const float DepartSpeed = 25f;
        private static readonly TimeSpan DepartRemoveDelay = TimeSpan.FromSeconds(12);

        private readonly IMapInstance map;
        private readonly IFactory<ISpellParameters> spellParametersFactory;
        private readonly ILogger log;
        private readonly TimedActionQueue actionQueue;

        // keyed by character id: a teleport within the map removes and re-adds the player, possibly with a new guid
        private readonly Dictionary<ulong, PlayerDrop> players = [];
        private int nextSpot;
        private uint visualHandle = 0x48440000u;

        public uint ShipGuid { get; set; }
        public List<uint> DoorGuids { get; } = [];
        public uint DawsonGuid { get; set; }

        public bool DoorsOpen { get; private set; }
        public bool Departed { get; private set; }

        /// <summary>
        /// Seconds since the first player was put on board (the client had finished loading), null until then.
        /// </summary>
        public double? SinceFirstBoard { get; private set; }

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
        /// Open the ship's doors and extend the walkway so players can walk out and jump.
        /// </summary>
        /// <remarks>
        /// The Set Ship's door and walkway are animated in its model, see <see cref="ShipOpenVisualEffects"/>. Separate door
        /// entities (the Dominion Dropship's) are platforms without an open state in the engine, so they are removed.
        /// </remarks>
        public void OpenDoors()
        {
            if (DoorsOpen)
                return;

            DoorsOpen = true;

            IWorldEntity ship = map.GetEntity<IWorldEntity>(ShipGuid);
            if (ship != null)
            {
                foreach (uint visualEffectId in ShipOpenVisualEffects)
                {
                    ship.EnqueueToVisible(new ServerCinematicVisualEffect
                    {
                        VisualHandle      = visualHandle++,
                        VisualEffectId    = visualEffectId,
                        UnitId            = ship.Guid,
                        Position          = new Position(ship.Position),
                        RemoveOnCameraEnd = false
                    });
                }
            }

            foreach (uint guid in DoorGuids)
                map.GetEntity<IWorldEntity>(guid)?.RemoveFromMap();
            DoorGuids.Clear();

            log.LogInformation($"Hycrest: drop ship doors opened (visual effects {string.Join(", ", ShipOpenVisualEffects)}).");
        }

        /// <summary>
        /// Send the ship away. Players still on board get Rocket Fall and are moved to the drop point first.
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

            IWorldEntity ship = map.GetEntity<IWorldEntity>(ShipGuid);
            if (ship == null)
                return;

            ship.MovementManager.SetPositionPath([ship.Position, ship.Position + DepartOffset], SplineType.Linear, SplineMode.OneShot, DepartSpeed);
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

                    drop.SinceLoaded += lastTick;
                    if (drop.SinceLoaded < BoardAfterLoad)
                        continue;

                    drop.Boarded = true;
                    SinceFirstBoard ??= 0d;
                    player.TeleportToLocal(drop.Spot);
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
            CastRocketFall(player, drop);
        }

        private void UpdateFalling(IPlayer player, PlayerDrop drop, float y, double lastTick)
        {
            drop.SinceSample += lastTick;
            if (drop.SinceSample >= LandedWindow)
            {
                if (Math.Abs(y - drop.SampleY) < LandedMaxDrop)
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

            ISpellParameters parameters = spellParametersFactory.Resolve();
            parameters.PrimaryTargetId = player.Guid;
            player.CastSpell(RocketFallSpell, parameters);
        }
    }
}
