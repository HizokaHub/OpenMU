// <copyright file="MobaProgression.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// The MOBA power curve from champion level 1 to <see cref="MobaLevels.MaxLevel"/>.
/// <para>
/// Everything scales together off the champion level so an even fight takes a similar
/// number of hits at every level, while the extras - skill ranks, the stat build (see
/// <see cref="MobaSkillDamage"/>) and items later - are what tilt a fight. This is the
/// single place to retune the whole game's numbers: change the six endpoint pairs below.
/// </para>
/// <list type="bullet">
/// <item>HP: <c>1,200 -&gt; 45,000</c> (a squishy at 30; VIT / items push tanks higher)</item>
/// <item>Mana: <c>900 -&gt; 14,000</c></item>
/// <item>Shield (SD): <c>300 -&gt; 9,000</c></item>
/// <item>Defense (flat): <c>15 -&gt; 400</c></item>
/// <item>Damage scale: <c>1.0x -&gt; 22x</c> multiplied onto every skill / basic hit</item>
/// </list>
/// </summary>
public static class MobaProgression
{
    private const float HealthLevel1 = 1_200f;
    private const float HealthLevelMax = 45_000f;

    private const float ManaLevel1 = 450f;
    private const float ManaLevelMax = 3_400f;

    private const float ShieldLevel1 = 300f;
    private const float ShieldLevelMax = 9_000f;

    private const float DefenseLevel1 = 15f;
    private const float DefenseLevelMax = 400f;

    private const double DamageScaleLevel1 = 1.0;
    private const double DamageScaleLevelMax = 22.0;

    /// <summary>Max health for a champion at the given level.</summary>
    /// <param name="level">Champion level (1..30).</param>
    /// <returns>The scaled max health.</returns>
    public static float HealthAt(int level) => Lerp(HealthLevel1, HealthLevelMax, level);

    /// <summary>Max mana for a champion at the given level.</summary>
    /// <param name="level">Champion level (1..30).</param>
    /// <returns>The scaled max mana.</returns>
    public static float ManaAt(int level) => Lerp(ManaLevel1, ManaLevelMax, level);

    /// <summary>Max shield (SD) for a champion at the given level.</summary>
    /// <param name="level">Champion level (1..30).</param>
    /// <returns>The scaled max shield.</returns>
    public static float ShieldAt(int level) => Lerp(ShieldLevel1, ShieldLevelMax, level);

    /// <summary>Flat defense for a champion at the given level.</summary>
    /// <param name="level">Champion level (1..30).</param>
    /// <returns>The scaled flat defense.</returns>
    public static float DefenseAt(int level) => Lerp(DefenseLevel1, DefenseLevelMax, level);

    /// <summary>Global damage multiplier applied to every MOBA skill / basic hit at the given level.</summary>
    /// <param name="level">Champion level (1..30).</param>
    /// <returns>The damage scale (1.0 at level 1).</returns>
    public static double DamageScale(int level) => Lerp((float)DamageScaleLevel1, (float)DamageScaleLevelMax, level);

    /// <summary>
    /// Role tilt on the shared curve: tanks trade damage for HP, carries the reverse,
    /// bruisers stay neutral. (HpMul, DamageMul) per family.
    /// </summary>
    /// <param name="family">The champion family.</param>
    /// <returns>The (health multiplier, damage-scale multiplier).</returns>
    public static (float HpMul, double DamageMul) RoleTilt(MobaFamily family) => family switch
    {
        MobaFamily.Knight or MobaFamily.RageFighter => (1.30f, 0.82),          // bruiser-tank front line
        MobaFamily.DarkLord => (1.15f, 0.92),                                  // tanky utility
        MobaFamily.Elf => (0.80f, 1.20),                                       // ranged carry
        MobaFamily.Wizard or MobaFamily.Summoner => (0.82f, 1.22),            // burst / DoT casters
        _ => (1.00f, 1.00),                                                    // Magic Gladiator - neutral
    };

    /// <summary>Damage scale for a champion at its level, tilted for its class role.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The role-adjusted damage scale.</returns>
    public static double DamageScaleFor(Player champion)
        => DamageScale(champion.MobaLevel) * RoleTilt(MobaPassives.FamilyOf(champion)).DamageMul;

    /// <summary>
    /// Re-pins a champion clone's resources and flat defense to its current champion level
    /// and tops health / mana / shield up to the new maximum. Call on spawn and on every
    /// level-up.
    /// </summary>
    /// <param name="champion">The champion clone.</param>
    public static void ApplyLevelScaling(Player champion)
    {
        if (champion.Attributes is not { } attributes)
        {
            return;
        }

        var level = Math.Clamp(champion.MobaLevel <= 0 ? 1 : champion.MobaLevel, 1, MobaLevels.MaxLevel);
        var (hpMul, _) = RoleTilt(MobaPassives.FamilyOf(champion));

        SetAbsolute(attributes, Stats.MaximumHealth, HealthAt(level) * hpMul);
        SetAbsolute(attributes, Stats.MaximumMana, ManaAt(level));
        SetAbsolute(attributes, Stats.MaximumShield, ShieldAt(level));

        var defense = DefenseAt(level);
        SetAbsolute(attributes, Stats.DefenseBase, defense);
        SetAbsolute(attributes, Stats.DefensePvp, defense);
        SetAbsolute(attributes, Stats.DefensePvm, defense);

        attributes[Stats.CurrentHealth] = attributes[Stats.MaximumHealth];
        attributes[Stats.CurrentMana] = attributes[Stats.MaximumMana];
        attributes[Stats.CurrentShield] = attributes[Stats.MaximumShield];
    }

    private static float Lerp(float atLevel1, float atLevelMax, int level)
    {
        var t = Math.Clamp((level - 1) / (float)(MobaLevels.MaxLevel - 1), 0f, 1f);
        return atLevel1 + ((atLevelMax - atLevel1) * t);
    }

    private static void SetAbsolute(IAttributeSystem attributes, AttributeDefinition stat, float value)
    {
        attributes.AddElement(new SimpleElement(value - attributes[stat], AggregateType.AddRaw), stat);
    }
}
