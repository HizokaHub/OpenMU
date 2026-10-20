// <copyright file="MobaFormat.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The format of a match: how many players a team has, which lanes are played and whether the jungle is.
/// </summary>
public enum MobaFormat : byte
{
    /// <summary>The classic match: 5 against 5, the three lanes and the jungle.</summary>
    Classic = 0,

    /// <summary>3 against 3, the three lanes (one player each) and the jungle.</summary>
    Trio = 1,

    /// <summary>1 against 1 in the middle lane, without jungle; the side lanes are closed by barriers.</summary>
    Duel = 2,

    /// <summary>5 against 5 in the middle lane only, without jungle; the side lanes are closed by barriers.</summary>
    Aram = 3,
}

/// <summary>
/// The rules of every <see cref="MobaFormat"/>.
/// </summary>
public static class MobaFormats
{
    /// <summary>The steps of the wait of the 3v3 queue: the first 10 minutes only a full match of humans starts; then 4 humans are enough.</summary>
    private static readonly IReadOnlyList<(TimeSpan Wait, int Humans)> TrioStages = [(TimeSpan.FromMinutes(10), 4)];

    /// <summary>The steps of the wait of the classic queue with bots: after the oldest entry waited this long, this many humans are enough.</summary>
    private static readonly IReadOnlyList<(TimeSpan Wait, int Humans)> ClassicStages =
    [
        (TimeSpan.FromSeconds(120), 6),
        (TimeSpan.FromSeconds(180), 4),
        (TimeSpan.FromSeconds(240), 2),
        (TimeSpan.FromSeconds(360), 1),
    ];

    /// <summary>
    /// Gets the players of a team.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <returns>The team size.</returns>
    public static int TeamSize(MobaFormat format) => format switch
    {
        MobaFormat.Trio => 3,
        MobaFormat.Duel => 1,
        _ => 5,
    };

    /// <summary>
    /// Gets the largest party the open pool of a format takes. The classic pool leaves the parties of five to the premade
    /// pool; the others take a party as large as a team (a duel has no parties).
    /// </summary>
    /// <param name="format">The format.</param>
    /// <returns>The size.</returns>
    public static int MaxOpenEntrySize(MobaFormat format) => format == MobaFormat.Classic ? TeamSize(format) - 1 : TeamSize(format);

    /// <summary>
    /// Gets the steps of the wait for a match with bots.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <returns>After how long the oldest entry has waited, how many humans a match with bots needs.</returns>
    public static IReadOnlyList<(TimeSpan Wait, int Humans)> BotStages(MobaFormat format) => format switch
    {
        MobaFormat.Classic or MobaFormat.Aram => ClassicStages,
        MobaFormat.Trio => TrioStages,
        _ => [],
    };

    /// <summary>
    /// Gets whether the jungle (camps, bosses, event) is part of the format.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <returns><see langword="true"/> if the jungle is played.</returns>
    public static bool HasJungle(MobaFormat format) => format is MobaFormat.Classic or MobaFormat.Trio;

    /// <summary>
    /// Gets whether the side lanes are closed (only the middle lane is played).
    /// </summary>
    /// <param name="format">The format.</param>
    /// <returns><see langword="true"/> if only the middle lane is open.</returns>
    public static bool MiddleLaneOnly(MobaFormat format) => format is MobaFormat.Duel or MobaFormat.Aram;

    /// <summary>
    /// Gets whether a format can be searched: the formats which play the middle lane only wait for the barriers which close the
    /// side lanes.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <returns><see langword="true"/> if the format is ready to be played.</returns>
    public static bool IsPlayable(MobaFormat format) => !MiddleLaneOnly(format);

    /// <summary>
    /// Gets the name of a format for the players.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <returns>The name.</returns>
    public static string Label(MobaFormat format) => format switch
    {
        MobaFormat.Trio => "3v3",
        MobaFormat.Duel => "1v1",
        MobaFormat.Aram => "ARAM 5v5",
        _ => "5v5",
    };
}
