// <copyright file="MobaPingLimiter.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Limits how many pings one champion can send: at most <see cref="MaxPings"/> within <see cref="WindowSeconds"/>; past that the champion is
/// locked out for <see cref="LockoutSeconds"/> (the same idea as the ping limit of League of Legends, which stops the spam).
/// </summary>
public sealed class MobaPingLimiter
{
    /// <summary>Pings allowed within the window.</summary>
    public const int MaxPings = 5;

    /// <summary>Length of the window, in seconds.</summary>
    public const double WindowSeconds = 5;

    /// <summary>Seconds the champion cannot ping after going over the limit.</summary>
    public const double LockoutSeconds = 6;

    private readonly Queue<DateTime> _recent = new();

    private DateTime _lockedUntilUtc = DateTime.MinValue;

    /// <summary>Tries to spend one ping.</summary>
    /// <param name="now">The current time (UTC).</param>
    /// <returns><c>true</c> when the ping may be sent; <c>false</c> when the champion is locked out (and, if it just went over the limit, starts the lockout).</returns>
    public bool TryAcquire(DateTime now)
    {
        lock (this._recent)
        {
            if (now < this._lockedUntilUtc)
            {
                return false;
            }

            while (this._recent.Count > 0 && (now - this._recent.Peek()).TotalSeconds >= WindowSeconds)
            {
                this._recent.Dequeue();
            }

            if (this._recent.Count >= MaxPings)
            {
                this._lockedUntilUtc = now.AddSeconds(LockoutSeconds);
                this._recent.Clear();
                return false;
            }

            this._recent.Enqueue(now);
            return true;
        }
    }
}
