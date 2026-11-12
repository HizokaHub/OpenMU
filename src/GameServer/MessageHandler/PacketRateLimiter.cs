// <copyright file="PacketRateLimiter.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic;

/// <summary>
/// A small per-player limit for the custom <c>C1 D5</c> packets of the MOBA, so a modified client cannot flood the server
/// (rooms, chat, shop previews, orders). The normal client sends far fewer than <see cref="MaxPerWindow"/> per window.
/// </summary>
internal static class PacketRateLimiter
{
    /// <summary>The most packets accepted per <see cref="WindowTicks"/>.</summary>
    internal const int MaxPerWindow = 60;

    /// <summary>The length of the window, in <see cref="Environment.TickCount64"/> milliseconds.</summary>
    internal const long WindowTicks = 1000;

    private static readonly ConditionalWeakTable<Player, Counter> Counters = new();

    /// <summary>Counts one packet of the player and tells whether it is within the limit.</summary>
    /// <param name="player">The player.</param>
    /// <returns><see langword="true"/> if the packet may be handled; <see langword="false"/> if it must be dropped.</returns>
    internal static bool Allow(Player player)
    {
        var counter = Counters.GetValue(player, _ => new Counter());
        return counter.Allow(Environment.TickCount64, player.Logger);
    }

    /// <summary>Same as <see cref="Allow(Player)"/> with an explicit clock; used by the tests.</summary>
    /// <param name="counter">The counter.</param>
    /// <param name="nowMs">The current time in milliseconds.</param>
    /// <returns>Whether the packet is accepted.</returns>
    internal static bool Allow(Counter counter, long nowMs) => counter.Allow(nowMs, null);

    /// <summary>The packets seen in the current window.</summary>
    internal sealed class Counter
    {
        private long _windowStart;
        private int _count;
        private bool _warned;

        /// <summary>Counts one packet at the given time.</summary>
        /// <param name="nowMs">The time, in milliseconds.</param>
        /// <param name="logger">Where to warn (once per flooding burst); may be <see langword="null"/>.</param>
        /// <returns>Whether the packet is accepted.</returns>
        internal bool Allow(long nowMs, ILogger? logger)
        {
            lock (this)
            {
                if (nowMs - this._windowStart >= WindowTicks)
                {
                    this._windowStart = nowMs;
                    this._count = 0;
                    this._warned = false;
                }

                if (++this._count <= MaxPerWindow)
                {
                    return true;
                }

                if (!this._warned)
                {
                    this._warned = true;
                    logger?.LogWarning("[MOBA-SECURITY] More than {Max} D5 packets per second; the rest are dropped.", MaxPerWindow);
                }

                return false;
            }
        }
    }
}
