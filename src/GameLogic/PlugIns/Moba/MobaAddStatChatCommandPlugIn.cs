// <copyright file="MobaAddStatChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Dev command <c>/mobaadd &lt;stat&gt; [amount]</c>: invests MOBA stat points into a
/// champion stat, spending <see cref="DataModel.Entities.Character.LevelUpPoints"/> and
/// enforcing the per-stat cap (<see cref="MobaStatEconomy.MaxPerStat"/> on top of the flat
/// clone baseline). The vanilla per-point <c>+</c> button / <c>/addstr</c> ignore both, so
/// this is the MOBA way to distribute the stat build.
/// </summary>
[Guid("9B41E7C2-0D63-4F58-8A19-5E2C7B0F6A34")]
[PlugIn]
[Display(Name = "MOBA: invest stat points", Description = "Dev command '/mobaadd <stat> [amount]'.")]
[ChatCommandHelp(Command, "Invest MOBA stat points: /mobaadd <str|agi|ene|vit|cmd> [amount]", typeof(MobaAddStatChatCommandArgs))]
public class MobaAddStatChatCommandPlugIn : ChatCommandPlugInBase<MobaAddStatChatCommandArgs>
{
    private const string Command = "/mobaadd";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaAddStatChatCommandArgs arguments)
    {
        if (!player.IsMobaClone || player.Attributes is not { } attributes || player.SelectedCharacter is not { } character)
        {
            await player.ShowBlueMessageAsync("[mobaadd] Solo dentro de una partida MOBA.").ConfigureAwait(false);
            return;
        }

        var baseStat = ResolveStat(arguments.Stat);
        if (baseStat is null)
        {
            await player.ShowBlueMessageAsync("[mobaadd] Stat inválido. Usá: str, agi, ene, vit o cmd.").ConfigureAwait(false);
            return;
        }

        var statDef = character.CharacterClass?.GetStatAttribute(baseStat);
        if (statDef is not { IncreasableByPlayer: true })
        {
            await player.ShowBlueMessageAsync($"[mobaadd] {character.CharacterClass?.Name} no tiene ese stat.").ConfigureAwait(false);
            return;
        }

        var amount = arguments.Amount;
        if (amount < 1)
        {
            await player.ShowBlueMessageAsync("[mobaadd] La cantidad tiene que ser mayor que 0.").ConfigureAwait(false);
            return;
        }

        var available = (int)Math.Max(0, character.LevelUpPoints);
        if (available <= 0)
        {
            await player.ShowBlueMessageAsync("[mobaadd] No te quedan puntos de stats.").ConfigureAwait(false);
            return;
        }

        // Cap: invested points (current base minus the flat clone baseline) may not exceed MaxPerStat.
        var current = attributes[baseStat];
        var invested = (int)Math.Round(current - MobaCloneFactory.BaselineStatValue);
        var roomInStat = Math.Max(0, MobaStatEconomy.MaxPerStat - invested);
        if (roomInStat <= 0)
        {
            await player.ShowBlueMessageAsync($"[mobaadd] {arguments.Stat!.ToUpperInvariant()} ya está al tope ({MobaStatEconomy.MaxPerStat:N0}).").ConfigureAwait(false);
            return;
        }

        var applied = Math.Min(amount, Math.Min(available, roomInStat));
        attributes[baseStat] += applied;
        character.LevelUpPoints -= applied;

        var newInvested = invested + applied;
        await player.ShowBlueMessageAsync(
            $"[mobaadd] +{applied:N0} {arguments.Stat!.ToUpperInvariant()} → invertido {newInvested:N0}/{MobaStatEconomy.MaxPerStat:N0} · quedan {character.LevelUpPoints:N0} puntos. (/mobastats)")
            .ConfigureAwait(false);
    }

    private static AttributeDefinition? ResolveStat(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "str" or "strength" or "fuerza" => Stats.BaseStrength,
        "agi" or "agility" or "dex" or "destreza" => Stats.BaseAgility,
        "ene" or "energy" or "energia" => Stats.BaseEnergy,
        "vit" or "vitality" or "vitalidad" => Stats.BaseVitality,
        "cmd" or "command" or "lead" or "leadership" or "comando" => Stats.BaseLeadership,
        _ => null,
    };
}
