// <copyright file="MakeCharChatCommandArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

/// <summary>
/// Arguments for <c>/makechar &lt;class&gt; &lt;name&gt;</c>: a class alias (or raw class
/// number) and the new character's name.
/// </summary>
public class MakeCharChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets the class alias ("rf", "sum", ...) or the raw class number.</summary>
    [Argument("class")]
    public string? Class { get; set; }

    /// <summary>Gets or sets the new character's name.</summary>
    [Argument("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Resolves <see cref="Class"/> to a character class number, or <c>null</c> if unknown.
    /// </summary>
    /// <returns>The class number, or <c>null</c>.</returns>
    public byte? ResolveClassNumber()
    {
        var value = this.Class?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value switch
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
            "duelmaster" or "dm2" => 13,
            "dl" or "darklord" or "lord" => 16,
            "le" or "lordemperor" => 17,
            "sum" or "summoner" => 20,
            "bs" or "bloodysummoner" => 22,
            "dim" or "dimensionmaster" => 23,
            "rf" or "ragefighter" or "rage" => 24,
            "fm" or "fistmaster" => 25,
            _ => byte.TryParse(value, out var number) ? number : null,
        };
    }
}
