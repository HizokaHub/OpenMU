// <copyright file="MobaSkillTestChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>Arguments for <c>/mobaskilltest [class|all] [level]</c>.</summary>
public class MobaSkillTestChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets the class alias ("dw", "bk", "fe", "mg", "dl", "sum", "rf") or "all" (default).</summary>
    [Argument("class", false)]
    public string? Class { get; set; }

    /// <summary>Gets or sets the champion level of the caster and the targets (default 30).</summary>
    [Argument("level", false)]
    public int Level { get; set; } = 30;
}

/// <summary>
/// GM command <c>/mobaskilltest [class|all] [level]</c>: runs the server-side skill review (<see cref="MobaSkillProbe"/>) and writes a JSON report.
/// Run it inside the arena; it removes the bots and dummies of the arena. The progress is in the server log (<c>[MOBA-PROBE]</c>).
/// </summary>
[Guid("8B1D6F32-2E74-4C59-A0B3-7F5C9E2D4A16")]
[PlugIn]
[Display(Name = "MOBA: skill test (GM)", Description = "'/mobaskilltest [class|all] [level]' - measures every skill of a class on dummies, without a client.")]
[ChatCommandHelp(Command, "GM test: measure every skill of a class (or all classes) on dummies and write a JSON report: /mobaskilltest [class|all] [level].", typeof(MobaSkillTestChatCommandArgs))]
public class MobaSkillTestChatCommandPlugIn : ChatCommandPlugInBase<MobaSkillTestChatCommandArgs>
{
    private const string Command = "/mobaskilltest";

    // dw (Grand Master), bk, high elf, magic gladiator, dark lord, summoner, rage fighter; 4 and 7 only for the combo definition.
    private static readonly byte[] AllClasses = { 3, 6, 11, 12, 16, 20, 24, 4, 7 };

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaSkillTestChatCommandArgs arguments)
    {
        if (!player.IsMobaClone || player.CurrentMap is null)
        {
            await player.ShowBlueMessageAsync("[mobaskilltest] Solo dentro de una partida MOBA (arena).").ConfigureAwait(false);
            return;
        }

        if (MobaSkillProbe.IsRunning)
        {
            await player.ShowBlueMessageAsync("[mobaskilltest] Ya hay una prueba corriendo.").ConfigureAwait(false);
            return;
        }

        var which = arguments.Class?.Trim().ToLowerInvariant();
        IReadOnlyList<byte> classes;
        if (string.IsNullOrEmpty(which) || which == "all")
        {
            classes = AllClasses;
        }
        else if (MobaBotChatCommandPlugIn.ResolveClassNumber(which) is { } number)
        {
            classes = new[] { number };
        }
        else
        {
            await player.ShowBlueMessageAsync("[mobaskilltest] Clase desconocida. Ej: dw, bk, fe, mg, dl, sum, rf o all.").ConfigureAwait(false);
            return;
        }

        var level = Math.Clamp(arguments.Level, 1, MobaLevels.MaxLevel);
        await player.ShowBlueMessageAsync($"[mobaskilltest] Empieza ({classes.Count} clase(s), nivel {level}). Mira el log [MOBA-PROBE]; el informe queda en un archivo skilltest_*.json.").ConfigureAwait(false);

        // runs in the background: the chat command must not block the game loop
        _ = Task.Run(async () =>
        {
            try
            {
                var path = await MobaSkillProbe.RunAsync(player, classes, level).ConfigureAwait(false);
                await player.ShowBlueMessageAsync($"[mobaskilltest] Listo: {path}").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                player.Logger.LogError(ex, "[MOBA-PROBE] failed");
                await player.ShowBlueMessageAsync($"[mobaskilltest] Falló: {ex.Message}").ConfigureAwait(false);
            }
        });
    }
}
