// <copyright file="MobaMinimapKind.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The packed KIND byte of a MOBA minimap entry (packet C1 D5 10):
/// <code>
/// bit 7     recalling (champions only: channelling the recall, see <see cref="MobaRecall.IsChanneling"/>)
/// bits 4-6  base class index 0..6 (champions only; see <see cref="BaseClassIndex"/>)
/// bits 0-3  kind (<see cref="Champion"/>, <see cref="Turret"/>, <see cref="Nexus"/>, <see cref="Ward"/>)
/// </code>
/// Turret, nexus and ward entries always keep bits 4-7 at 0. The client decodes it as
/// <c>kind = b &amp; 0x0F, cls = (b &gt;&gt; 4) &amp; 0x07, recalling = (b &amp; 0x80) != 0</c>.
/// </summary>
public static class MobaMinimapKind
{
    /// <summary>A champion.</summary>
    public const byte Champion = 1;

    /// <summary>A turret.</summary>
    public const byte Turret = 2;

    /// <summary>A nexus.</summary>
    public const byte Nexus = 3;

    /// <summary>A ward.</summary>
    public const byte Ward = 4;

    /// <summary>An inhibitor. Bit 7 set = it is down; then the HP byte of the entry holds the seconds left to respawn divided by 2.</summary>
    public const byte Inhibitor = 5;

    /// <summary>Bit 7 of an inhibitor entry: the inhibitor is down.</summary>
    public const byte InhibitorDownBit = 0x80;

    /// <summary>The highest base class index (Rage Fighter).</summary>
    public const byte MaxBaseClassIndex = 6;

    /// <summary>The first class number of the rune wizard family (28, 30 and 31). The client has no portrait for it yet, so it shows the Dark Wizard's.</summary>
    public const byte FirstRuneWizardClassNumber = 28;

    /// <summary>The last class number of the rune wizard family.</summary>
    public const byte LastRuneWizardClassNumber = 31;

    private const byte KindMask = 0x0F;
    private const byte ClassMask = 0x07;
    private const int ClassShift = 4;
    private const byte RecallingBit = 0x80;

    /// <summary>
    /// Maps a character class number to its base class index: 0 Dark Wizard, 1 Dark Knight, 2 Elf,
    /// 3 Magic Gladiator, 4 Dark Lord, 5 Summoner, 6 Rage Fighter (2nd/3rd evolutions included;
    /// same order as <see cref="MobaFamily"/>).
    /// </summary>
    /// <param name="classNumber">The character class number.</param>
    /// <returns>The base class index, clamped to 0..<see cref="MaxBaseClassIndex"/>.</returns>
    public static byte BaseClassIndex(byte classNumber)
        => classNumber is >= FirstRuneWizardClassNumber and <= LastRuneWizardClassNumber ? (byte)0 : (byte)Math.Min(classNumber >> 2, MaxBaseClassIndex);

    /// <summary>Packs the KIND byte of a minimap entry.</summary>
    /// <param name="kind">The entry kind (<see cref="Champion"/>, <see cref="Turret"/>, <see cref="Nexus"/>, <see cref="Ward"/>).</param>
    /// <param name="classNumber">The champion's character class number; ignored for other kinds.</param>
    /// <param name="recalling">Whether the champion is channelling the recall; ignored for other kinds.</param>
    /// <returns>The packed byte.</returns>
    public static byte Pack(byte kind, byte classNumber, bool recalling)
    {
        var packed = (byte)(kind & KindMask);
        if (packed != Champion)
        {
            return packed;
        }

        packed |= (byte)(BaseClassIndex(classNumber) << ClassShift);
        if (recalling)
        {
            packed |= RecallingBit;
        }

        return packed;
    }

    /// <summary>Packs the KIND byte of a champion entry.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The packed byte.</returns>
    public static byte PackChampion(Player champion)
        => Pack(Champion, champion.SelectedCharacter?.CharacterClass?.Number ?? 0, MobaRecall.IsChanneling(champion));

    /// <summary>Gets the kind (bits 0-3) of a packed byte.</summary>
    /// <param name="packed">The packed byte.</param>
    /// <returns>The kind.</returns>
    public static byte KindOf(byte packed) => (byte)(packed & KindMask);

    /// <summary>Gets the base class index (bits 4-6) of a packed byte.</summary>
    /// <param name="packed">The packed byte.</param>
    /// <returns>The base class index.</returns>
    public static byte ClassOf(byte packed) => (byte)((packed >> ClassShift) & ClassMask);

    /// <summary>Gets whether the recalling bit (bit 7) of a packed byte is set.</summary>
    /// <param name="packed">The packed byte.</param>
    /// <returns><see langword="true"/> if the champion is recalling.</returns>
    public static bool IsRecalling(byte packed) => (packed & RecallingBit) != 0;
}
