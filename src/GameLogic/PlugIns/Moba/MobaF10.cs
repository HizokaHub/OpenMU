// <copyright file="MobaF10.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// Formula F10 ("conservation law of the duel power"), ported from <c>tools/balance_sim/f10_conservacion.py</c> and validated in the balance simulator
/// (90.8/100 against 48.9 of the current game). It is an ALTERNATIVE to the 45/20/35 blend, behind the <see cref="Enabled"/> switch (<c>/mobaf10</c>),
/// so both can be duelled on the same server (<c>/mobaduel</c>).
/// <para>
/// IDEA: in a duel A kills B first when <c>DPS_A × EHP_A &gt; DPS_B × EHP_B</c>. With the same investment every champion has the same duel power
/// <c>P = DPS × EHP</c>, however it splits between damage and defense; tank and glass cannon differ by an inclination, not by an advantage:
/// <c>log DPS = log DPS(level) + kO·O + ½·lean + ½·psi</c> and <c>log EHP = log EHP(level) + kD·D − ½·lean + ½·psi</c>, with
/// <c>O</c> the offensive pool (primary stat, offense nodes, weapon and attack pieces, spell power, damage buffs) and <c>D</c> the defensive pool
/// (VIT, defense / durability / health nodes, armour pieces, life and durability traits, life buffs). Every element is a saturating curve
/// (<c>x(1+k)/(x+k)</c>) with its own weight, so there are no hard caps and every element and buff counts at once.
/// </para>
/// <para>
/// This is the FIRST translation: it keeps the game's own crit roll, hit cap and burst brake, ignores pets (the bots buy none) and uses the aggregated
/// inputs the game already has (stat points, tree fractions, item fractions, archetype traits, buff ranks) instead of the per-slot roles of the simulator.
/// The weights are the simulator's; the class curves (<see cref="Psi"/>) and the global scales start neutral and are calibrated with real duels.
/// </para>
/// </summary>
public static class MobaF10
{
    private const double KO = 0.5;                 // offense pool -> log damage
    private const double KD = 0.5;                 // defense pool -> log life / mitigation
    private const double SatStat = 1.2;
    private const double SatTree = 1.5;
    private const double BaseMitigation = 0.08;    // m0
    private const double MagicMitigationShare = 0.72; // mu (the mitigation is blended with it until the hit carries its damage type)
    private const double MitigationShare = 0.5;    // phi_mit: part of the durability pool that becomes percent mitigation (the rest cuts damage taken)

    /// <summary>Items: full T3 weapon / off-hand value and full T3 armour value in pool units (sim: 0.5 weight x slot cost weights).</summary>
    private const double WeaponPool = 1.2;

    private const double ArmourPool = 3.3;

    /// <summary>
    /// Class offsets calibrated with real duels (2026-11-19, level 30, no items, damage scale 2.7, 8 duels per pair, all classes 39-56 % against the field).
    /// Large because the kits and the bot rotations are not neutral: SUM and FE need a big negative offset.
    /// </summary>
    private static readonly double[] PsiByFamily = BuildPsi();

    /// <summary>Gets or sets a value indicating whether the F10 model replaces the 45/20/35 blend (default: environment variable <c>MOBA_F10=1</c>).</summary>
    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("MOBA_F10") == "1";

    /// <summary>Gets or sets the global damage scale of the model (calibrated with real duels so that a typical fight lasts as designed).</summary>
    public static double DamageScale { get; set; } = 2.7;

    /// <summary>Gets or sets the global life scale of the model.</summary>
    public static double HealthScale { get; set; } = 1.0;

    private static double[] BuildPsi()
    {
        var psi = new double[Enum.GetValues<MobaFamily>().Length];
        psi[(int)MobaFamily.Wizard] = -0.443;
        psi[(int)MobaFamily.Knight] = 1.066;
        psi[(int)MobaFamily.Elf] = -2.388;
        psi[(int)MobaFamily.MagicGladiator] = 1.383;
        psi[(int)MobaFamily.DarkLord] = 2.751;
        psi[(int)MobaFamily.Summoner] = -3.498;
        psi[(int)MobaFamily.RageFighter] = 1.129;
        return psi;
    }

    /// <summary>Gets the class power offset (log of the duel power), set by <c>/mobaf10 psi</c> from the duel calibration.</summary>
    /// <param name="family">The champion family.</param>
    /// <returns>The offset (0 = neutral).</returns>
    public static double Psi(MobaFamily family) => PsiByFamily[(int)family];

    /// <summary>Sets the class power offset.</summary>
    /// <param name="family">The champion family.</param>
    /// <param name="value">The offset.</param>
    public static void SetPsi(MobaFamily family, double value) => PsiByFamily[(int)family] = value;

    /// <summary>The pools of a champion.</summary>
    /// <param name="Offense">The offensive pool <c>O</c>.</param>
    /// <param name="Life">The life part of the defensive pool.</param>
    /// <param name="Durability">The durability part of the defensive pool (percent mitigation and damage taken).</param>
    public readonly record struct Pools(double Offense, double Life, double Durability);

    /// <summary>Saturating curve <c>x(1+k)/(x+k)</c>: 0 at 0, 1 at 1, no hard cap.</summary>
    /// <param name="x">The input, 0..1.</param>
    /// <param name="k">The knee (small = saturates early).</param>
    /// <returns>The value.</returns>
    public static double Sat(double x, double k) => x <= 0 ? 0 : x * (1.0 + k) / (x + k);

    /// <summary>Gets the offensive and defensive pools of a champion from what it really has invested.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The pools.</returns>
    public static Pools PoolsOf(Player champion)
    {
        var a = champion.Attributes;
        var family = MobaPassives.FamilyOf(champion);
        var prim = 0.0;
        var vit = 0.0;
        if (a is not null)
        {
            var primStat = family switch
            {
                MobaFamily.Knight or MobaFamily.RageFighter => Stats.TotalStrength,
                MobaFamily.Elf => Stats.TotalAgility,
                MobaFamily.DarkLord => Stats.TotalLeadership,
                _ => Stats.TotalEnergy,
            };
            prim = Math.Clamp((a[primStat] - MobaCloneFactory.BaselineStatValue) / MobaStatEconomy.MaxPerStat, 0.0, 1.0);
            vit = Math.Clamp((a[Stats.TotalVitality] - MobaCloneFactory.BaselineStatValue) / MobaStatEconomy.MaxPerStat, 0.0, 1.0);
        }

        var treeOff = MobaMasterTree.Fraction(champion, MobaTreeKind.Offense) ?? 0.0;
        var treeDef = MobaMasterTree.Fraction(champion, MobaTreeKind.Defense) ?? 0.0;
        var treeHealth = MobaMasterTree.Fraction(champion, MobaTreeKind.Health) ?? 0.0;
        var treeDur = MobaMasterTree.Fraction(champion, MobaTreeKind.Durability) ?? 0.0;
        var itemOff = MobaItemPower.OffenseFractionOf(champion);
        var itemDef = MobaItemPower.DefenseFractionOf(champion);
        var spellPower = MobaArchetype.TotalOf(champion, MobaArchetypeTrait.SpellPower);
        var healthPool = MobaArchetype.TotalOf(champion, MobaArchetypeTrait.HealthPool);
        var durItems = MobaArchetype.TotalOf(champion, MobaArchetypeTrait.Durability);

        // buffs received (rank 0..5 of the caster's skill): all of them count at once, stacked with diminishing returns
        double Buff(short skill) => MobaBuffPower.RankOn(champion, skill) / 5.0;
        const double Kb = 0.12;
        var buffOff = Kb * (1.0 - Math.Exp(-((0.10 * Buff(MobaBuffPower.ExpansionOfWizardry)) + (0.05 * Buff(218))) / Kb));
        var buffLife = Kb * (1.0 - Math.Exp(-((0.10 * Buff(48)) + (0.10 * Buff(267)) + (0.05 * Buff(218)) + (0.10 * Buff(26)) + (0.10 * Buff(234))) / Kb));
        var buffDur = Kb * (1.0 - Math.Exp(-(0.06 * Buff(MobaBuffPower.IncreaseBlock)) / Kb));

        var offense = (0.5 * Sat(prim, SatStat)) + (0.5 * Sat(treeOff, SatTree)) + (WeaponPool * itemOff) + (0.25 * ArmourPool * itemDef) + (3.0 * spellPower) + buffOff;
        var life = (0.25 * Sat(vit, SatStat)) + (0.5 * Sat(treeHealth, SatTree)) + (0.25 * ArmourPool * itemDef) + (2.0 * healthPool) + (0.3 * itemOff) + buffLife;
        var durability = (0.25 * Sat(vit, SatStat)) + (0.5 * Sat(treeDef, SatTree)) + (0.5 * Sat(treeDur, SatTree)) + (0.5 * ArmourPool * itemDef) + (3.0 * durItems) + buffDur;
        return new Pools(offense, life, durability);
    }

    /// <summary>The pools of the reference build of a level: the default clone of the bots (35 % primary / 35 % VIT, no items before level 3, tier by level, 30 % of each tree).</summary>
    /// <param name="level">The champion level.</param>
    /// <param name="family">The family.</param>
    /// <returns>The reference pools.</returns>
    private static Pools ReferencePools(int level, MobaFamily family)
    {
        var xl = (Math.Clamp(level, 1, 30) - 1) / 29.0;
        var total = family switch
        {
            MobaFamily.RageFighter or MobaFamily.Summoner => 112_000.0,
            MobaFamily.DarkLord => 130_000.0,
            _ => 100_000.0,
        };
        var prim = Math.Clamp(0.35 * total * xl / MobaStatEconomy.MaxPerStat, 0.0, 1.0);
        var tier = level < 3 ? 0.0 : level < 10 ? 1.0 / 3 : level < 20 ? 2.0 / 3 : 1.0;
        var tree = 0.3 * xl;
        var offense = (0.5 * Sat(prim, SatStat)) + (0.5 * Sat(tree, SatTree)) + (WeaponPool * tier) + (0.25 * ArmourPool * tier);
        var life = (0.25 * Sat(prim, SatStat)) + (0.5 * Sat(tree, SatTree)) + (0.25 * ArmourPool * tier) + (0.3 * tier);
        var durability = (0.25 * Sat(prim, SatStat)) + (0.5 * Sat(tree, SatTree)) + (0.5 * Sat(tree, SatTree)) + (0.5 * ArmourPool * tier);
        return new Pools(offense, life, durability);
    }

    /// <summary>The class lean: half the log of damage tilt over life tilt of the game's role tilt (tank leans to life, caster to damage).</summary>
    /// <param name="family">The family.</param>
    /// <returns>The lean.</returns>
    public static double Lean(MobaFamily family)
    {
        var (hp, dmg) = MobaProgression.RoleTilt(family);
        return 0.5 * Math.Log(dmg / hp);
    }

    /// <summary>Gets the factor that multiplies the flat damage of a hit (1 = the reference build of the champion's level).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The damage factor.</returns>
    public static double DamageFactor(Player champion)
    {
        var family = MobaPassives.FamilyOf(champion);
        var pools = PoolsOf(champion);
        var reference = ReferencePools(champion.MobaLevel, family);
        return DamageScale * Math.Exp((KO * (pools.Offense - reference.Offense)) + (0.5 * Lean(family)) + (0.5 * Psi(family)));
    }

    /// <summary>Gets the maximum life the model gives a champion.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The maximum life.</returns>
    public static float HealthOf(Player champion)
    {
        var family = MobaPassives.FamilyOf(champion);
        var pools = PoolsOf(champion);
        var reference = ReferencePools(champion.MobaLevel, family);
        var factor = HealthScale * Math.Exp((KD * (pools.Life - reference.Life)) - (0.5 * Lean(family)) + (0.5 * Psi(family)));
        return (float)(MobaProgression.HealthAt(champion.MobaLevel) * factor);
    }

    /// <summary>Gets the percent mitigation and the damage-taken factor of a champion from its durability pool.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The mitigation fraction (0..1) and the factor on the damage taken (1 = none).</returns>
    public static (double Mitigation, double DamageTaken) DefenseOf(Player champion)
    {
        var pools = PoolsOf(champion);
        var x = KD * pools.Durability;
        var xm = MitigationShare * x * ((1.0 + MagicMitigationShare) / 2.0);
        var xt = (1.0 - MitigationShare) * x;
        return (1.0 - ((1.0 - BaseMitigation) * Math.Exp(-xm)), Math.Exp(-xt));
    }

    /// <summary>A one-line description of a champion under the model, for <c>/mobaf10 status</c>.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The text.</returns>
    public static string Describe(Player champion)
    {
        var pools = PoolsOf(champion);
        var (mit, dtm) = DefenseOf(champion);
        return FormattableString.Invariant($"O={pools.Offense:F2} vida={pools.Life:F2} dur={pools.Durability:F2} dano x{DamageFactor(champion):F2} vida {HealthOf(champion):F0} mit {mit:P0} recibido x{dtm:F2}");
    }
}
