// <copyright file="MobaDuelChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>Arguments for <c>/mobaduel [what] [duels] [pass] [parallel]</c>.</summary>
public class MobaDuelChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets what to duel: "all" (default, the 7 classes against each other and themselves), "a-b" (one cross, e.g. bk-rf), a class alias (that class against the others) or "stop".</summary>
    [Argument("what", false)]
    public string? What { get; set; }

    /// <summary>Gets or sets the duels of every cross (default 10).</summary>
    [Argument("duels", false)]
    public int Duels { get; set; } = 10;

    /// <summary>Gets or sets the pass: "both" (default), "plain" (no items) or "t3" (full tier 3 build).</summary>
    [Argument("pass", false)]
    public string? Pass { get; set; }

    /// <summary>Gets or sets how many duels run at once (default 6).</summary>
    [Argument("parallel", false)]
    public int Parallel { get; set; } = 6;
}

/// <summary>
/// GM command <c>/mobaduel [all|a-b|class|stop] [duels] [both|plain|t3] [parallel]</c>: runs the 1v1 duels between the classes
/// (<see cref="MobaDuel"/>) and writes <c>duelos_*.json</c> next to the server. Run it inside the arena; it removes the bots and dummies of the arena.
/// The progress is in the server log (<c>[MOBA-DUEL]</c>).
/// </summary>
[Guid("5D2E8A71-9C34-4B6F-A1D0-3E7B4C8F1A92")]
[PlugIn]
[Display(Name = "MOBA: duels (GM)", Description = "'/mobaduel [all|a-b|class|stop] [duels] [both|plain|t3] [parallel]' - 1v1 duels between the classes with the real bot AI, without a client.")]
[ChatCommandHelp(Command, "GM test: 1v1 duels between the classes at level 30 with the real bot AI, no client: /mobaduel [all|a-b|class|stop] [duels] [both|plain|t3] [parallel].", typeof(MobaDuelChatCommandArgs))]
public class MobaDuelChatCommandPlugIn : ChatCommandPlugInBase<MobaDuelChatCommandArgs>
{
    private const string Command = "/mobaduel";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaDuelChatCommandArgs arguments)
    {
        var what = arguments.What?.Trim().ToLowerInvariant();
        if (what == "stop")
        {
            MobaDuel.Cancel();
            await player.ShowBlueMessageAsync("[mobaduel] Parando después de los duelos en curso.").ConfigureAwait(false);
            return;
        }

        if (!player.IsMobaClone || player.CurrentMap is null)
        {
            await player.ShowBlueMessageAsync("[mobaduel] Solo dentro de una partida MOBA (arena).").ConfigureAwait(false);
            return;
        }

        if (MobaDuel.IsRunning || MobaSkillProbe.IsRunning)
        {
            await player.ShowBlueMessageAsync("[mobaduel] Ya hay una prueba corriendo.").ConfigureAwait(false);
            return;
        }

        IReadOnlyList<(byte A, byte B)> pairs;
        if (string.IsNullOrEmpty(what) || what == "all")
        {
            pairs = MobaDuel.AllPairs(MobaDuel.MainClasses);
        }
        else if (what.Contains('-', StringComparison.Ordinal))
        {
            var parts = what.Split('-', 2);
            if (MobaBotChatCommandPlugIn.ResolveClassNumber(parts[0]) is not { } a || MobaBotChatCommandPlugIn.ResolveClassNumber(parts[1]) is not { } b)
            {
                await player.ShowBlueMessageAsync("[mobaduel] Cruce desconocido. Ej: bk-rf.").ConfigureAwait(false);
                return;
            }

            pairs = new[] { (a, b) };
        }
        else if (MobaBotChatCommandPlugIn.ResolveClassNumber(what) is { } one)
        {
            pairs = MobaDuel.MainClasses.Select(other => (one, other)).ToList();
        }
        else
        {
            await player.ShowBlueMessageAsync("[mobaduel] Clase desconocida. Ej: all, bk-rf, bk, dw, fe, mg, dl, sum, rf, stop.").ConfigureAwait(false);
            return;
        }

        var pass = arguments.Pass?.Trim().ToLowerInvariant();
        var plain = pass is null or "" or "both" or "plain";
        var t3 = pass is null or "" or "both" or "t3";
        var request = new MobaDuelRequest(pairs, Math.Clamp(arguments.Duels, 1, 100), plain, t3, MobaLevels.MaxLevel, Math.Clamp(arguments.Parallel, 1, 12));
        await player.ShowBlueMessageAsync($"[mobaduel] Empieza: {pairs.Count} cruces x {request.Duels} duelos, pasadas: {(plain ? "sin ítems " : string.Empty)}{(t3 ? "T3" : string.Empty)}. Mira el log [MOBA-DUEL]; el informe queda en duelos_*.json.").ConfigureAwait(false);

        // runs in the background: the chat command must not block the game loop
        _ = Task.Run(async () =>
        {
            try
            {
                var paths = await MobaDuel.RunAsync(player, request).ConfigureAwait(false);
                await player.ShowBlueMessageAsync($"[mobaduel] Listo: {string.Join(", ", paths)}").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                player.Logger.LogError(ex, "[MOBA-DUEL] failed");
                await player.ShowBlueMessageAsync($"[mobaduel] Falló: {ex.Message}").ConfigureAwait(false);
            }
        });
    }
}
