// <copyright file="MobaRecall.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The MOBA recall (key B on the client): the champion channels for <see cref="ChannelSeconds"/>
/// standing still, then is teleported (with the regular Teleport skill animation) next to its
/// team's shop. The channel breaks when the champion moves, casts, attacks, takes damage or dies.
/// The same channel drives the teleport-to-minion consumable (see <see cref="TryStartAsync"/>).
/// </summary>
public static class MobaRecall
{
    /// <summary>Seconds a recall has to be channelled.</summary>
    public const double ChannelSeconds = 5;

    /// <summary>The channel kind sent to the client for the recall.</summary>
    public const byte KindRecall = 1;

    /// <summary>The channel kind sent to the client for the teleport-to-minion consumable.</summary>
    public const byte KindTeleport = 2;

    private const int PollMs = 100;
    private const short TeleportSkillNumber = 6;

    private static readonly (byte X, byte Y) BlueBase = (112, 57);
    private static readonly (byte X, byte Y) RedBase = (112, 208);

    private static readonly ConditionalWeakTable<Player, Channel> Channels = new();

    /// <summary>Gets whether the champion is currently channelling.</summary>
    /// <param name="player">The champion.</param>
    /// <returns><see langword="true"/> while a channel is running.</returns>
    public static bool IsChanneling(Player player) => Channels.TryGetValue(player, out var channel) && !channel.Cts.IsCancellationRequested;

    /// <summary>Starts the base recall of a champion.</summary>
    /// <param name="player">The champion.</param>
    /// <returns><see langword="true"/> if the channel started.</returns>
    public static ValueTask<bool> StartRecallAsync(Player player)
        => TryStartAsync(player, KindRecall, () => BaseOf(player));

    /// <summary>Starts a channel that ends with a teleport to the given destination.</summary>
    /// <param name="player">The champion.</param>
    /// <param name="kind">The channel kind for the client bar.</param>
    /// <param name="destination">Evaluated when the channel completes; <see langword="null"/> aborts.</param>
    /// <param name="onTeleported">Runs after the champion was teleported (not when the channel was interrupted).</param>
    /// <returns><see langword="true"/> if the channel started.</returns>
    public static async ValueTask<bool> TryStartAsync(Player player, byte kind, Func<Point?> destination, Func<ValueTask>? onTeleported = null)
    {
        if (!player.IsMobaClone || !player.IsAlive || player.IsTeleporting || player.CurrentMap is null || IsChanneling(player))
        {
            return false;
        }

        var channel = new Channel(new CancellationTokenSource());
        Channels.AddOrUpdate(player, channel);
        await player.InvokeViewPlugInAsync<IMobaChannelPlugIn>(p => p.ShowChannelAsync(kind, (int)(ChannelSeconds * 1000))).ConfigureAwait(false);
        _ = RunAsync(player, channel, kind, destination, onTeleported);
        return true;
    }

    /// <summary>Breaks the champion's channel, if any (moved, cast, attacked or was hit).</summary>
    /// <param name="player">The champion.</param>
    public static void Interrupt(Player player)
    {
        if (Channels.TryGetValue(player, out var channel) && !channel.Cts.IsCancellationRequested)
        {
            _ = channel.Cts.CancelAsync();
        }
    }

    /// <summary>Gets the recall point of the champion's team, on a walkable tile next to the shop.</summary>
    /// <param name="player">The champion.</param>
    /// <returns>The point, or <see langword="null"/> if the champion has no team or map.</returns>
    public static Point? BaseOf(Player player)
    {
        var team = MobaTeams.GetTeam(player);
        if (team == MobaTeam.None || player.CurrentMap is not { } map)
        {
            return null;
        }

        var (x, y) = team == MobaTeam.Blue ? BlueBase : RedBase;
        return map.Terrain.GetRandomCoordinate(new Point(x, y), 2);
    }

    private static async Task RunAsync(Player player, Channel channel, byte kind, Func<Point?> destination, Func<ValueTask>? onTeleported)
    {
        var start = player.Position;
        var health = player.Attributes?[Stats.CurrentHealth] ?? 0;
        var completed = false;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(ChannelSeconds);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(PollMs, channel.Cts.Token).ConfigureAwait(false);
                var currentHealth = player.Attributes?[Stats.CurrentHealth] ?? 0;
                if (!player.IsAlive || player.Position != start || currentHealth < health || player.CurrentMap is null)
                {
                    await channel.Cts.CancelAsync().ConfigureAwait(false);
                    break;
                }

                health = currentHealth;
            }

            completed = !channel.Cts.IsCancellationRequested;
        }
        catch (OperationCanceledException)
        {
            // interrupted
        }

        if (!completed)
        {
            await player.InvokeViewPlugInAsync<IMobaChannelPlugIn>(p => p.ShowChannelAsync(kind, 0)).ConfigureAwait(false);
            return;
        }

        try
        {
            await channel.Cts.CancelAsync().ConfigureAwait(false); // marks the channel as finished
            if (player.IsAlive && destination() is { } target)
            {
                var skill = player.GameContext.Configuration.Skills.FirstOrDefault(s => s.Number == TeleportSkillNumber);
                if (skill is not null)
                {
                    await player.TeleportAsync(target, skill).ConfigureAwait(false);
                }
                else
                {
                    await player.MoveAsync(target).ConfigureAwait(false);
                }

                if (onTeleported is not null)
                {
                    await onTeleported().ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            player.Logger.LogWarning(ex, "[MOBA-RECALL] teleport failed");
        }
    }

    private sealed record Channel(System.Threading.CancellationTokenSource Cts);
}
