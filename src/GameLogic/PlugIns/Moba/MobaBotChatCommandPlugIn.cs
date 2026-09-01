// <copyright file="MobaBotChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.Pathfinding;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Dev command <c>/mobabot &lt;blue|red&gt; &lt;class|all&gt; [count]</c>: spawns
/// server-driven champion bots near the caller for skill / balance testing. They walk to
/// the nearest enemy and cycle their loadout; watch the <c>[MOBA-DMG]</c> log.
/// <c>/mobabotclear</c> removes them.
/// </summary>
[Guid("3D9A6E82-1B47-4C05-8F62-9A0E7C3B1D54")]
[PlugIn]
[Display(Name = "MOBA: spawn test bots", Description = "Dev command '/mobabot <blue|red> <class|all> [count]'.")]
[ChatCommandHelp(Command, "Spawn MOBA champion test bots: /mobabot <blue|red> <class|all> [count]", typeof(MobaBotChatCommandArgs))]
public class MobaBotChatCommandPlugIn : ChatCommandPlugInBase<MobaBotChatCommandArgs>
{
    private const string Command = "/mobabot";

    /// <summary>One representative class per family (Wizard, BK, HE, MG, LE, Summoner, RF).</summary>
    internal static readonly byte[] AllFamilies = { 0, 6, 11, 12, 17, 20, 24 };


    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <summary>Resolves a class alias (or raw number) to a character-class number.</summary>
    /// <param name="value">The alias or number.</param>
    /// <returns>The class number, or <c>null</c>.</returns>
    internal static byte? ResolveClassNumber(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "dw" or "wizard" or "darkwizard" => 0,
        "sm" or "soulmaster" => 2,
        "gm" or "grandmaster" => 3,
        "dk" or "knight" or "darkknight" => 4,
        "bk" or "bladeknight" => 6,
        "bm" or "blademaster" => 7,
        "fe" or "elf" or "fairyelf" => 8,
        "me" or "muse" or "museelf" => 10,
        "he" or "highelf" => 11,
        "mg" or "magicgladiator" or "gladiator" => 12,
        "dl" or "darklord" or "lord" => 16,
        "le" or "lordemperor" => 17,
        "sum" or "summoner" => 20,
        "bs" or "bloodysummoner" => 22,
        "dim" or "dimensionmaster" => 23,
        "rf" or "ragefighter" or "rage" => 24,
        "fm" or "fistmaster" => 25,
        _ => byte.TryParse(value, out var n) ? n : (byte?)null,
    };

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaBotChatCommandArgs arguments)
    {
        var team = arguments.ResolveTeam();

        var classNumbers = string.Equals(arguments.Class?.Trim(), "all", StringComparison.OrdinalIgnoreCase)
            ? AllFamilies.ToList()
            : Enumerable.Repeat(ResolveClassNumber(arguments.Class) ?? byte.MaxValue, Math.Clamp(arguments.Count, 1, 8)).ToList();

        if (classNumbers.Contains(byte.MaxValue))
        {
            await player.ShowBlueMessageAsync("[mobabot] Clase desconocida. Ej: rf, sum, dw, dk, fe, mg, dl, o 'all'.").ConfigureAwait(false);
            return;
        }

        var spawned = await SpawnAsync(player, team, classNumbers).ConfigureAwait(false);
        var origin = team == MobaTeam.Blue ? BlueBrawlOrigin : RedBrawlOrigin;
        await player.ShowBlueMessageAsync($"[mobabot] {spawned} bot(s) {team} en la arena ~({origin.X},{origin.Y}). Mirá con: /move {player.SelectedCharacter?.Name} 200 116 128").ConfigureAwait(false);
    }

    /// <summary>Spawns the given classes as bots on a team, near the caller.</summary>
    /// <param name="caller">The GM running the command.</param>
    /// <param name="team">The team.</param>
    /// <param name="classNumbers">The character-class numbers to spawn.</param>
    /// <returns>The number of bots spawned.</returns>
    /// <summary>Brawl spawn on the carved mid lane (x = 108..124 walkable): blue north, red south, ~36 tiles apart so they march toward each other and the fight is watchable.</summary>
    internal static readonly Point BlueBrawlOrigin = new(116, 118);

    /// <summary>Red team's brawl origin (see <see cref="BlueBrawlOrigin"/>).</summary>
    internal static readonly Point RedBrawlOrigin = new(116, 140);

    internal static async ValueTask<int> SpawnAsync(Player caller, MobaTeam team, IReadOnlyList<byte> classNumbers)
    {
        var config = caller.GameContext.Configuration;

        // Always spawn at a fixed spot in the arena (not next to the caller, who may not
        // even be on the arena map), so the fight always happens where it can be found.
        var origin = team == MobaTeam.Blue ? BlueBrawlOrigin : RedBrawlOrigin;

        var spawned = 0;
        for (var i = 0; i < classNumbers.Count; i++)
        {
            var characterClass = config.CharacterClasses.FirstOrDefault(c => c.Number == classNumbers[i]);
            if (characterClass is null)
            {
                continue;
            }

            // Character names are capped at 10 bytes in the scope packet - a longer name
            // makes INewPlayersInScopePlugIn throw and the bot never renders for anyone.
            var tag = team == MobaTeam.Blue ? "b" : "r";
            var name = $"{tag}{classNumbers[i]}_{((DateTime.UtcNow.Ticks / 1000) % 10000) + i}";
            if (name.Length > 10)
            {
                name = name[..10];
            }

            var clone = await MobaCloneFactory.BuildForClassAsync(caller, characterClass, name).ConfigureAwait(false);
            var account = caller.PersistenceContext.CreateNew<Account>();
            account.LoginName = $"#bot_{name}";

            var spawn = new Point(
                (byte)Math.Clamp(origin.X + ((i % 4) * 2) - 3, 5, 250),
                (byte)Math.Clamp(origin.Y + ((i / 4) * 2), 5, 250));

            var bot = new MobaBotPlayer(caller.GameContext, team);
            if (await bot.StartMobaAsync(account, clone, spawn).ConfigureAwait(false))
            {
                spawned++;
            }
        }

        return spawned;
    }
}
