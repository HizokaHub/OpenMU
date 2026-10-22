// <copyright file="MobaLoadoutStore.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.IO;
using System.Text.Json;

/// <summary>
/// The loadouts the players picked: for every character, which <see cref="MinSkills"/> to <see cref="MaxSkills"/> skills
/// its champion takes into a match. Kept in a small JSON file next to the server (no database table: it is a preference,
/// not game state), read at start and written at every change.
/// </summary>
public static class MobaLoadoutStore
{
    /// <summary>The fewest skills a loadout has.</summary>
    public const int MinSkills = 4;

    /// <summary>The most skills a loadout has.</summary>
    public const int MaxSkills = 4;

    private static readonly object Sync = new();

    private static Dictionary<string, short[]>? _picks;

    /// <summary>Gets or sets the file the picks are kept in.</summary>
    public static string FilePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "moba-loadouts.json");

    /// <summary>
    /// Gets the loadout a character takes into a match: what it picked, or the class default.
    /// </summary>
    /// <param name="characterName">The real character's name.</param>
    /// <param name="classNumber">The character class number.</param>
    /// <returns>The skill numbers.</returns>
    public static IReadOnlyList<short> SelectedFor(string characterName, byte classNumber)
    {
        lock (Sync)
        {
            Load();
            if (_picks!.TryGetValue(characterName, out var picked) && Validate(picked, classNumber) is null)
            {
                return picked;
            }
        }

        return MobaLoadouts.DefaultFor(classNumber);
    }

    /// <summary>
    /// Gets whether the character ever picked its skills (otherwise it takes the class default).
    /// </summary>
    /// <param name="characterName">The real character's name.</param>
    /// <returns><see langword="true"/> if there is a saved pick.</returns>
    public static bool HasPick(string characterName)
    {
        lock (Sync)
        {
            Load();
            return _picks!.ContainsKey(characterName);
        }
    }

    /// <summary>
    /// Checks a loadout.
    /// </summary>
    /// <param name="skills">The skill numbers.</param>
    /// <param name="classNumber">The character class number.</param>
    /// <returns>The reason it is not valid, or <see langword="null"/> if it is.</returns>
    public static string? Validate(IReadOnlyCollection<short> skills, byte classNumber)
    {
        if (skills.Count is < MinSkills or > MaxSkills)
        {
            return MinSkills == MaxSkills ? $"Elige {MinSkills} skills." : $"Elige entre {MinSkills} y {MaxSkills} skills.";
        }

        if (skills.Distinct().Count() != skills.Count)
        {
            return "Hay skills repetidas.";
        }

        var allowed = MobaLoadouts.SelectableFor(classNumber);
        return skills.All(allowed.Contains) ? null : "Alguna skill no se puede elegir para tu clase.";
    }

    /// <summary>
    /// Saves the loadout of a character.
    /// </summary>
    /// <param name="characterName">The real character's name.</param>
    /// <param name="classNumber">The character class number.</param>
    /// <param name="skills">The skills picked.</param>
    /// <param name="error">The reason it was refused.</param>
    /// <returns><see langword="true"/> if it was saved.</returns>
    public static bool TrySave(string characterName, byte classNumber, IReadOnlyCollection<short> skills, out string? error)
    {
        error = Validate(skills, classNumber);
        if (error is not null)
        {
            return false;
        }

        // Kept in the order of the class's list, which is the order of the skill bar.
        var ordered = MobaLoadouts.SelectableFor(classNumber).Where(skills.Contains).ToArray();
        lock (Sync)
        {
            Load();
            _picks![characterName] = ordered;
            Write();
        }

        return true;
    }

    /// <summary>Forgets what was read, so the next call reads the file again (tests).</summary>
    public static void Reset()
    {
        lock (Sync)
        {
            _picks = null;
        }
    }

    private static void Load()
    {
        if (_picks is not null)
        {
            return;
        }

        _picks = new Dictionary<string, short[]>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(FilePath))
            {
                var read = JsonSerializer.Deserialize<Dictionary<string, short[]>>(File.ReadAllText(FilePath));
                if (read is not null)
                {
                    foreach (var (name, skills) in read)
                    {
                        _picks[name] = skills;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // A broken file only costs the picks: everybody gets the defaults again.
            Console.Error.WriteLine($"[MOBA-LOADOUT] could not read '{FilePath}': {ex.Message}");
        }
    }

    private static void Write()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_picks, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MOBA-LOADOUT] could not write '{FilePath}': {ex.Message}");
        }
    }
}
