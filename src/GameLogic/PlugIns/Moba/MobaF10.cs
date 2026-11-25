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

    /// <summary>The levels where the per-level tables (class offsets, matchups, scales) hold a calibrated value; between two of them the value is interpolated (2026-11-25).</summary>
    public static readonly int[] LevelKnots = { 1, 5, 10, 15, 20, 25, 30 };

    /// <summary>
    /// Class offsets calibrated with real duels (2026-11-20, level 30, no items, damage scale 1.5, 8 duels per pair, all classes 38-59 % against the field, duels of 10 s),
    /// AFTER the channels that F10 did not see were brought into the model (crowd-control rule, buff ledger, cap and brake against the reference life, reflection,
    /// passives, master tree): they went from +-3.5 to +-0.6. What is left is mostly positioning (range, kiting).
    /// </summary>
    private static readonly double[][] PsiKnots = BuildKnots(BuildPsi());

    /// <summary>
    /// The class offset that comes WITH the items (2026-11-23): the traits of the pieces (spell power, life, durability, mana, regeneration) are not in the reference build,
    /// so a fully equipped champion carries a class-specific excess that the offset of the unequipped build (<see cref="Psi"/>) cannot see. It is scaled by the fraction
    /// of items the champion wears (<see cref="ItemFraction"/>) and calibrated on the T3 pass.
    /// </summary>
    private static readonly double[][] PsiTierKnots = BuildKnots(new double[Enum.GetValues<MobaFamily>().Length]);

    // Multipliers of the global damage / life scales at each knot (1 = the global scale): they bring the duels of every level to the designed length.
    private static readonly double[] DamageKnots = Enumerable.Repeat(1.0, LevelKnots.Length).ToArray();
    private static readonly double[] HealthKnots = Enumerable.Repeat(1.0, LevelKnots.Length).ToArray();

    private static double[][] BuildKnots(double[] atMax) => LevelKnots.Select(_ => (double[])atMax.Clone()).ToArray();

    private static int KnotOf(int level) => Array.IndexOf(LevelKnots, level) is var i && i >= 0 ? i : Array.FindIndex(LevelKnots, k => k >= level) is var j && j >= 0 ? j : LevelKnots.Length - 1;

    private static double Interp(Func<int, double> atKnot, int level)
    {
        level = Math.Clamp(level, LevelKnots[0], LevelKnots[^1]);
        for (var i = 0; i < LevelKnots.Length - 1; i++)
        {
            if (level <= LevelKnots[i + 1])
            {
                var t = (level - LevelKnots[i]) / (double)(LevelKnots[i + 1] - LevelKnots[i]);
                return atKnot(i) + ((atKnot(i + 1) - atKnot(i)) * t);
            }
        }

        return atKnot(LevelKnots.Length - 1);
    }

    private static IEnumerable<int> KnotsFor(int level) => level <= 0 ? Enumerable.Range(0, LevelKnots.Length) : new[] { KnotOf(level) };

    /// <summary>The damage scale of the model at a level: the global scale times the multiplier of the level.</summary>
    /// <param name="level">The champion level.</param>
    /// <returns>The scale.</returns>
    public static double DamageScaleAt(int level) => DamageScale * Interp(i => DamageKnots[i], level);

    /// <summary>The life scale of the model at a level: the global scale times the multiplier of the level.</summary>
    /// <param name="level">The champion level.</param>
    /// <returns>The scale.</returns>
    public static double HealthScaleAt(int level) => HealthScale * Interp(i => HealthKnots[i], level);

    /// <summary>Sets the damage multiplier of a level knot (0 = every knot).</summary>
    /// <param name="level">The knot level.</param>
    /// <param name="value">The multiplier.</param>
    public static void SetDamageKnot(int level, double value)
    {
        foreach (var k in KnotsFor(level))
        {
            DamageKnots[k] = value;
        }
    }

    /// <summary>Sets the life multiplier of a level knot (0 = every knot).</summary>
    /// <param name="level">The knot level.</param>
    /// <param name="value">The multiplier.</param>
    public static void SetHealthKnot(int level, double value)
    {
        foreach (var k in KnotsFor(level))
        {
            HealthKnots[k] = value;
        }
    }

    static MobaF10()
    {
        MobaF10Calibration.Apply();   // the values calibrated by level (generated file)
    }

    /// <summary>Gets or sets a value indicating whether the F10 model replaces the 45/20/35 blend (default: environment variable <c>MOBA_F10=1</c>).</summary>
    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("MOBA_F10") == "1";

    /// <summary>Gets or sets the global damage scale of the model (calibrated with real duels so that a typical fight lasts as designed).</summary>
    public static double DamageScale { get; set; } = 1.5;

    /// <summary>
    /// Gets or sets a value indicating whether the per-hit cap (30 % of the victim's max HP) and the 2 s burst brake of <see cref="MobaDefense"/> stay on under F10.
    /// Diagnosis 2026-11-20: they cut 92-96 % of the damage that reaches Summoner / Elf (low HP) but only ~50 % for the others, so the class offsets (psi) mostly compensate them.
    /// </summary>
    public static bool HitCapAndBrake { get; set; } = true;

    /// <summary>The most of the damage received a reflection can send back under F10 (the Reflection buff of the game gives 30-60 %).</summary>
    public const float MaxReflect = 0.25f;

    /// <summary>
    /// The critical chance of the live roll under F10 (2026-11-20): the same for every class. What a champion has above or below it (AGI, tree, pieces, traits)
    /// enters the damage factor as the ratio of expected damage (<see cref="CritRatio"/>), so the roll no longer decides the balance by luck of the class
    /// (the Wizard crit 29 % of its hits and the Elf 16 %).
    /// </summary>
    public const double CritReference = 0.20;

    /// <summary>Gets or sets the global life scale of the model (1.35 since 2026-11-25: it makes the duels last 10-15 s so a team fight does not end in one second).</summary>
    public static double HealthScale { get; set; } = 1.35;

    // Calibrated 2026-11-25 (evening) with the static duels (4 hand-picked skills rotated by the bots, all buffs, no items, level 30, 60 at a time, life scale 1.35) with tools/calibrar_f10.py.
    // The matchup table (2026-11-25): a log factor on the damage of a family against ANOTHER one, [attacker, defender]. The class offsets (psi) move a family against EVERY rival, so
    // fixing one matchup moved all the others; this table moves one matchup only (the extreme counters are brought to 30-70 % without touching the classes that are already right).
    private static readonly double[][,] PairKnots = LevelKnots.Select(_ => BuildPairPsi()).ToArray();

    /// <summary>Gets the log factor on the damage of a family against another one (0 = none).</summary>
    /// <param name="attacker">The attacking family.</param>
    /// <param name="defender">The defending family.</param>
    /// <returns>The offset.</returns>
    public static double PairPsi(MobaFamily attacker, MobaFamily defender) => PairKnots[^1][(int)attacker, (int)defender];

    /// <summary>Gets the log factor on the damage of a family against another one at a level (interpolated between the knots).</summary>
    /// <param name="attacker">The attacking family.</param>
    /// <param name="defender">The defending family.</param>
    /// <param name="level">The champion level.</param>
    /// <returns>The offset.</returns>
    public static double PairPsiAt(MobaFamily attacker, MobaFamily defender, int level) => Interp(i => PairKnots[i][(int)attacker, (int)defender], level);

    /// <summary>Sets the log factor on the damage of a family against another one.</summary>
    /// <param name="attacker">The attacking family.</param>
    /// <param name="defender">The defending family.</param>
    /// <param name="value">The offset.</param>
    /// <param name="level">The knot level (0 = every knot).</param>
    public static void SetPairPsi(MobaFamily attacker, MobaFamily defender, double value, int level = 0)
    {
        foreach (var k in KnotsFor(level))
        {
            PairKnots[k][(int)attacker, (int)defender] = value;
        }
    }

    /// <summary>Clears the whole matchup table.</summary>
    /// <param name="level">The knot level (0 = every knot).</param>
    public static void ClearPairPsi(int level = 0)
    {
        foreach (var k in KnotsFor(level))
        {
            Array.Clear(PairKnots[k]);
        }
    }

    /// <summary>The factor the matchup table puts on the damage of the attacker against the defender (1 when F10 is off).</summary>
    /// <param name="attacker">The attacking champion.</param>
    /// <param name="defender">The defending champion.</param>
    /// <returns>The factor.</returns>
    public static double PairFactorOf(Player attacker, Player defender)
        => Enabled ? Math.Exp(PairPsiAt(MobaPassives.FamilyOf(attacker), MobaPassives.FamilyOf(defender), attacker.MobaLevel)) : 1.0;

    // Calibrated 2026-11-25 with tools/calibrar_pares_f10.py: the matchups above 80 % (or below 20 %) were brought to 70 % (30 %).
    private static double[,] BuildPairPsi()
    {
        var table = new double[Enum.GetValues<MobaFamily>().Length, Enum.GetValues<MobaFamily>().Length];
        table[(int)MobaFamily.Wizard, (int)MobaFamily.MagicGladiator] = -0.021;
        table[(int)MobaFamily.Knight, (int)MobaFamily.Elf] = 0.040;
        table[(int)MobaFamily.Knight, (int)MobaFamily.RageFighter] = 0.050;
        table[(int)MobaFamily.Elf, (int)MobaFamily.Knight] = -0.040;
        table[(int)MobaFamily.Elf, (int)MobaFamily.RageFighter] = 0.062;
        table[(int)MobaFamily.MagicGladiator, (int)MobaFamily.Wizard] = 0.021;
        table[(int)MobaFamily.MagicGladiator, (int)MobaFamily.RageFighter] = 0.090;
        table[(int)MobaFamily.DarkLord, (int)MobaFamily.RageFighter] = 0.063;
        table[(int)MobaFamily.RageFighter, (int)MobaFamily.Knight] = -0.050;
        table[(int)MobaFamily.RageFighter, (int)MobaFamily.Elf] = -0.062;
        table[(int)MobaFamily.RageFighter, (int)MobaFamily.MagicGladiator] = -0.090;
        table[(int)MobaFamily.RageFighter, (int)MobaFamily.DarkLord] = -0.063;
        return table;
    }

    private static double[] BuildPsi()
    {
        var psi = new double[Enum.GetValues<MobaFamily>().Length];
        psi[(int)MobaFamily.Wizard] = 0.575;
        psi[(int)MobaFamily.Knight] = -0.006;
        psi[(int)MobaFamily.Elf] = -0.173;
        psi[(int)MobaFamily.MagicGladiator] = 0.353;
        psi[(int)MobaFamily.DarkLord] = 0.354;
        psi[(int)MobaFamily.Summoner] = -0.386;
        psi[(int)MobaFamily.RageFighter] = -0.718;
        return psi;
    }

    /// <summary>Gets the class power offset (log of the duel power), set by <c>/mobaf10 psi</c> from the duel calibration.</summary>
    /// <param name="family">The champion family.</param>
    /// <returns>The offset (0 = neutral).</returns>
    public static double Psi(MobaFamily family) => PsiKnots[^1][(int)family];

    /// <summary>Gets the class power offset at a level (interpolated between the knots).</summary>
    /// <param name="family">The champion family.</param>
    /// <param name="level">The champion level.</param>
    /// <returns>The offset.</returns>
    public static double PsiAt(MobaFamily family, int level) => Interp(i => PsiKnots[i][(int)family], level);

    /// <summary>Gets the class offset that scales with the items worn (see <see cref="PsiTierByFamily"/>).</summary>
    /// <param name="family">The champion family.</param>
    /// <returns>The offset at full items.</returns>
    public static double PsiTier(MobaFamily family) => PsiTierKnots[^1][(int)family];

    /// <summary>Gets the class offset that scales with the items worn, at a level (interpolated between the knots).</summary>
    /// <param name="family">The champion family.</param>
    /// <param name="level">The champion level.</param>
    /// <returns>The offset at full items.</returns>
    public static double PsiTierAt(MobaFamily family, int level) => Interp(i => PsiTierKnots[i][(int)family], level);

    /// <summary>Sets the class offset that scales with the items worn.</summary>
    /// <param name="family">The champion family.</param>
    /// <param name="value">The offset at full items.</param>
    /// <param name="level">The knot level (0 = every knot).</param>
    public static void SetPsiTier(MobaFamily family, double value, int level = 0)
    {
        foreach (var k in KnotsFor(level))
        {
            PsiTierKnots[k][(int)family] = value;
        }
    }

    /// <summary>The fraction of items a champion wears (0 = none, 1 = full T3): mean of the offense and defense item fractions.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>0..1.</returns>
    public static double ItemFraction(Player champion) => Math.Clamp(0.5 * (MobaItemPower.OffenseFractionOf(champion) + MobaItemPower.DefenseFractionOf(champion)), 0.0, 1.0);

    /// <summary>The class offset of a champion: the one of the unequipped build plus the one of the items it wears.</summary>
    /// <param name="champion">The champion.</param>
    /// <param name="family">Its family.</param>
    /// <returns>The offset.</returns>
    public static double PsiOf(Player champion, MobaFamily family) => PsiAt(family, champion.MobaLevel) + (PsiTierAt(family, champion.MobaLevel) * ItemFraction(champion));

    /// <summary>Sets the class power offset.</summary>
    /// <param name="family">The champion family.</param>
    /// <param name="value">The offset.</param>
    /// <param name="level">The knot level (0 = every knot).</param>
    public static void SetPsi(MobaFamily family, double value, int level = 0)
    {
        foreach (var k in KnotsFor(level))
        {
            PsiKnots[k][(int)family] = value;
        }
    }

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

        // A class without a master tree gets the reference tree (the same decision as giving every class the four branches): no class starts
        // below the reference only because the tree does not exist for it.
        var refTree = 0.3 * ((Math.Clamp(champion.MobaLevel, 1, 30) - 1) / 29.0);
        var treeOff = MobaMasterTree.Fraction(champion, MobaTreeKind.Offense) ?? refTree;
        var treeDef = MobaMasterTree.Fraction(champion, MobaTreeKind.Defense) ?? refTree;
        var treeHealth = MobaMasterTree.Fraction(champion, MobaTreeKind.Health) ?? refTree;
        var treeDur = MobaMasterTree.Fraction(champion, MobaTreeKind.Durability) ?? refTree;
        var itemOff = MobaItemPower.OffenseFractionOf(champion);
        var itemDef = MobaItemPower.DefenseFractionOf(champion);
        var spellPower = MobaArchetype.TotalOf(champion, MobaArchetypeTrait.SpellPower);
        var healthPool = MobaArchetype.TotalOf(champion, MobaArchetypeTrait.HealthPool);
        var durItems = MobaArchetype.TotalOf(champion, MobaArchetypeTrait.Durability);

        // buffs received (rank 0..5 of the caster's skill): all of them count at once, stacked with diminishing returns
        double Buff(short skill) => MobaBuffPower.RankOn(champion, skill) / 5.0;
        // THE BUFF LEDGER (2026-11-20): every buff of the game counts in some % of the hit damage (offense), of the life, or against the hit (durability).
        // Buffs whose real effect stays alive under F10 are NOT here (they would count twice): Defense 18 and Second Wind (DamageReceiveDecrement),
        // Damage Reflection 217 (reflect, see MobaDefense.ReflectedDamage), Increase Block 268 (crowd-control resistance, see MobaCc.ShapeHardCc).
        // Their real effect on the hit is switched off under F10 and the pool carries it: Soul Barrier 16, Ignore Defense 266, the crit bonus of 64.
        var buffOff = BuffPool(0.10 * Buff(MobaBuffPower.ExpansionOfWizardry), 0.05 * Buff(218), 0.10 * Buff(28), 0.08 * Buff(77), 0.06 * Buff(MobaBuffPower.IncreaseCriticalDamage), 0.05 * Buff(266));
        var buffLife = BuffPool(0.10 * Buff(48), 0.10 * Buff(267), 0.05 * Buff(218), 0.10 * Buff(26), 0.10 * Buff(234));
        var buffDur = BuffPool(0.10 * Buff(16), 0.06 * Buff(27));

        var offense = (0.5 * Sat(prim, SatStat)) + (0.5 * Sat(treeOff, SatTree)) + (WeaponPool * itemOff) + (0.25 * ArmourPool * itemDef) + (3.0 * spellPower) + buffOff;
        var life = (0.25 * Sat(vit, SatStat)) + (0.5 * Sat(treeHealth, SatTree)) + (0.25 * ArmourPool * itemDef) + (2.0 * healthPool) + (0.3 * itemOff) + buffLife;
        var durability = (0.25 * Sat(vit, SatStat)) + (0.5 * Sat(treeDef, SatTree)) + (0.5 * Sat(treeDur, SatTree)) + (0.5 * ArmourPool * itemDef) + (3.0 * durItems) + buffDur;

        // The channels the first translation did not see (mana, sustain, SD, anti-heal): see MobaF10Ledger.
        var extra = MobaF10Ledger.ExtraOf(champion, family);
        return new Pools(offense + extra.Offense, life + extra.Life, durability + extra.Durability);
    }

    /// <summary>Buffs received stack with diminishing returns: all of them count at once, the next one adds less.</summary>
    private static double BuffPool(params double[] terms)
    {
        const double Kb = 0.20;
        return Kb * (1.0 - Math.Exp(-terms.Sum() / Kb));
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
        return DamageScaleAt(champion.MobaLevel) * Math.Exp((KO * (pools.Offense - reference.Offense)) + (0.5 * Lean(family)) + (0.5 * PsiOf(champion, family))) * CritRatio(champion) * CooldownRatio(champion);
    }

    /// <summary>Expected damage of the champion's critical chance over the expected damage of the reference chance (the live roll uses the reference).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The ratio (1 = the reference).</returns>
    public static double CritRatio(Player champion)
    {
        var m = MobaCombatStats.CritMultiplier - 1.0;
        return (1.0 + (MobaCombatStats.FinalCritChanceOf(champion) * m)) / (1.0 + (CritReference * m));
    }

    /// <summary>
    /// The skills of the champion come back sooner with cooldown reduction (pieces, traits): half of its damage is bound to cooldowns,
    /// so the damage per second grows by half of the extra casts.
    /// </summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The factor (1 = no reduction).</returns>
    public static double CooldownRatio(Player champion)
    {
        var cdr = Math.Clamp(MobaItemTraits.CooldownReductionOf(champion), 0.0, 0.65);
        return 1.0 + (0.5 * cdr / (1.0 - cdr));
    }

    /// <summary>Gets the maximum life the model gives a champion.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The maximum life.</returns>
    public static float HealthOf(Player champion)
    {
        var family = MobaPassives.FamilyOf(champion);
        var pools = PoolsOf(champion);
        var reference = ReferencePools(champion.MobaLevel, family);
        var factor = HealthScaleAt(champion.MobaLevel) * Math.Exp((KD * (pools.Life - reference.Life)) - (0.5 * Lean(family)) + (0.5 * PsiOf(champion, family)));
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
        return FormattableString.Invariant($"O={pools.Offense:F2} vida={pools.Life:F2} dur={pools.Durability:F2} dano x{DamageFactor(champion):F2} vida {HealthOf(champion):F0} mit {mit:P0} recibido x{dtm:F2} golpes/s x{MobaF10Ledger.HitRateRatio(champion):F2}");
    }
}
