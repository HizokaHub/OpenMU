// <copyright file="MobaShopVariant.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Which mix of options a shop item carries. The item itself and the number of options are
/// fixed by its tier; the variant only decides <i>which</i> options fill those slots, so the
/// same weapon / set / wing can be bought as different builds.
/// </summary>
public enum MobaShopVariant
{
    /// <summary>The default mix: raw damage / max health and mitigation first.</summary>
    Standard,

    /// <summary>
    /// Offense: crit chance and attack speed on weapons, reflection and defense rate on armor,
    /// defense ignore on wings.
    /// </summary>
    Aggressive,

    /// <summary>
    /// Sustain: life / mana on kill on weapons, health and mana pool on armor, health recovery
    /// on wings.
    /// </summary>
    Sustain,

    /// <summary>Utility: reflection / mana wing option (wings only).</summary>
    Utility,
}
