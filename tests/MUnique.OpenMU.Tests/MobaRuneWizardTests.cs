// <copyright file="MobaRuneWizardTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.IO;
using Moq;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the Rune Wizard family of the MOBA (imported from Season 21): its kit, passive, shop and portrait.
/// </summary>
[TestFixture]
public class MobaRuneWizardTests
{
    private static readonly byte[] RuneWizardClasses = { 28, 30, 31 };

    private string _file = string.Empty;

    /// <summary>Points the loadout store at a temporary file.</summary>
    [SetUp]
    public void SetUp()
    {
        this._file = Path.Combine(Path.GetTempPath(), $"moba-loadouts-{Guid.NewGuid():N}.json");
        MobaLoadoutStore.FilePath = this._file;
        MobaLoadoutStore.Reset();
    }

    /// <summary>Removes the temporary file.</summary>
    [TearDown]
    public void TearDown()
    {
        if (File.Exists(this._file))
        {
            File.Delete(this._file);
        }
    }

    /// <summary>
    /// The class numbers of the family resolve to it and the other families stay as they were.
    /// </summary>
    [Test]
    public void ClassNumbersResolveToTheFamily()
    {
        foreach (var number in RuneWizardClasses)
        {
            Assert.That(MobaPassives.FamilyOf(number), Is.EqualTo(MobaFamily.RuneWizard), $"class {number}");
        }

        Assert.That(MobaPassives.FamilyOf(0), Is.EqualTo(MobaFamily.Wizard));
        Assert.That(MobaPassives.FamilyOf(24), Is.EqualTo(MobaFamily.RageFighter));
    }

    /// <summary>
    /// The default loadout is a valid pick, has no buffs, and the family has enough attacks to choose from.
    /// </summary>
    [Test]
    public void LoadoutIsValid()
    {
        var defaults = MobaLoadouts.DefaultFor(28);
        Assert.That(MobaLoadoutStore.Validate(defaults.ToArray(), 28), Is.Null);
        Assert.That(MobaLoadouts.SelectableFor(28).Count, Is.GreaterThanOrEqualTo(MobaLoadoutStore.MaxSkills + 2));
        Assert.That(MobaLoadouts.SelectableFor(28).Where(MobaLoadouts.IsBuff), Is.Empty);
        Assert.That(MobaLoadouts.BuffsFor(28), Is.EquivalentTo(new short[] { 233, 16 }), "Burst (Expansion of Wizardry) and Soul Barrier");
        Assert.That(MobaLoadouts.BotPicksFor(28).Count, Is.EqualTo(MobaLoadouts.BotSkillCount));
    }

    /// <summary>
    /// The family has no portrait on the client yet: it shows the Dark Wizard's, never the Rage Fighter's (the old clamp).
    /// </summary>
    [Test]
    public void PortraitIsTheWizardsUntilItHasItsOwn()
    {
        foreach (var number in RuneWizardClasses)
        {
            var packed = MobaMinimapKind.Pack(MobaMinimapKind.Champion, number, true);
            Assert.That(MobaMinimapKind.BaseClassIndex(number), Is.EqualTo(0), $"class {number}");
            Assert.That(MobaMinimapKind.ClassOf(packed), Is.EqualTo(0), $"class {number}");
            Assert.That(MobaMinimapKind.IsRecalling(packed), Is.True);
        }
    }

    /// <summary>
    /// The shop sells the family what it sells the Wizard.
    /// </summary>
    [Test]
    public void ShopSellsWhatTheWizardGets()
    {
        static IEnumerable<(MobaShopCategory Category, byte Group, short Number, MobaShopTier Tier, MobaShopVariant Variant)> Of(MobaFamily family)
            => MobaShopCatalog.Entries
                .Where(e => e.Families is { Length: 1 } f && f[0] == family)
                .Select(e => (e.Category, e.Group, e.Number, e.Tier, e.Variant))
                .OrderBy(e => e.ToString());

        Assert.That(Of(MobaFamily.RuneWizard), Is.Not.Empty);
        Assert.That(Of(MobaFamily.RuneWizard), Is.EquivalentTo(Of(MobaFamily.Wizard)));
    }

    /// <summary>
    /// The third spell hit on the same target within the window makes the runes explode; another target or a pause starts the count again.
    /// </summary>
    [Test]
    public async ValueTask ThirdSpellHitOnTheSameTargetDetonatesAsync()
    {
        var player = await PlayerTestHelper.CreatePlayerAsync().ConfigureAwait(false);
        var target = new Mock<IAttackable>().Object;
        var other = new Mock<IAttackable>().Object;
        var now = DateTime.UtcNow;

        Assert.That(MobaRuneMarkPassive.NextHitDetonates(player, target, now), Is.False);
        Assert.That(MobaRuneMarkPassive.NextHitDetonates(player, target, now.AddSeconds(1)), Is.False);
        Assert.That(MobaRuneMarkPassive.NextHitDetonates(player, target, now.AddSeconds(2)), Is.True, "third hit");
        Assert.That(MobaRuneMarkPassive.NextHitDetonates(player, target, now.AddSeconds(3)), Is.False, "the count starts again");

        Assert.That(MobaRuneMarkPassive.NextHitDetonates(player, other, now.AddSeconds(4)), Is.False, "another target");
        Assert.That(MobaRuneMarkPassive.NextHitDetonates(player, other, now.AddSeconds(5)), Is.False);
        Assert.That(MobaRuneMarkPassive.NextHitDetonates(player, other, now.AddSeconds(5 + MobaRuneMarkPassive.MarkWindowSeconds + 1)), Is.False, "the runes faded");
    }
}
