// <copyright file="MobaManaReport.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.IO;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix;
using MUnique.OpenMU.Persistence.InMemory;

/// <summary>
/// Sizing helper for the MOBA mana economy (not part of the regular suite): per class, the real mana cost
/// of every loadout skill (x<see cref="MobaMana.CostMultiplier"/>), its cooldown, the drain of casting the
/// whole loadout on cooldown, and the regeneration needed so the champion is never out of mana for more
/// than <see cref="MobaMana.MaxDryWaitSeconds"/> seconds. Output goes to <c>moba-mana.txt</c> in the temp folder.
/// </summary>
[TestFixture]
[Explicit("Balance report, run by hand.")]
public class MobaManaReport
{
    /// <summary>Writes the mana report.</summary>
    /// <returns>A task.</returns>
    [Test]
    public async ValueTask WriteReportAsync()
    {
        var contextProvider = new InMemoryPersistenceContextProvider();
        await new DataInitialization(contextProvider, new NullLoggerFactory()).CreateInitialDataAsync(3, true).ConfigureAwait(false);
        var config = (await contextProvider.CreateNewConfigurationContext().GetAsync<GameConfiguration>().ConfigureAwait(false)).First();
        var sb = new StringBuilder();
        foreach (var classNumber in new byte[] { 3, 7, 11, 13, 17, 23, 25 })
        {
            var characterClass = config.CharacterClasses.First(c => c.Number == classNumber);
            sb.AppendLine($"=== {characterClass.Name} ===");
            double drain = 0;
            double maxCost = 0;
            foreach (var number in MobaLoadouts.SkillNumbersFor(characterClass))
            {
                var skill = config.Skills.FirstOrDefault(s => s.Number == number);
                var baseCost = skill?.ConsumeRequirements.FirstOrDefault(r => r.Attribute == Stats.CurrentMana)?.MinimumValue ?? 0;
                var cost = baseCost * MobaMana.CostMultiplier;
                var cd = MobaCooldowns.SecondsOf(number, 1);
                drain += cost / cd;
                maxCost = Math.Max(maxCost, cost);
                sb.AppendLine($"  #{number} {skill?.Name} baseMana={baseCost} cost={cost} cd(rank1)={cd:F1}s -> {cost / cd:F1} mana/s");
            }

            sb.AppendLine($"  full-rotation drain = {drain:F1} mana/s, most expensive skill = {maxCost}, regen for {MobaMana.MaxDryWaitSeconds:F0}s dry wait = {maxCost / MobaMana.MaxDryWaitSeconds:F1}/s");
        }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "moba-mana.txt"), sb.ToString());
    }
}
