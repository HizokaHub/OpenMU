// <copyright file="MobaStatsChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Dev command <c>/mobastats</c>: prints the caller's MOBA champion final attributes -
/// invested stats, resources, damage, defense, attack speed and a couple of sample skill
/// damage rolls - so the stat economy and per-race scaling can be verified in game.
/// </summary>
[Guid("2E5B8C10-7F42-4A93-B1D6-9C0E7A4F3B28")]
[PlugIn]
[Display(Name = "MOBA: print champion stats", Description = "Dev command '/mobastats' - print your MOBA champion's final attributes.")]
[ChatCommandHelp(Command, "Print your MOBA champion's final attributes.", null)]
public class MobaStatsChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/mobastats";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    public async ValueTask HandleCommandAsync(Player player, string command)
    {
        if (!player.IsMobaClone || player.Attributes is not { } attr || player.SelectedCharacter is not { } character)
        {
            await player.ShowBlueMessageAsync("[mobastats] Solo dentro de una partida MOBA.").ConfigureAwait(false);
            return;
        }

        var family = MobaPassives.FamilyOf(player);
        var lines = new List<string>
        {
            $"[mobastats] {character.Name} · {character.CharacterClass?.Name} ({family}) · nivel {player.MobaLevel}",
            $"  puntos: stats={character.LevelUpPoints:N0} restantes · skill={player.MobaSkillPoints}",
            $"  STR {Stat(attr, Stats.BaseStrength, Stats.TotalStrength)}  AGI {Stat(attr, Stats.BaseAgility, Stats.TotalAgility)}",
            $"  ENE {Stat(attr, Stats.BaseEnergy, Stats.TotalEnergy)}  VIT {Stat(attr, Stats.BaseVitality, Stats.TotalVitality)}",
        };

        if (character.CharacterClass?.StatAttributes.Any(a => a.Attribute == Stats.BaseLeadership) == true)
        {
            lines.Add($"  CMD {Stat(attr, Stats.BaseLeadership, Stats.TotalLeadership)}");
        }

        lines.Add($"  HP {attr[Stats.CurrentHealth]:N0}/{attr[Stats.MaximumHealth]:N0}  Mana {attr[Stats.CurrentMana]:N0}/{attr[Stats.MaximumMana]:N0}  SD {attr[Stats.CurrentShield]:N0}/{attr[Stats.MaximumShield]:N0}");
        lines.Add($"  dmg fis {attr[Stats.MinimumPhysBaseDmg]:F0}-{attr[Stats.MaximumPhysBaseDmg]:F0}  mag {attr[Stats.MinimumWizBaseDmg]:F0}-{attr[Stats.MaximumWizBaseDmg]:F0}");
        lines.Add($"  def {attr[Stats.DefenseBase]:F0}  velAtk {attr[Stats.AttackSpeedAny]:F0}");

        // Sample MOBA skill damage: primary stat term + a mid skill and a heavy skill.
        var primary = family switch
        {
            MobaFamily.Knight or MobaFamily.RageFighter => Stats.TotalStrength,
            MobaFamily.Elf => Stats.TotalAgility,
            MobaFamily.DarkLord => Stats.TotalLeadership,
            _ => Stats.TotalEnergy,
        };
        var invested = Math.Max(0, attr[primary] - MobaCloneFactory.BaselineStatValue);
        lines.Add($"  stat primario ({primary.Designation}) = {attr[primary]:F0}  (invertido {invested:F0}/{MobaStatEconomy.MaxPerStat:N0}, escala {Math.Clamp(invested / MobaStatEconomy.MaxPerStat, 0f, 1f) * 100f:F0}%)");

        foreach (var (number, label) in SampleSkills(family))
        {
            // Use the skill's real learned rank so the numbers match what the server rolls
            // in combat, not a fixed reference rank.
            var rank = character.LearnedSkills.FirstOrDefault(s => s.Skill?.Number == number)?.Level ?? 0;
            MobaSkillDamage.GetSkillBaseDamage(player, number, rank, out var min, out var max);
            lines.Add($"    {label} (r{Math.Max(1, rank)}): {min}-{max}");
        }

        MobaSkillDamage.GetBasicAttackDamage(player, out var bmin, out var bmax);
        lines.Add($"    ataque basico: {bmin}-{bmax}");

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            sb.AppendLine(line);
        }

        await player.ShowBlueMessageAsync(sb.ToString().TrimEnd()).ConfigureAwait(false);
    }

    private static string Stat(IAttributeSystem attr, AttributeDefinition baseStat, AttributeDefinition totalStat)
    {
        var b = attr[baseStat];
        var t = attr[totalStat];
        return Math.Abs(b - t) < 0.5f
            ? b.ToString("N0", CultureInfo.InvariantCulture)
            : $"{b:N0} (tot {t:N0})";
    }

    private static IEnumerable<(short Number, string Label)> SampleSkills(MobaFamily family) => family switch
    {
        MobaFamily.Knight => new[] { ((short)41, "Twisting Slash"), ((short)232, "Strike of Destruction") },
        MobaFamily.Elf => new[] { ((short)24, "Triple Shot"), ((short)51, "Ice Arrow") },
        MobaFamily.DarkLord => new[] { ((short)60, "Force"), ((short)65, "Electric Spike") },
        MobaFamily.RageFighter => new[] { ((short)260, "Killing Blow"), ((short)263, "Dark Side") },
        MobaFamily.Summoner => new[] { ((short)214, "Drain Life"), ((short)215, "Chain Lightning") },
        _ => new[] { ((short)17, "Energy Ball"), ((short)4, "Fire Ball") },
    };
}
