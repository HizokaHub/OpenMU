// <copyright file="MobaCombatLog.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Process-wide, RAM-only log of recent damaging hits between MOBA participants, used
/// by <see cref="MobaLaneCreepIntelligence"/> for the reactive parts of the LoL
/// targeting priority ("attack whoever is hitting an ally / me").
/// </summary>
/// <remarks>
/// Fed by <see cref="MobaCombatLogPlugIn"/> on every hit. Entries older than
/// <see cref="MaxAge"/> are pruned on write. A per-match context owning this comes
/// later.
/// </remarks>
public static class MobaCombatLog
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(5);

    private static readonly object Sync = new();

    private static readonly LinkedList<Entry> Recent = new();

    /// <summary>
    /// Records that <paramref name="attacker"/> damaged <paramref name="victim"/> now.
    /// </summary>
    /// <param name="attacker">The attacker.</param>
    /// <param name="victim">The victim.</param>
    public static void Record(object attacker, object victim)
    {
        var now = DateTime.UtcNow;
        lock (Sync)
        {
            Recent.AddLast(new Entry(attacker, victim, now));
            while (Recent.First is { Value.At: var at } && now - at > MaxAge)
            {
                Recent.RemoveFirst();
            }
        }
    }

    /// <summary>
    /// Gets the most recent time <paramref name="attacker"/> damaged any of
    /// <paramref name="victims"/> within <paramref name="window"/>, or <see langword="null"/>.
    /// </summary>
    /// <param name="attacker">The attacker to look for.</param>
    /// <param name="victims">The candidate victims.</param>
    /// <param name="window">How far back to look.</param>
    /// <returns>The most recent matching hit time, or null.</returns>
    public static DateTime? LastHitTimeAmong(object attacker, IReadOnlyCollection<object> victims, TimeSpan window)
    {
        if (victims.Count == 0)
        {
            return null;
        }

        var cutoff = DateTime.UtcNow - window;
        DateTime? best = null;
        lock (Sync)
        {
            for (var node = Recent.Last; node is not null; node = node.Previous)
            {
                var e = node.Value;
                if (e.At < cutoff)
                {
                    break;
                }

                if (ReferenceEquals(e.Attacker, attacker) && victims.Contains(e.Victim))
                {
                    best = e.At;
                    break; // iterating newest-first, first match is the most recent
                }
            }
        }

        return best;
    }

    /// <summary>
    /// The distinct objects that damaged <paramref name="victim"/> within <paramref name="window"/>, most recent first.
    /// </summary>
    /// <param name="victim">The victim to look up.</param>
    /// <param name="window">How far back to look.</param>
    /// <returns>The recent attackers of the victim.</returns>
    public static IReadOnlyList<object> RecentAttackersOf(object victim, TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;
        var result = new List<object>();
        lock (Sync)
        {
            for (var node = Recent.Last; node is not null; node = node.Previous)
            {
                var e = node.Value;
                if (e.At < cutoff)
                {
                    break;
                }

                if (ReferenceEquals(e.Victim, victim) && !result.Contains(e.Attacker))
                {
                    result.Add(e.Attacker);
                }
            }
        }

        return result;
    }

    /// <summary>Whether <paramref name="participant"/> dealt OR took damage within <paramref name="window"/>.</summary>
    /// <param name="participant">The champion / unit.</param>
    /// <param name="window">How far back to look.</param>
    /// <returns><see langword="true"/> if it is in combat.</returns>
    public static bool InCombat(object participant, TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;
        lock (Sync)
        {
            for (var node = Recent.Last; node is not null; node = node.Previous)
            {
                var e = node.Value;
                if (e.At < cutoff)
                {
                    break;
                }

                if (ReferenceEquals(e.Attacker, participant) || ReferenceEquals(e.Victim, participant))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether <paramref name="attacker"/> damaged any of <paramref name="victims"/> within <paramref name="window"/>.
    /// </summary>
    /// <param name="attacker">The attacker.</param>
    /// <param name="victims">The candidate victims.</param>
    /// <param name="window">How far back to look.</param>
    /// <returns><see langword="true"/> if a matching hit exists.</returns>
    public static bool HitAnyOf(object attacker, IReadOnlyCollection<object> victims, TimeSpan window)
        => LastHitTimeAmong(attacker, victims, window) is not null;

    private readonly record struct Entry(object Attacker, object Victim, DateTime At);
}
