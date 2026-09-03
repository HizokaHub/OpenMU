// <copyright file="MobaBotFightChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Dev command <c>/mobabotfight [n]</c>: spawns <c>n</c> bots (default 7, one of every
/// family) on BOTH teams so they immediately brawl - <c>n</c> = 1 -&gt; 1v1, 3 -&gt; 3v3,
/// etc. Equivalent to <c>/mobabot blue</c> + <c>/mobabot red</c> for the first n classes.
/// </summary>
[Guid("6C2E9B41-4A83-4F50-8D67-1B0A7E3C5F92")]
[PlugIn]
[Display(Name = "MOBA: bot brawl", Description = "Dev command '/mobabotfight [n]' - spawn an n-vs-n bot fight.")]
[ChatCommandHelp(Command, "Spawn an n-vs-n bot fight (n = 1..7, default 7): /mobabotfight [n]", null)]
public class MobaBotFightChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/mobabotfight";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    public async ValueTask HandleCommandAsync(Player player, string command)
    {
        var all = MobaBotChatCommandPlugIn.AllFamilies;

        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var n = parts.Length > 1 && int.TryParse(parts[1], out var parsed)
            ? Math.Clamp(parsed, 1, all.Length)
            : all.Length;

        // A bot fight needs objectives to play around: spawn the turrets + nexuses on the
        // ARENA map (not wherever the caller happens to stand) if it doesn't have them,
        // otherwise the bots just brawl their way to the enemy spawn.
        var structures = 0;
        var arena = await player.GameContext.GetMapAsync(MobaCloneFactory.ArenaMapNumber).ConfigureAwait(false);
        if (arena is not null)
        {
            if (!MobaStructureSpawner.HasTurrets(arena.MapId))
            {
                structures += await MobaStructureSpawner.SpawnTurretsAsync(arena, player.GameContext).ConfigureAwait(false);
            }

            if (!MobaStructureSpawner.HasNexuses(arena.MapId))
            {
                structures += await MobaStructureSpawner.SpawnNexusesAsync(arena, player.GameContext).ConfigureAwait(false);
            }
        }

        player.Logger.LogInformation("[MOBA-BOT] /mobabotfight {N}v{N}: spawned {S} structures on arena.", n, structures);

        var families = all.Take(n).ToList();
        var blue = await MobaBotChatCommandPlugIn.SpawnAsync(player, MobaTeam.Blue, families).ConfigureAwait(false);
        var red = await MobaBotChatCommandPlugIn.SpawnAsync(player, MobaTeam.Red, families).ConfigureAwait(false);
        await player.ShowBlueMessageAsync(
            $"[mobabotfight] {n}v{n}: {blue} azules + {red} rojos + {structures} estructuras. Objetivo: destruir el nexo enemigo. /move {player.SelectedCharacter?.Name} 200 116 128 . /mobabotclear para terminar.").ConfigureAwait(false);
    }
}
