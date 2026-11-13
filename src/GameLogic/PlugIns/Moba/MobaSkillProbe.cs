// <copyright file="MobaSkillProbe.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlayerActions.Skills;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.Pathfinding;

/// <summary>One landed hit on a champion, as seen by the skill probe.</summary>
/// <param name="Attacker">The attacker.</param>
/// <param name="Victim">The victim champion.</param>
/// <param name="Skill">The skill, or <see langword="null"/> for a basic attack.</param>
/// <param name="Hp">The health damage after mitigation and the per-hit cap.</param>
/// <param name="Sd">The shield damage.</param>
/// <param name="Raw">The damage before mitigation and the per-hit cap, or -1 if unknown.</param>
/// <param name="Crit">Whether it was a critical hit.</param>
/// <param name="Combo">Whether it was part of a combo.</param>
/// <param name="Dist">The distance between attacker and victim.</param>
/// <param name="PctOfMax">The damage as a fraction of the victim's maximum health.</param>
public sealed record MobaProbeHit(IAttacker Attacker, Player Victim, Skill? Skill, long Hp, long Sd, int Raw, bool Crit, bool Combo, double Dist, double PctOfMax);

/// <summary>
/// Server-side skill review (2026-11-13): a champion of a class casts EVERY skill of its kit against level 30 dummies through the same handlers a
/// player uses, with no client, and measures damage per hit (before and after the per-hit cap), hits and victims, mana, cooldown, effects on the
/// victims (stun, freeze, sleep, magic effects), real range, buffs (attributes, damage dealt and taken with and without the buff), the
/// Blade Knight combo and the level curve. The result is a JSON report (see <c>tools/informe_probe.py</c>). Started with <c>/mobaskilltest</c>.
/// </summary>
public static class MobaSkillProbe
{
    private const double Grace = 3.0;
    private const int SettleMs = 700;

    private static readonly TargetedSkillDefaultPlugin DefaultPlugin = new();
    private static readonly AreaSkillAttackAction AreaAction = new();
    private static readonly ConcurrentQueue<MobaProbeHit> Hits = new();
    private static int _running;

    private static readonly (string Name, AttributeDefinition Attribute)[] WatchedAttributes =
    {
        ("MaximumHealth", Stats.MaximumHealth),
        ("MaximumShield", Stats.MaximumShield),
        ("DefenseBase", Stats.DefenseBase),
        ("DamageReceiveDecrement", Stats.DamageReceiveDecrement),
        ("GreaterDamageBonus", Stats.GreaterDamageBonus),
        ("GreaterDefenseBonus", Stats.GreaterDefenseBonus),
        ("DamageReflection", Stats.DamageReflection),
        ("CriticalDamageBonus", Stats.CriticalDamageBonus),
        ("MinimumWizBaseDmg", Stats.MinimumWizBaseDmg),
        ("BaseDamageBonus", Stats.BaseDamageBonus),
        ("SoulBarrierReceiveDecrement", Stats.SoulBarrierReceiveDecrement),
    };

    /// <summary>Whether a probe is running.</summary>
    public static bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <summary>Runs the probe for some classes and writes the JSON report.</summary>
    /// <param name="gm">The game master in the arena (the dummies are built with its context).</param>
    /// <param name="classNumbers">The classes to probe.</param>
    /// <param name="level">The champion level of caster and targets.</param>
    /// <returns>The path of the report.</returns>
    public static async ValueTask<string> RunAsync(Player gm, IReadOnlyList<byte> classNumbers, int level)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            throw new InvalidOperationException("Ya hay una prueba de skills corriendo.");
        }

        var map = gm.CurrentMap;
        MobaBotPlayer? carrier = null;
        try
        {
            if (!gm.IsMobaClone || map is null)
            {
                throw new InvalidOperationException("Solo dentro de una partida MOBA.");
            }

            await MobaBotPlayer.ClearAllAsync(map).ConfigureAwait(false);
            MobaTelemetry.ProbeObserver = hit => Hits.Enqueue(hit);
            var started = DateTime.UtcNow;
            var classes = new List<object>();

            // The bodies are built with the persistence context of a carrier bot that lives as long as the run: the context of the game master
            // is disposed when its client closes, and the review must not depend on it.
            carrier = new MobaBotPlayer(gm.GameContext, MobaTeam.Blue, isDummy: true);
            foreach (var number in classNumbers)
            {
                gm.Logger.LogInformation("[MOBA-PROBE] class {Class} start", number);
                try
                {
                    classes.Add(await RunClassAsync(carrier, number, level, quick: number is 4 or 7).ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    gm.Logger.LogWarning(ex, "[MOBA-PROBE] class {Class} failed", number);
                    classes.Add(new Dictionary<string, object?> { ["class"] = number, ["error"] = ex.Message });
                }

                await MobaBotPlayer.ClearAllAsync(map).ConfigureAwait(false);
            }

            var report = new Dictionary<string, object?>
            {
                ["started"] = started.ToString("o"),
                ["seconds"] = Math.Round((DateTime.UtcNow - started).TotalSeconds),
                ["level"] = level,
                ["classes"] = classes,
            };
            var path = Path.Combine(AppContext.BaseDirectory, $"skilltest_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            gm.Logger.LogInformation("[MOBA-PROBE] report={Path}", path);
            return path;
        }
        finally
        {
            MobaTelemetry.ProbeObserver = null;
            carrier?.PersistenceContext.Dispose();
            if (map is not null)
            {
                await MobaBotPlayer.ClearAllAsync(map).ConfigureAwait(false);
            }

            Interlocked.Exchange(ref _running, 0);
        }
    }

    private sealed class Ctx
    {
        public Player Gm = null!;
        public int Level;
        public MobaBotPlayer Caster = null!;
        public MobaBotPlayer Attacker = null!;
        public MobaBotPlayer Ally = null!;
        public List<MobaBotPlayer> Targets = new();
        public List<Point> Homes = new();
        public List<Dictionary<string, object?>> Rows = new();
    }

    private static async ValueTask<Dictionary<string, object?>> RunClassAsync(Player gm, byte classNumber, int level, bool quick)
    {
        var characterClass = gm.GameContext.Configuration.CharacterClasses.First(c => c.Number == classNumber);
        var c = new Ctx { Gm = gm, Level = level };
        c.Caster = await SpawnAsync(gm, classNumber, "probeC", MobaTeam.Blue, new Point(120, 124), level).ConfigureAwait(false) ?? throw new InvalidOperationException("no se pudo crear el lanzador");
        c.Attacker = await SpawnAsync(gm, 6, "probeB", MobaTeam.Red, new Point(120, 118), level).ConfigureAwait(false) ?? throw new InvalidOperationException("no se pudo crear el atacante");
        c.Ally = await SpawnAsync(gm, 6, "probeA", MobaTeam.Blue, new Point(120, 129), level).ConfigureAwait(false) ?? throw new InvalidOperationException("no se pudo crear el aliado");
        for (var i = 0; i < 3; i++)
        {
            var t = await SpawnAsync(gm, 6, $"probeT{i}", MobaTeam.Red, new Point((byte)(120 + (i == 1 ? 1 : 0) + (i == 2 ? 1 : 0)), (byte)(i == 2 ? 129 : 128)), level).ConfigureAwait(false) ?? throw new InvalidOperationException("no se pudo crear un objetivo");
            c.Targets.Add(t);
            c.Homes.Add(new Point((byte)(120 + (i == 1 ? 1 : 0) + (i == 2 ? 1 : 0)), (byte)(i == 2 ? 129 : 128)));
        }

        await Task.Delay(500).ConfigureAwait(false);

        var info = new Dictionary<string, object?>
        {
            ["class"] = classNumber,
            ["className"] = characterClass.Name.ToString(),
            ["family"] = MobaPassives.FamilyOf(c.Caster).ToString(),
            ["comboDefined"] = c.Caster.ComboState is not null,
            ["maxHealth"] = Math.Round(c.Caster.Attributes![Stats.MaximumHealth]),
            ["maxMana"] = Math.Round(c.Caster.Attributes[Stats.MaximumMana]),
            ["manaRegenPerSecond"] = Math.Round(MobaMana.RegenPerSecond(c.Caster), 1),
            ["targetMaxHealth"] = Math.Round(c.Targets[0].Attributes![Stats.MaximumHealth]),
            ["targetMaxShield"] = Math.Round(c.Targets[0].Attributes![Stats.MaximumShield]),
            ["perHitCap"] = 0.30,
        };

        var character = c.Caster.SelectedCharacter!;
        var kit = character.LearnedSkills.Where(e => e.Skill is not null).OrderBy(e => e.Skill!.Number).ToList();
        info["kit"] = kit.Select(e => $"{e.Skill!.Number} {e.Skill.Name}").ToList();

        if (!quick)
        {
            // 1) attacks, summons, heals and buffs
            foreach (var entry in kit)
            {
                try
                {
                    await ProbeSkillAsync(c, entry).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    c.Rows.Add(new Dictionary<string, object?> { ["skill"] = entry.Skill!.Number, ["name"] = entry.Skill.Name.ToString(), ["test"] = "error", ["error"] = ex.Message });
                }
            }

            // 2) buffs: attributes, damage dealt and taken with and without them
            var probeAttack = kit.FirstOrDefault(e => Classify(e.Skill!) == "attack" && e.Skill!.SkillType == SkillType.DirectHit);
            foreach (var entry in kit.Where(e => Classify(e.Skill!) is "buff" or "heal"))
            {
                try
                {
                    await ProbeBuffAsync(c, entry, probeAttack).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    c.Rows.Add(new Dictionary<string, object?> { ["skill"] = entry.Skill!.Number, ["name"] = entry.Skill.Name.ToString(), ["test"] = "buff-error", ["error"] = ex.Message });
                }
            }

            // 3) level curve
            info["levelCurve"] = LevelCurve(c, kit);
        }

        // 4) combo
        info["combo"] = await ProbeComboAsync(c, kit).ConfigureAwait(false);
        info["rows"] = c.Rows;
        return info;
    }

    private static string Classify(Skill skill)
    {
        var n = (short)skill.Number;
        return n switch
        {
            6 => "skip-teleport",
            40 => "skip-channel",
            63 => "skip-partysummon",
            _ when MobaLoadouts.IsCreatureSummon(n) => "summon",
            _ when MobaLoadouts.IsBuff(n) => skill.SkillType == SkillType.Regeneration ? "heal" : "buff",
            _ => "attack",
        };
    }

    private static async ValueTask<MobaBotPlayer?> SpawnAsync(Player gm, byte classNumber, string name, MobaTeam team, Point at, int level)
    {
        var characterClass = gm.GameContext.Configuration.CharacterClasses.FirstOrDefault(cc => cc.Number == classNumber);
        if (characterClass is null)
        {
            return null;
        }

        var clone = await MobaCloneFactory.BuildForClassAsync(gm, characterClass, name).ConfigureAwait(false);
        var account = gm.PersistenceContext.CreateNew<Account>();
        account.LoginName = $"#probe_{name}_{(DateTime.UtcNow.Ticks / 10000) % 100000}";
        var bot = new MobaBotPlayer(gm.GameContext, team, isDummy: true);
        if (!await bot.StartMobaAsync(account, clone, at).ConfigureAwait(false))
        {
            return null;
        }

        bot.MobaLevel = level;
        MobaProgression.ApplyLevelScaling(bot);
        Refill(bot);
        return bot;
    }

    private static void Refill(Player p)
    {
        if (p.Attributes is { } a)
        {
            a[Stats.CurrentHealth] = a[Stats.MaximumHealth];
            a[Stats.CurrentMana] = a[Stats.MaximumMana];
            a[Stats.CurrentShield] = a[Stats.MaximumShield];
        }
    }

    private static void ResetCooldowns(Player p)
    {
        p.MobaSkillCooldowns.Clear();
        p.MobaSkillGraceEnds.Clear();
    }

    private static async ValueTask CastCoreAsync(Player caster, SkillEntry entry, IAttackable? target)
    {
        var skill = entry.Skill!;
        if (target is not null)
        {
            caster.Rotation = caster.Position.GetDirectionTo(target.Position);
        }

        if (skill.SkillType is SkillType.AreaSkillAutomaticHits or SkillType.AreaSkillExplicitHits or SkillType.AreaSkillExplicitTarget)
        {
            var key = target is IIdentifiable identifiable ? identifiable.GetId(caster) : (ushort)0xFFFF;
            await AreaAction.AttackAsync(caster, key, (ushort)skill.Number, (target ?? caster).Position, (byte)caster.Rotation, true).ConfigureAwait(false);
            return;
        }

        var strategy = caster.GameContext.PlugInManager.GetStrategy<short, ITargetedSkillPlugin>(skill.Number) ?? DefaultPlugin;
        await strategy.PerformSkillAsync(caster, target ?? caster, (ushort)skill.Number).ConfigureAwait(false);
    }

    private static async ValueTask ResetTargetsAsync(Ctx c)
    {
        for (var i = 0; i < c.Targets.Count; i++)
        {
            await PlaceAsync(c.Targets[i], c.Homes[i].X, c.Homes[i].Y).ConfigureAwait(false);
            await ClearEffectsAsync(c.Targets[i]).ConfigureAwait(false);
            Refill(c.Targets[i]);
        }
    }

    private static async ValueTask PlaceAsync(Player p, int x, int y) => await p.MoveAsync(new Point((byte)x, (byte)y)).ConfigureAwait(false);

    private static string EffectsOf(Player p)
        => string.Join(",", p.MagicEffectList.ActiveEffects.Values.Select(e => $"{e.Id}:{e.Definition.Name}:{e.Duration.TotalSeconds:F0}s"));

    private static async ValueTask ClearEffectsAsync(Player p)
    {
        foreach (var effect in p.MagicEffectList.ActiveEffects.Values.ToList())
        {
            try
            {
                await effect.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }
        }
    }

    private static Dictionary<string, object?> Snapshot(Player p)
        => WatchedAttributes.ToDictionary(w => w.Name, w => (object?)Math.Round(p.Attributes![w.Attribute], 3));

    /// <summary>One cast of a skill by <paramref name="caster"/> and everything measured from it.</summary>
    private static async ValueTask<Dictionary<string, object?>> MeasureAsync(Ctx c, Player caster, SkillEntry entry, int rank, string test, double distance, IAttackable? target, int settleMs = SettleMs, bool refill = true)
    {
        var skill = entry.Skill!;
        var row = new Dictionary<string, object?> { ["skill"] = skill.Number, ["name"] = skill.Name.ToString(), ["type"] = skill.SkillType.ToString(), ["declaredRange"] = skill.Range, ["rank"] = rank, ["test"] = test, ["distance"] = distance };
        entry.Level = rank;
        ResetCooldowns(caster);
        if (refill)
        {
            Refill(caster);
        }

        await ResetTargetsAsync(c).ConfigureAwait(false);

        if (target is not null)
        {
            await PlaceAsync(caster, target.Position.X, target.Position.Y - (int)distance).ConfigureAwait(false);
        }

        while (Hits.TryDequeue(out _))
        {
        }

        var mana0 = caster.Attributes![Stats.CurrentMana];
        var t0 = DateTime.UtcNow;
        try
        {
            await CastCoreAsync(caster, entry, target).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            row["exception"] = ex.Message;
        }

        var spent = mana0 - caster.Attributes[Stats.CurrentMana];
        row["hpAfterCast"] = Math.Round(caster.Attributes[Stats.CurrentHealth]);
        row["shieldAfterCast"] = Math.Round(caster.Attributes[Stats.CurrentShield]);
        await Task.Delay(settleMs).ConfigureAwait(false);
        var mine = Hits.Where(h => ReferenceEquals(h.Attacker, caster)).ToList();
        var hasCooldown = caster.MobaSkillCooldowns.TryGetValue(skill.Number, out var ready) && ready > t0;
        row["cast"] = spent > 0.5f || mine.Count > 0 || hasCooldown;
        row["manaSpent"] = Math.Round(spent);
        row["cooldownSeconds"] = hasCooldown ? Math.Round((ready - t0).TotalSeconds - Grace, 2) : null;
        row["hits"] = mine.Count;
        row["hitsDirect"] = mine.Count(h => h.Raw >= 0);
        row["victims"] = mine.Select(h => h.Victim.Name).Distinct().Count();
        row["allyHit"] = mine.Any(h => h.Victim.Name == c.Ally.Name);
        var raws = mine.Where(h => h.Raw >= 0).Select(h => h.Raw).ToList();
        row["rawAvg"] = raws.Count > 0 ? Math.Round(raws.Average()) : null;
        row["rawMin"] = raws.Count > 0 ? raws.Min() : null;
        row["rawMax"] = raws.Count > 0 ? raws.Max() : null;
        row["hpTotal"] = mine.Sum(h => h.Hp);
        row["sdTotal"] = mine.Sum(h => h.Sd);
        row["crits"] = mine.Count(h => h.Crit);
        row["combo"] = mine.Any(h => h.Combo);
        row["maxPctOfMaxHp"] = mine.Count > 0 ? Math.Round(mine.Max(h => h.PctOfMax), 3) : null;
        row["firstHitDistance"] = mine.Count > 0 ? Math.Round(mine[0].Dist, 1) : null;
        row["targetEffects"] = c.Targets.Select(t => EffectsOf(t)).Where(s => s.Length > 0).ToList();
        row["targetFlags"] = c.Targets.Select(t => $"stun={t.Attributes![Stats.IsStunned]:0} freeze={t.Attributes[Stats.IsFrozen]:0} sleep={t.Attributes[Stats.IsAsleep]:0}").Distinct().ToList();
        return row;
    }

    private static async ValueTask ProbeSkillAsync(Ctx c, SkillEntry entry)
    {
        var skill = entry.Skill!;
        var kind = Classify(skill);
        var declared = skill.Range > 0 ? skill.Range : 6;
        var target = c.Targets[0];
        switch (kind)
        {
            case "attack":
                foreach (var rank in new[] { 1, 5 })
                {
                    var row = await MeasureAsync(c, c.Caster, entry, rank, "dmg", 2, target).ConfigureAwait(false);
                    MobaSkillDamage.GetSkillBaseDamage(c.Caster, (short)skill.Number, rank, out var min, out var max);
                    row["modelMin"] = min;
                    row["modelMax"] = max;
                    c.Rows.Add(row);
                }

                foreach (var distance in new[] { declared, declared + 2, declared + 3 })
                {
                    c.Rows.Add(await MeasureAsync(c, c.Caster, entry, 1, "range", distance, target).ConfigureAwait(false));
                }

                break;
            case "summon":
                {
                    var row = await MeasureAsync(c, c.Caster, entry, 1, "summon", 2, null).ConfigureAwait(false);
                    row["summonCreated"] = c.Caster.Summon is not null;
                    c.Rows.Add(row);
                    await c.Caster.RemoveSummonAsync().ConfigureAwait(false);
                    break;
                }

            case "heal" or "buff":
                break; // measured in ProbeBuffAsync
            default:
                c.Rows.Add(new Dictionary<string, object?> { ["skill"] = skill.Number, ["name"] = skill.Name.ToString(), ["test"] = kind, ["note"] = "no se mide en el servidor" });
                break;
        }
    }

    private static async ValueTask<(double Out, double Taken, double Reflected)> BaselineAsync(Ctx c, SkillEntry? probeAttack, int samples = 5)
    {
        var outs = new List<double>();
        var takens = new List<double>();
        var reflects = new List<double>();
        for (var i = 0; i < samples; i++)
        {
            var (o, t, r) = await BaselineOnceAsync(c, probeAttack).ConfigureAwait(false);
            if (o > 0)
            {
                outs.Add(o);
            }

            if (t > 0)
            {
                takens.Add(t);
            }

            reflects.Add(r);
        }

        return (outs.Count > 0 ? Math.Round(outs.Average()) : 0, takens.Count > 0 ? Math.Round(takens.Average()) : 0, Math.Round(reflects.Average()));
    }

    private static async ValueTask<(double Out, double Taken, double Reflected)> BaselineOnceAsync(Ctx c, SkillEntry? probeAttack)
    {
        double dealt = 0;
        if (probeAttack is not null)
        {
            var row = await MeasureAsync(c, c.Caster, probeAttack, 1, "baseline-out", 2, c.Targets[0]).ConfigureAwait(false);
            dealt = row["rawAvg"] is double d ? d : 0;
        }

        // the attacker (a level 30 Blade Knight) hits the caster with Falling Slash at rank 5
        var fallingSlash = c.Attacker.SelectedCharacter!.LearnedSkills.First(e => e.Skill!.Number == 19);
        Refill(c.Caster);
        await PlaceAsync(c.Caster, 120, 126).ConfigureAwait(false);
        await PlaceAsync(c.Attacker, 120, 124).ConfigureAwait(false);
        fallingSlash.Level = 5;
        ResetCooldowns(c.Attacker);
        Refill(c.Attacker);
        while (Hits.TryDequeue(out _))
        {
        }

        await CastCoreAsync(c.Attacker, fallingSlash, c.Caster).ConfigureAwait(false);
        var reflected = c.Attacker.Attributes![Stats.MaximumHealth] - c.Attacker.Attributes[Stats.CurrentHealth] + Math.Max(0f, c.Attacker.Attributes[Stats.MaximumShield] - c.Attacker.Attributes[Stats.CurrentShield]);
        await Task.Delay(SettleMs).ConfigureAwait(false);
        var taken = Hits.Where(h => ReferenceEquals(h.Attacker, c.Attacker) && ReferenceEquals(h.Victim, c.Caster)).Sum(h => h.Hp + h.Sd);
        return (dealt, taken, reflected);
    }

    private static async ValueTask ProbeBuffAsync(Ctx c, SkillEntry entry, SkillEntry? probeAttack)
    {
        var skill = entry.Skill!;
        foreach (var rank in new[] { 1, 5 })
        {
            await ClearEffectsAsync(c.Caster).ConfigureAwait(false);
            var row = new Dictionary<string, object?> { ["skill"] = skill.Number, ["name"] = skill.Name.ToString(), ["type"] = skill.SkillType.ToString(), ["test"] = "buff", ["rank"] = rank };
            var (outBefore, takenBefore, reflectBefore) = await BaselineAsync(c, probeAttack).ConfigureAwait(false);
            Refill(c.Caster);
            await ClearEffectsAsync(c.Caster).ConfigureAwait(false);
            var attrBefore = Snapshot(c.Caster);
            c.Caster.Attributes![Stats.CurrentHealth] = c.Caster.Attributes[Stats.MaximumHealth] * 0.5f;
            c.Caster.Attributes[Stats.CurrentShield] = 0;
            var hp0 = c.Caster.Attributes[Stats.CurrentHealth];
            var measure = await MeasureAsync(c, c.Caster, entry, rank, "buff-cast", 2, null, SettleMs, refill: false).ConfigureAwait(false);
            row["cast"] = measure["cast"];
            row["manaSpent"] = measure["manaSpent"];
            row["cooldownSeconds"] = measure["cooldownSeconds"];
            row["hpHealed"] = Math.Round(Convert.ToDouble(measure["hpAfterCast"]) - hp0);
            row["shieldGained"] = measure["shieldAfterCast"];
            row["effects"] = EffectsOf(c.Caster);
            var attrAfter = Snapshot(c.Caster);
            row["attributeChanges"] = attrAfter.Where(kv => !Equals(kv.Value, attrBefore[kv.Key])).ToDictionary(kv => kv.Key, kv => (object?)$"{attrBefore[kv.Key]} -> {kv.Value}");
            var (outAfter, takenAfter, reflectAfter) = await BaselineAsync(c, probeAttack).ConfigureAwait(false);
            row["damageDealtBefore"] = outBefore;
            row["damageDealtAfter"] = outAfter;
            row["damageTakenBefore"] = takenBefore;
            row["damageTakenAfter"] = takenAfter;
            row["reflectedBefore"] = reflectBefore;
            row["reflectedAfter"] = reflectAfter;
            c.Rows.Add(row);
        }

        await ClearEffectsAsync(c.Caster).ConfigureAwait(false);
    }

    private static List<Dictionary<string, object?>> LevelCurve(Ctx c, List<SkillEntry> kit)
    {
        var curve = new List<Dictionary<string, object?>>();
        var firstAttack = kit.FirstOrDefault(e => Classify(e.Skill!) == "attack");
        foreach (var lvl in new[] { 1, 5, 10, 15, 20, 25, 30 })
        {
            c.Caster.MobaLevel = lvl;
            MobaProgression.ApplyLevelScaling(c.Caster);
            var row = new Dictionary<string, object?>
            {
                ["level"] = lvl,
                ["maxHealth"] = Math.Round(c.Caster.Attributes![Stats.MaximumHealth]),
                ["maxMana"] = Math.Round(c.Caster.Attributes[Stats.MaximumMana]),
                ["damageScale"] = Math.Round(MobaProgression.DamageScale(lvl), 2),
            };
            MobaSkillDamage.GetBasicAttackDamage(c.Caster, out var bmin, out var bmax);
            row["basicMin"] = bmin;
            row["basicMax"] = bmax;
            if (firstAttack is not null)
            {
                MobaSkillDamage.GetSkillBaseDamage(c.Caster, (short)firstAttack.Skill!.Number, 1, out var smin, out var smax);
                row["firstSkill"] = firstAttack.Skill.Name.ToString();
                row["firstSkillRank1Min"] = smin;
                row["firstSkillRank1Max"] = smax;
            }

            curve.Add(row);
        }

        c.Caster.MobaLevel = c.Level;
        MobaProgression.ApplyLevelScaling(c.Caster);
        Refill(c.Caster);
        return curve;
    }

    private static async ValueTask<Dictionary<string, object?>> ProbeComboAsync(Ctx c, List<SkillEntry> kit)
    {
        var result = new Dictionary<string, object?> { ["defined"] = c.Caster.ComboState is not null };
        if (c.Caster.ComboState is null)
        {
            return result;
        }

        SkillEntry? Get(int n) => kit.FirstOrDefault(e => e.Skill!.Number == n);
        var sequences = new (string Name, int[] Skills)[]
        {
            ("19-41-42 (debe completar)", new[] { 19, 41, 42 }),
            ("22-43-41 (debe completar)", new[] { 22, 43, 41 }),
            ("19-19-41 (no debe)", new[] { 19, 19, 41 }),
            ("23-42-43 (debe completar)", new[] { 23, 42, 43 }),
        };
        var list = new List<object>();
        foreach (var (name, skills) in sequences)
        {
            var steps = new List<object>();
            ResetCooldowns(c.Caster);
            foreach (var n in skills)
            {
                if (Get(n) is not { } entry)
                {
                    steps.Add(new Dictionary<string, object?> { ["skill"] = n, ["note"] = "no está en el kit" });
                    continue;
                }

                entry.Level = 5;
                await ResetTargetsAsync(c).ConfigureAwait(false);
                c.Caster.MobaSkillCooldowns.Remove((short)n);
                c.Caster.MobaSkillGraceEnds.Remove((short)n);
                Refill(c.Caster);
                await PlaceAsync(c.Caster, 120, 126).ConfigureAwait(false);
                while (Hits.TryDequeue(out _))
                {
                }

                await CastCoreAsync(c.Caster, entry, c.Targets[0]).ConfigureAwait(false);
                await Task.Delay(450).ConfigureAwait(false);
                var mine = Hits.Where(h => ReferenceEquals(h.Attacker, c.Caster)).ToList();
                steps.Add(new Dictionary<string, object?>
                {
                    ["skill"] = n,
                    ["hits"] = mine.Count,
                    ["combo"] = mine.Any(h => h.Combo),
                    ["rawAvg"] = mine.Count > 0 ? Math.Round(mine.Where(h => h.Raw >= 0).Select(h => (double)h.Raw).DefaultIfEmpty(0).Average()) : null,
                });
            }

            list.Add(new Dictionary<string, object?> { ["sequence"] = name, ["steps"] = steps });
        }

        result["sequences"] = list;
        MobaSkillDamage.GetBasicAttackDamage(c.Caster, out _, out _);
        result["comboBonusModel"] = MobaSkillDamage.GetComboBonus(c.Caster);
        return result;
    }
}
