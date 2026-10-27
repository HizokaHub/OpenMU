// <copyright file="RuneWizardClassTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.GameServer.RemoteView;
using MUnique.OpenMU.Persistence.Initialization.CharacterClasses;
using MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix;
using MUnique.OpenMU.Persistence.InMemory;

/// <summary>
/// Tests the rune wizard class (imported from Season 21), which is added to a configuration by <see cref="RuneWizardClassFactory"/>.
/// </summary>
[TestFixture]
public class RuneWizardClassTests
{
    private const byte RuneWizardNumber = 28;

    private InMemoryPersistenceContextProvider _contextProvider = null!;

    private GameConfiguration _configuration = null!;

    /// <summary>
    /// Creates the standard Season 6 configuration in memory.
    /// </summary>
    /// <returns>A task.</returns>
    [SetUp]
    public async ValueTask SetUpAsync()
    {
        this._contextProvider = new InMemoryPersistenceContextProvider();
        await new DataInitialization(this._contextProvider, new NullLoggerFactory()).CreateInitialDataAsync(3, true).ConfigureAwait(false);
        this._configuration = (await this._contextProvider.CreateNewConfigurationContext().GetAsync<GameConfiguration>().ConfigureAwait(false)).First();
    }

    /// <summary>
    /// The standard initialization doesn't create the rune wizard: databases stay unchanged until an update is approved.
    /// </summary>
    [Test]
    public void StandardInitializationDoesNotCreateIt()
    {
        Assert.That(this._configuration.CharacterClasses.Any(c => c.Number == RuneWizardNumber), Is.False);
    }

    /// <summary>
    /// The factory creates a class with the agreed number and a mage's attributes.
    /// </summary>
    [Test]
    public void FactoryCreatesTheClass()
    {
        var context = this._contextProvider.CreateNewConfigurationContext();
        var runeWizard = RuneWizardClassFactory.AddTo(context, this._configuration);

        Assert.That(runeWizard.Number, Is.EqualTo(RuneWizardNumber));
        Assert.That(runeWizard.Name.ToString(), Is.EqualTo("Rune Wizard"));
        Assert.That(runeWizard.CanGetCreated, Is.True);
        Assert.That(runeWizard.CreationAllowedFlag, Is.EqualTo(16));
        Assert.That(runeWizard.HomeMap?.Number, Is.EqualTo(0), "starts in Lorencia like the other classes");
        Assert.That(this._configuration.CharacterClasses, Does.Contain(runeWizard));

        var darkWizard = this._configuration.CharacterClasses.First(c => c.Number == 0);
        Assert.That(runeWizard.AttributeCombinations.Count, Is.EqualTo(darkWizard.AttributeCombinations.Count), "same formulas as the dark wizard");
        Assert.That(runeWizard.StatAttributes.Count, Is.EqualTo(darkWizard.StatAttributes.Count));
    }

    /// <summary>
    /// Adding the class twice returns the same one.
    /// </summary>
    [Test]
    public void FactoryIsIdempotent()
    {
        var context = this._contextProvider.CreateNewConfigurationContext();
        var first = RuneWizardClassFactory.AddTo(context, this._configuration);
        var second = RuneWizardClassFactory.AddTo(context, this._configuration);

        Assert.That(second, Is.SameAs(first));
        Assert.That(this._configuration.CharacterClasses.Count(c => c.Number == RuneWizardNumber), Is.EqualTo(1));
    }

    /// <summary>
    /// The class flags resolve to the class when it exists and are skipped when it doesn't (old configurations).
    /// </summary>
    [Test]
    public void ClassFlagsTolerateTheMissingClass()
    {
        Assert.That(this._configuration.DetermineCharacterClasses(CharacterClasses.All).Count(), Is.EqualTo(18), "without it, as before");

        var runeWizard = RuneWizardClassFactory.AddTo(this._contextProvider.CreateNewConfigurationContext(), this._configuration);
        Assert.That(this._configuration.DetermineCharacterClasses(CharacterClasses.RuneWizard), Is.EquivalentTo(new[] { runeWizard }));
        Assert.That(this._configuration.DetermineCharacterClasses(CharacterClasses.All).Count(), Is.EqualTo(19));
    }

    /// <summary>
    /// The class number fits the 5 bit class field of the appearance packet and doesn't collide with another class.
    /// </summary>
    [Test]
    public void ClassNumberFitsTheAppearancePacket()
    {
        var serializer = new AppearanceSerializer();
        var appearanceData = new Mock<IAppearanceData>();
        appearanceData.Setup(a => a.CharacterClass).Returns(new CharacterClass { Number = RuneWizardNumber });
        appearanceData.Setup(a => a.EquippedItems).Returns(Enumerable.Empty<ItemAppearance>());
        var data = new byte[serializer.NeededSpace];
        serializer.WriteAppearanceData(data, appearanceData.Object, false);

        Assert.That(data[0] & 0xF8, Is.EqualTo(RuneWizardNumber << 3));
        Assert.That(this._configuration.CharacterClasses.Any(c => c.Number == RuneWizardNumber), Is.False);
    }

    /// <summary>
    /// The preview adds the class in memory, without being able to get created, and lets it use the skills and items of the dark wizard.
    /// </summary>
    [Test]
    public void PreviewAllowsDarkWizardSkillsAndItems()
    {
        var darkWizardSkills = this._configuration.Skills.Where(s => s.QualifiedCharacters.Any(c => c.Number == 0)).ToList();
        var darkWizardItems = this._configuration.Items.Where(i => i.QualifiedCharacters.Any(c => c.Number == 0)).ToList();
        Assert.That(darkWizardSkills, Is.Not.Empty);
        Assert.That(darkWizardItems, Is.Not.Empty);

        var memoryContext = new InMemoryPersistenceContextProvider().CreateNewContext();
        var runeWizard = RuneWizardPreview.Apply(memoryContext, this._configuration);

        Assert.That(runeWizard.CanGetCreated, Is.False);
        Assert.That(darkWizardSkills.All(s => s.QualifiedCharacters.Contains(runeWizard)), Is.True);
        Assert.That(darkWizardItems.All(i => i.QualifiedCharacters.Contains(runeWizard)), Is.True);

        var knightSkill = this._configuration.Skills.First(s => s.QualifiedCharacters.Count > 0 && s.QualifiedCharacters.All(c => c.Number is 4 or 6 or 7));
        Assert.That(knightSkill.QualifiedCharacters.Contains(runeWizard), Is.False, "only what the dark wizard uses");
    }

    /// <summary>
    /// A MOBA clone of the rune wizard gets the wizard family kit (skills and the starter staff), like a bot placed with <c>/mobabot ... rw</c>.
    /// </summary>
    [Test]
    public void MobaCloneGetsTheWizardKit()
    {
        var context = this._contextProvider.CreateNewContext();
        var runeWizard = RuneWizardPreview.Apply(context, this._configuration);

        var clone = context.CreateNew<Character>();
        clone.CharacterClass = runeWizard;
        clone.Inventory = context.CreateNew<ItemStorage>();
        MobaLoadouts.Apply(context, this._configuration, clone, runeWizard);

        Assert.That(clone.LearnedSkills.Count, Is.GreaterThan(4));
        Assert.That(clone.Inventory.Items.Any(i => i.ItemSlot == 1 && i.Definition?.Group == 5), Is.True, "starter staff in the right hand");
        Assert.That(MobaPassives.FamilyOf(runeWizard.Number), Is.EqualTo(MobaFamily.RuneWizard));
        Assert.That(MobaBotChatCommandPlugIn.ResolveClassNumber("rw"), Is.EqualTo(RuneWizardNumber));
    }
}
