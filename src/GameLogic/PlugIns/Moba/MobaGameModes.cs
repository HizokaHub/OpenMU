// <copyright file="MobaGameModes.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The modes the server knows. A new mode is one class (see <see cref="MobaGameModeBase"/>) and one line in <see cref="All"/>.
/// </summary>
public static class MobaGameModes
{
    /// <summary>Gets the classic MOBA, the mode every arena starts with.</summary>
    public static IMobaGameMode Classic { get; } = new MobaClassicMode();

    /// <summary>Gets every mode of the server; the first is the default.</summary>
    public static IReadOnlyList<IMobaGameMode> All { get; } = [Classic];

    /// <summary>
    /// Finds a mode by its identifier.
    /// </summary>
    /// <param name="id">The identifier; the case does not matter.</param>
    /// <returns>The mode, or <see langword="null"/> if there is none with that identifier.</returns>
    public static IMobaGameMode? Find(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(m => string.Equals(m.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
}
