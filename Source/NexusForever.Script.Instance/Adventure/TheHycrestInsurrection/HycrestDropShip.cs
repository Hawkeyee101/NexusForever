using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Map.Instance;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Shared;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    /// <summary>
    /// The intro drop ship: tracks which players are on board, gives players who leave it the slow-burn jetpack
    /// (Rocket Fall) until they land, opens the doors and sends the ship away.
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
            public double SinceCast;
            public float SampleY;
            public double SinceSample;
        }

        // Rocket Fall: GravityMultiplier 0.1 and no fall damage for 3 s, jetpack/flame visuals
        private const uint RocketFallSpell = 47734u;
        private const double RocketFallRecast = 2.5d;

        // deck height from the deck points 50008/50009/50021/50022; a player more than LeaveDistance below it left the ship
        private const float DeckY = -819.6f;
        private const float LeaveDistance = 3f;

        // landed: moved less than LandedMaxDrop vertically within LandedWindow; a window is used because position
        // updates don't arrive every tick and the slow-burn descent is slow
        private const float LandedMaxDrop = 0.3f;
        private const double LandedWindow = 1d;

        // anyone still on board when the ship leaves is moved just outside it, towards the barn, and glides down
        private static readonly Vector3 DropPoint = new(-2537.9f, -826f, -1100f);

        // the ship climbs away north over the hills and is removed once it's out of sight
        private static readonly Vector3 DepartOffset = new(0f, 80f, 250f);
        private const float DepartSpeed = 25f;
        private static readonly TimeSpan DepartRemoveDelay = TimeSpan.FromSeconds(12);

        private readonly IMapInstance map;
        private readonly IFactory<ISpellParameters> spellParametersFactory;
        private readonly ILogger log;
        private readonly TimedActionQueue actionQueue;

        private readonly Dictionary<uint, PlayerDrop> players = [];

        public uint ShipGuid { get; set; }
        public List<uint> DoorGuids { get; } = [];
        public uint DawsonGuid { get; set; }

        public bool DoorsOpen { get; private set; }
        public bool Departed { get; private set; }

        public HycrestDropShip(IMapInstance map, IFactory<ISpellParameters> spellParametersFactory, ILogger log, TimedActionQueue actionQueue)
        {
            this.map                    = map;
            this.spellParametersFactory = spellParametersFactory;
            this.log                    = log;
            this.actionQueue            = actionQueue;
        }

        /// <summary>
        /// Open the ship's doors so players can jump out.
        /// </summary>
        /// <remarks>
        /// The door entities are platforms without an open state in the engine, so opening removes them.
        /// </remarks>
        public void OpenDoors()
        {
            if (DoorsOpen)
                return;

            DoorsOpen = true;
            foreach (uint guid in DoorGuids)
                map.GetEntity<IWorldEntity>(guid)?.RemoveFromMap();
            DoorGuids.Clear();
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

            foreach ((uint guid, PlayerDrop drop) in players)
            {
                if (drop.State != DropState.OnBoard)
                    continue;

                IPlayer player = map.GetEntity<IPlayer>(guid);
                if (player == null)
                    continue;

                StartFalling(player, drop);
                player.TeleportToLocal(DropPoint, false);
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
            return map.GetPlayers().All(p => !players.TryGetValue(p.Guid, out PlayerDrop drop) || drop.State != DropState.OnBoard);
        }

        /// <summary>
        /// Invoked each tick: detect players leaving the ship, keep Rocket Fall on them until they land.
        /// </summary>
        public void Update(double lastTick)
        {
            foreach (IPlayer player in map.GetPlayers())
            {
                float y = player.Position.Y;
                if (!players.TryGetValue(player.Guid, out PlayerDrop drop))
                {
                    // players arriving on the deck start on board, anyone else (GM teleport, ground entrance) doesn't
                    drop = new PlayerDrop
                    {
                        State   = !Departed && y > DeckY - LeaveDistance ? DropState.OnBoard : DropState.Landed,
                        SampleY = y
                    };
                    players.Add(player.Guid, drop);
                    continue;
                }

                switch (drop.State)
                {
                    case DropState.OnBoard:
                        if (y < DeckY - LeaveDistance)
                            StartFalling(player, drop);
                        break;
                    case DropState.Falling:
                        UpdateFalling(player, drop, y, lastTick);
                        break;
                }
            }
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
