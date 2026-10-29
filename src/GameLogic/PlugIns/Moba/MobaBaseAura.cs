// <copyright file="MobaBaseAura.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// The green aura around the blacksmith (Hanzo, the vendor) of every base (2026-10-25): the living champions of the team within
/// <see cref="RadiusTiles"/> of him recover <see cref="Fraction"/> of their maximum life, mana and shield every second, in or out of
/// combat. The client draws the aura at the same position and radius (<c>World/Moba/MobaBaseAura</c>).
/// </summary>
public static class MobaBaseAura
{
    /// <summary>The radius of the aura in tiles, around the vendor of the team.</summary>
    public const double RadiusTiles = 7;

    /// <summary>The fraction of the maximum life, mana and shield recovered per second.</summary>
    public const double Fraction = 0.20;

    private static DateTime _lastTickUtc = DateTime.MinValue;

    /// <summary>
    /// Gets whether a position is inside the aura of a team.
    /// </summary>
    /// <param name="team">The team whose base it is.</param>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    /// <returns><see langword="true"/> within the radius of the vendor of the team.</returns>
    public static bool Contains(MobaTeam team, int x, int y)
    {
        if (team == MobaTeam.None)
        {
            return false;
        }

        var shop = MobaLayout.ShopOf(team);
        var dx = x - shop.X;
        var dy = y - shop.Y;
        return (dx * dx) + (dy * dy) <= RadiusTiles * RadiusTiles;
    }

    /// <summary>
    /// Recovers the champions that stand in the aura of their base (called every second by the match tick).
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    public static async ValueTask TickAsync(IGameContext gameContext)
    {
        var now = DateTime.UtcNow;
        var seconds = _lastTickUtc == DateTime.MinValue ? 1.0 : Math.Clamp((now - _lastTickUtc).TotalSeconds, 0.05, 2.5);
        _lastTickUtc = now;

        var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        foreach (var champion in players)
        {
            if (!champion.IsMobaClone
                || !champion.IsAlive
                || champion.CurrentMap is not { } map
                || map.MapId != MobaCloneFactory.ArenaMapNumber
                || champion.Attributes is not { } attributes
                || champion is MobaBotPlayer { IsDummy: true }
                || !Contains(MobaTeams.GetTeam(champion), champion.Position.X, champion.Position.Y))
            {
                continue;
            }

            Restore(attributes, Stats.CurrentHealth, Stats.MaximumHealth, seconds);
            Restore(attributes, Stats.CurrentMana, Stats.MaximumMana, seconds);
            Restore(attributes, Stats.CurrentShield, Stats.MaximumShield, seconds);
        }
    }

    private static void Restore(IAttributeSystem attributes, AttributeDefinition current, AttributeDefinition maximum, double seconds)
    {
        var max = attributes[maximum];
        if (max <= 0 || attributes[current] >= max)
        {
            return;
        }

        attributes[current] = (float)Math.Min(max, attributes[current] + (max * Fraction * seconds));
    }
}
