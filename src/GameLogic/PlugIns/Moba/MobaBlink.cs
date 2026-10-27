// <copyright file="MobaBlink.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The Teleport skill of the Soul Master and Grand Master (skill 6) in a match (2026-10-25): a short jump of up to 6 tiles to a walkable
/// tile, like a "flash". The champion has it besides its attacks (it is listed with the buffs: it is not one of the picks and its
/// ranks come from the buff points). The mana is the usual one times 3 and the cooldown of the skill is only one second: what limits the
/// jumps is the <b>AG gauge</b>.
/// <para>
/// A full gauge (<see cref="GaugePool"/>) pays <see cref="ChargesPerFullGauge"/> jumps. The gauge refills by itself fast enough for one more jump
/// every <see cref="BaseSecondsPerCharge"/> seconds; the accessories (the two rings and the pendant) add AG regeneration, up to a jump every
/// <see cref="BestSecondsPerCharge"/> seconds with three tier 3 pieces. Only the Soul Master family uses the gauge: its other skills ask for no
/// AG in a match (see <see cref="MobaBuffs"/>), and the native AG regeneration is switched off.
/// </para>
/// </summary>
public static class MobaBlink
{
    /// <summary>The number of the Teleport skill.</summary>
    public const short SkillNumber = 6;

    /// <summary>The AG of a full gauge.</summary>
    public const float GaugePool = 90f;

    /// <summary>How many jumps a full gauge pays.</summary>
    public const int ChargesPerFullGauge = 3;

    /// <summary>Seconds to regenerate the AG of a jump without accessories.</summary>
    public const double BaseSecondsPerCharge = 4.5;

    /// <summary>Seconds to regenerate the AG of a jump with the best accessories (three tier 3 pieces).</summary>
    public const double BestSecondsPerCharge = 2.5;

    /// <summary>How many accessories add AG regeneration: the two rings and the pendant.</summary>
    public const int AccessorySlots = 3;

    private const double TierCount = 3.0;

    private static readonly byte[] AccessorySlotNumbers = { InventoryConstants.Ring1Slot, InventoryConstants.Ring2Slot, InventoryConstants.PendantSlot };

    private static readonly ConditionalWeakTable<Player, Carry> Carries = new();

    private static DateTime _lastTickUtc = DateTime.MinValue;

    /// <summary>The AG one jump costs.</summary>
    public static float JumpCost => GaugePool / ChargesPerFullGauge;

    /// <summary>The AG regenerated per second without accessories.</summary>
    public static double BaseRegenPerSecond => JumpCost / BaseSecondsPerCharge;

    /// <summary>
    /// The extra AG regeneration (as a fraction of the base one) a tier 3 accessory gives; three of them regenerate a jump every
    /// <see cref="BestSecondsPerCharge"/> seconds.
    /// </summary>
    public static double AccessoryBonusAtFullTier => ((BaseSecondsPerCharge / BestSecondsPerCharge) - 1.0) / AccessorySlots;

    /// <summary>The extra AG regeneration of an accessory of a tier, as a fraction of the base regeneration.</summary>
    /// <param name="tier">The tier of the accessory, 0 for none.</param>
    /// <returns>The fraction (0 without an accessory).</returns>
    public static double AccessoryBonusOf(int tier) => Math.Clamp(tier, 0, (int)TierCount) / TierCount * AccessoryBonusAtFullTier;

    /// <summary>The AG regenerated per second with accessories of the given tiers.</summary>
    /// <param name="accessoryTiers">The tier of each accessory worn (0 for an empty slot).</param>
    /// <returns>The AG per second.</returns>
    public static double RegenPerSecondFor(IEnumerable<int> accessoryTiers)
        => BaseRegenPerSecond * (1.0 + accessoryTiers.Sum(AccessoryBonusOf));

    /// <summary>The AG a champion regenerates per second, by its accessories.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The AG per second.</returns>
    public static double RegenPerSecond(Player champion)
        => RegenPerSecondFor(AccessorySlotNumbers.Select(slot => MobaItemPower.TierIn(champion, slot, MobaShopCategory.Accessories)));

    /// <summary>Whether the champion pays its jumps with the AG gauge (the Soul Master family).</summary>
    /// <param name="player">The player.</param>
    /// <returns><see langword="true"/> for a champion of the Soul Master family.</returns>
    public static bool UsesGauge(Player player) => player.IsMobaClone && MobaPassives.FamilyOf(player) == MobaFamily.Wizard;

    /// <summary>
    /// Whether a champion can jump to a tile: never before the match starts (the barrier of the base would be crossed), never onto the
    /// green aura of the enemy base (nobody should land on the vendor), and only with the AG of a jump.
    /// </summary>
    /// <param name="player">The player who casts it.</param>
    /// <param name="target">The tile.</param>
    /// <returns><see langword="true"/> if the jump is allowed.</returns>
    public static bool Allows(Player player, Point target)
    {
        if (!player.IsMobaClone)
        {
            return true;
        }

        if (!player.IsAlive || player.CurrentMap is not { } map || MobaArena.Of(map).IsPreMatch || !HasGauge(player))
        {
            return false;
        }

        var team = MobaTeams.GetTeam(player);
        var enemy = team == MobaTeam.Blue ? MobaTeam.Red : team == MobaTeam.Red ? MobaTeam.Blue : MobaTeam.None;
        return !MobaBaseAura.Contains(enemy, target.X, target.Y);
    }

    /// <summary>
    /// The tile a jump lands on: the clicked one, or - when it is farther than the range of the skill - the last walkable tile of the line towards it
    /// within the range (a click too far still makes a jump of the full range instead of being refused).
    /// </summary>
    /// <param name="player">The player who casts it.</param>
    /// <param name="requested">The tile clicked.</param>
    /// <param name="range">The range of the skill in tiles.</param>
    /// <param name="target">The tile to jump to.</param>
    /// <returns><see langword="false"/> if no walkable tile is reachable (the target is the requested one then).</returns>
    public static bool TryClampTarget(Player player, Point requested, int range, out Point target)
    {
        target = requested;
        if (player.CurrentMap?.Terrain is not { } terrain)
        {
            return false;
        }

        var from = player.Position;
        var dx = requested.X - from.X;
        var dy = requested.Y - from.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        var reach = Math.Min(distance, Math.Max(1, range));
        if (distance < 0.5)
        {
            return false;
        }

        for (var step = (int)Math.Floor(reach); step >= 1; step--)
        {
            var x = (int)Math.Round(from.X + (dx / distance * step));
            var y = (int)Math.Round(from.Y + (dy / distance * step));
            if (x >= 0 && y >= 0 && x < terrain.WalkMap.GetLength(0) && y < terrain.WalkMap.GetLength(1) && terrain.WalkMap[x, y])
            {
                target = new Point((byte)x, (byte)y);
                return true;
            }
        }

        return false;
    }

    /// <summary>Takes the AG of the jump from the gauge (called once the jump was paid in mana and started).</summary>
    /// <param name="player">The player who jumps.</param>
    /// <returns>Always <see langword="true"/>: the gauge was checked by <see cref="Allows"/>.</returns>
    public static bool PayGauge(Player player)
    {
        if (UsesGauge(player) && player.Attributes is { } attributes)
        {
            attributes[Stats.CurrentAbility] = Math.Max(0f, attributes[Stats.CurrentAbility] - JumpCost);
        }

        return true;
    }

    /// <summary>Tells a Soul Master without the AG of a jump how long it takes to have it.</summary>
    /// <param name="player">The player.</param>
    /// <returns>A task.</returns>
    public static async ValueTask TellMissingGaugeAsync(Player player)
    {
        if (!UsesGauge(player) || player.Attributes is not { } attributes || HasGauge(player))
        {
            return;
        }

        var seconds = Math.Ceiling((JumpCost - attributes[Stats.CurrentAbility]) / RegenPerSecond(player));
        await player.ShowBlueMessageAsync($"[MOBA] Sin AG para teletransportarte: faltan {seconds:0} s.").ConfigureAwait(false);
    }

    /// <summary>
    /// Pins the AG gauge of a champion of the Soul Master family to the size of the match, with the native AG regeneration off (call it when
    /// its level scaling is applied).
    /// </summary>
    /// <param name="champion">The champion.</param>
    public static void PinGauge(Player champion)
    {
        if (!UsesGauge(champion) || champion.Attributes is not { } attributes)
        {
            return;
        }

        attributes.AddElement(new SimpleElement(GaugePool - attributes[Stats.MaximumAbility], AggregateType.AddRaw), Stats.MaximumAbility);
        attributes.AddElement(new SimpleElement(-attributes[Stats.AbilityRecoveryMultiplier], AggregateType.AddRaw), Stats.AbilityRecoveryMultiplier);
        attributes.AddElement(new SimpleElement(-attributes[Stats.AbilityRecoveryAbsolute], AggregateType.AddRaw), Stats.AbilityRecoveryAbsolute);
        attributes[Stats.CurrentAbility] = attributes[Stats.MaximumAbility];
    }

    /// <summary>Regenerates the AG of the Soul Masters of every match (called every second by the match tick).</summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    public static async ValueTask TickAsync(IGameContext gameContext)
    {
        var now = DateTime.UtcNow;
        var seconds = _lastTickUtc == DateTime.MinValue ? 1.0 : Math.Clamp((now - _lastTickUtc).TotalSeconds, 0.05, 2.5);
        _lastTickUtc = now;

        var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        foreach (var champion in players.Where(p => UsesGauge(p) && p.IsAlive && p.Attributes is not null))
        {
            var attributes = champion.Attributes!;
            var maximum = attributes[Stats.MaximumAbility];
            if (attributes[Stats.CurrentAbility] >= maximum)
            {
                continue;
            }

            var carry = Carries.GetOrCreateValue(champion);
            carry.Value += RegenPerSecond(champion) * seconds;
            var whole = Math.Floor(carry.Value);
            carry.Value -= whole;
            attributes[Stats.CurrentAbility] = (float)Math.Min(maximum, attributes[Stats.CurrentAbility] + whole);
        }
    }

    private static bool HasGauge(Player player)
        => !UsesGauge(player) || (player.Attributes?[Stats.CurrentAbility] ?? 0f) >= JumpCost - 0.5f;

    private sealed class Carry
    {
        public double Value;
    }
}
