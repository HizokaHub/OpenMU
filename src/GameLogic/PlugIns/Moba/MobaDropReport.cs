// <copyright file="MobaDropReport.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;

/// <summary>
/// Observability of the lane creep drops (<see cref="MobaCreepDropGenerator"/>): a <c>[MOBA-DROP]</c> line per drop and
/// a running summary (<c>[MOBA-DROP-SUM]</c>, written by <see cref="MobaTelemetry"/>) with the accumulated percentages
/// against the design values 35/35/30 (nothing / Zen / item) and 65/35 (weapons and shields / sets). Only writes logs.
/// </summary>
public static class MobaDropReport
{
    private static readonly object Sync = new();

    private static int _nothing;
    private static int _zen;
    private static int _items;
    private static int _weapons;
    private static int _shields;
    private static int _sets;
    private static long _zenTotal;
    private static int _levelSum;
    private static int _excellentSum;

    /// <summary>Clears the accumulated counters (new match).</summary>
    public static void Reset()
    {
        lock (Sync)
        {
            _nothing = _zen = _items = _weapons = _shields = _sets = _levelSum = _excellentSum = 0;
            _zenTotal = 0;
        }
    }

    /// <summary>Records the nothing / Zen / item roll of a dead creep.</summary>
    /// <param name="killer">The last hitter.</param>
    /// <param name="monsterName">The creep.</param>
    /// <param name="kind">"nothing", "zen" or "item".</param>
    /// <param name="zen">The Zen amount (only for "zen").</param>
    public static void NoteRoll(Player killer, string monsterName, string kind, uint zen)
    {
        lock (Sync)
        {
            switch (kind)
            {
                case "nothing":
                    _nothing++;
                    break;
                case "zen":
                    _zen++;
                    _zenTotal += zen;
                    break;
                default:
                    _items++;
                    break;
            }
        }

        killer.Logger.LogInformation(
            "[MOBA-DROP] roll={Kind} creep={Creep} killer={Killer} phase={Phase}{Zen}",
            kind,
            monsterName,
            killer.Name,
            MobaMatchPhase.Current,
            kind == "zen" ? $" zen={zen}" : string.Empty);
    }

    /// <summary>Records the item a creep dropped.</summary>
    /// <param name="killer">The last hitter.</param>
    /// <param name="item">The item.</param>
    /// <param name="category">The shop category it came from.</param>
    /// <param name="variant">The shop variant.</param>
    public static void NoteItem(Player killer, Item item, MobaShopCategory category, MobaShopVariant variant)
    {
        var kind = category switch
        {
            MobaShopCategory.Weapons => "weapon",
            MobaShopCategory.Offhand => "shield",
            _ => "set",
        };

        var excellent = MobaShop.ExcellentCountOf(item);
        lock (Sync)
        {
            switch (kind)
            {
                case "weapon":
                    _weapons++;
                    break;
                case "shield":
                    _shields++;
                    break;
                default:
                    _sets++;
                    break;
            }

            _levelSum += item.Level;
            _excellentSum += excellent;
        }

        killer.Logger.LogInformation(
            "[MOBA-DROP] item={Item} category={Category} variant={Variant} +{Level} opt+{Opt} luck={Luck} exc={Exc} skill={Skill} killer={Killer} phase={Phase}",
            item.Definition?.Name,
            kind,
            variant,
            item.Level,
            MobaShop.OptionLevelOf(item),
            MobaShop.HasLuck(item),
            excellent,
            item.HasSkill,
            killer.Name,
            MobaMatchPhase.Current);
    }

    /// <summary>Writes the accumulated percentages against the expected 35/35/30 and 65/35.</summary>
    /// <param name="logger">A logger.</param>
    public static void WriteSummary(ILogger logger)
    {
        int nothing, zen, items, weapons, shields, sets, levelSum, excellentSum;
        long zenTotal;
        lock (Sync)
        {
            (nothing, zen, items, weapons, shields, sets, levelSum, excellentSum, zenTotal) = (_nothing, _zen, _items, _weapons, _shields, _sets, _levelSum, _excellentSum, _zenTotal);
        }

        var total = nothing + zen + items;
        if (total == 0)
        {
            return;
        }

        var dropped = weapons + shields + sets;
        double Pct(int n, int d) => d == 0 ? 0 : 100.0 * n / d;
        logger.LogInformation(
            "[MOBA-DROP-SUM] creeps={Total} | nothing {N:F0}% (exp 35) zen {Z:F0}% (exp 35) item {I:F0}% (exp 30) | items={Items}: weapons+shields {Ws:F0}% (exp 65; weapons {W}, shields {S}) sets {St:F0}% (exp 35) | avg +{Lvl:F1} avg exc {Exc:F1} | zen total {ZenTotal} (avg {ZenAvg:F0})",
            total,
            Pct(nothing, total),
            Pct(zen, total),
            Pct(items, total),
            dropped,
            Pct(weapons + shields, dropped),
            weapons,
            shields,
            Pct(sets, dropped),
            dropped == 0 ? 0 : (double)levelSum / dropped,
            dropped == 0 ? 0 : (double)excellentSum / dropped,
            zenTotal,
            zen == 0 ? 0 : (double)zenTotal / zen);
    }
}
