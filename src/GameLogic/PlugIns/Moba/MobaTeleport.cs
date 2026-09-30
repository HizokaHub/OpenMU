// <copyright file="MobaTeleport.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.Inventory;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The teleport scroll (the Town Portal Scroll item, 14/10): using it lets the champion pick an
/// <b>allied lane minion</b> by clicking it in the world; after the same 5 s channel as the recall
/// (<see cref="MobaRecall"/>) the champion is teleported next to that minion. The scroll is only
/// consumed when the teleport happens, and every teleport puts all scrolls on a
/// <see cref="CooldownMinutes"/> minute cooldown.
/// </summary>
public static class MobaTeleport
{
    /// <summary>The group of the teleport scroll item.</summary>
    public const byte ScrollGroup = 14;

    /// <summary>The number of the teleport scroll item.</summary>
    public const short ScrollNumber = 10;

    /// <summary>Minutes before another scroll can be used.</summary>
    public const double CooldownMinutes = 5;

    /// <summary>Seconds the champion has to click a minion after using the scroll.</summary>
    public const double TargetingSeconds = 15;

    /// <summary>The channel kind the client uses to enter / leave the minion-targeting mode (duration 1 = on, 0 = off).</summary>
    public const byte KindTargeting = 3;

    private static readonly ConditionalWeakTable<Player, State> States = new();

    /// <summary>Starts the minion targeting for a scroll the champion used.</summary>
    /// <param name="player">The champion.</param>
    /// <param name="scroll">The scroll item.</param>
    /// <returns>A task.</returns>
    public static async ValueTask BeginTargetingAsync(Player player, Item scroll)
    {
        var state = States.GetOrCreateValue(player);
        var now = DateTime.UtcNow;
        if (now < state.CooldownUntilUtc)
        {
            var left = (int)Math.Ceiling((state.CooldownUntilUtc - now).TotalSeconds);
            await player.ShowBlueMessageAsync($"[MOBA] Teletransporte en enfriamiento: {left} s.").ConfigureAwait(false);
            return;
        }

        if (!player.IsAlive || MobaRecall.IsChanneling(player))
        {
            return;
        }

        state.Scroll = scroll;
        state.TargetingUntilUtc = now.AddSeconds(TargetingSeconds);
        player.Logger.LogInformation("[MOBA-TP] \"{Name}\" scroll used, waiting for a minion click ({Seconds}s).", player.SelectedCharacter?.Name, TargetingSeconds);
        await player.InvokeViewPlugInAsync<IMobaChannelPlugIn>(p => p.ShowChannelAsync(KindTargeting, 1)).ConfigureAwait(false);
        await player.ShowBlueMessageAsync("[MOBA] Haz clic en un minion aliado para teletransportarte a él.").ConfigureAwait(false);
    }

    /// <summary>The champion clicked a monster while targeting: teleports to it if it is an allied, living minion.</summary>
    /// <param name="player">The champion.</param>
    /// <param name="targetId">The object id of the clicked monster.</param>
    /// <returns>A task.</returns>
    public static async ValueTask SelectTargetAsync(Player player, ushort targetId)
    {
        if (!States.TryGetValue(player, out var state) || state.Scroll is not { } scroll || DateTime.UtcNow > state.TargetingUntilUtc)
        {
            player.Logger.LogInformation("[MOBA-TP] \"{Name}\" minion click {Id} ignored: not targeting (or timed out).", player.SelectedCharacter?.Name, targetId);
            return;
        }

        await EndTargetingAsync(player, state).ConfigureAwait(false);
        if (player.CurrentMap?.GetObject(targetId) is not Monster { IsAlive: true } minion
            || !MobaTeams.AreAllies(player, minion))
        {
            player.Logger.LogInformation("[MOBA-TP] \"{Name}\" clicked {Id}: not a living allied minion.", player.SelectedCharacter?.Name, targetId);
            await player.ShowBlueMessageAsync("[MOBA] Ese no es un minion aliado.").ConfigureAwait(false);
            return;
        }

        player.Logger.LogInformation("[MOBA-TP] \"{Name}\" channelling teleport to minion {Id} @ {Pos}.", player.SelectedCharacter?.Name, targetId, minion.Position);

        await MobaRecall.TryStartAsync(
            player,
            MobaRecall.KindTeleport,
            () => minion.IsAlive && minion.CurrentMap is { } map ? map.Terrain.GetRandomCoordinate(minion.Position, 2) : null,
            async () =>
            {
                player.Logger.LogInformation("[MOBA-TP] \"{Name}\" teleported next to minion {Id} @ {Pos}.", player.SelectedCharacter?.Name, targetId, player.Position);
                state.CooldownUntilUtc = DateTime.UtcNow.AddMinutes(CooldownMinutes);
                await ConsumeAsync(player, scroll).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    private static async ValueTask EndTargetingAsync(Player player, State state)
    {
        state.TargetingUntilUtc = DateTime.MinValue;
        await player.InvokeViewPlugInAsync<IMobaChannelPlugIn>(p => p.ShowChannelAsync(KindTargeting, 0)).ConfigureAwait(false);
    }

    private static async ValueTask ConsumeAsync(Player player, Item scroll)
    {
        if (scroll.Durability > 0)
        {
            scroll.Durability -= 1;
        }

        if (scroll.Durability <= 0)
        {
            await player.DestroyInventoryItemAsync(scroll).ConfigureAwait(false);
        }
        else
        {
            await player.InvokeViewPlugInAsync<IItemDurabilityChangedPlugIn>(p => p.ItemDurabilityChangedAsync(scroll, true)).ConfigureAwait(false);
        }
    }

    private sealed class State
    {
        public Item? Scroll;

        public DateTime TargetingUntilUtc = DateTime.MinValue;

        public DateTime CooldownUntilUtc = DateTime.MinValue;
    }
}
