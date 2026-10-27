// <copyright file="WizardTeleportAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.Views.World;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Action to warp to another place through a gate.
/// </summary>
public class WizardTeleportAction
{
    private const ushort TeleportSkillId = 6;
    private const ushort TeleportTargetSkillId = 15;

    private static readonly byte[] PreventingMagicEffects =
    {
        0x39, // Stone
        0x3D, // Stun
        0x48, // Sleep
        0x92, // Freeze2
        0x93, // Earth Binds
    };

    /// <summary>
    /// Tries to teleport to the specified target with the teleport skill.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="target">The target.</param>
    public async ValueTask TryTeleportWithSkillAsync(Player player, Point target)
    {
        if (player.IsMobaClone)
        {
            await TryMobaBlinkAsync(player, target).ConfigureAwait(false);
            return;
        }

        // todo: additional checks:
        //  - During castle siege, can't teleport over the non-destroyed gates (simple y-axis check)
        if (!player.IsAtSafezone()
            && player.IsActive()
            && player.SkillList?.GetSkill(TeleportSkillId) is { } skillEntry
            && skillEntry.Skill is { } skill
            && player.CurrentMap!.Terrain.WalkMap[target.X, target.Y]
            && !player.CurrentMap.Terrain.SafezoneMap[target.X, target.Y]
            && player.IsInRange(target, skill.Range)
            && PlugIns.Moba.MobaBlink.Allows(player, target)
            && CanPlayerBeTeleported(player)
            && await player.TryConsumeForSkillAsync(skillEntry).ConfigureAwait(false)
            && PlugIns.Moba.MobaBlink.PayGauge(player))
        {
            _ = Task.Run(() => player.TeleportAsync(target, skill));
        }
        else
        {
            await PlugIns.Moba.MobaBlink.TellMissingGaugeAsync(player).ConfigureAwait(false);
            await player.InvokeViewPlugInAsync<ITeleportPlugIn>(p => p.ShowTeleportedAsync()).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Tries to teleport the target with the 'Teleport Ally' skill.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="targetId">The target identifier.</param>
    /// <param name="target">The target.</param>
    public async ValueTask TryTeleportTargetWithSkillAsync(Player player, ushort targetId, Point target)
    {
        if (!player.IsAtSafezone()
            && player.IsActive()
            && player.SkillList?.GetSkill(TeleportTargetSkillId) is { } skillEntry
            && skillEntry.Skill is { } skill
            && player.Party is not null
            && player.CurrentMap!.Terrain.WalkMap[target.X, target.Y]
            && !player.CurrentMap.Terrain.SafezoneMap[target.X, target.Y]
            && await player.GetObservingPlayerWithIdAsync(targetId).ConfigureAwait(false) is { } targetPlayer
            && targetPlayer.Party == player.Party
            && targetPlayer.IsActive()
            && CanPlayerBeTeleported(targetPlayer)
            && targetPlayer.IsInRange(target, skill.Range + 1)
            && await player.TryConsumeForSkillAsync(skillEntry).ConfigureAwait(false))
        {
            _ = Task.Run(() => targetPlayer.TeleportAsync(target, skill));
        }
    }

    /// <summary>
    /// The Teleport skill in a MOBA match (see <see cref="PlugIns.Moba.MobaBlink"/>): a jump to the tile clicked, shortened to the range of the skill when
    /// it was clicked farther away; it ignores the safezone flags of the map (the bases are not one for the jumps, the rules are the ones of the MOBA) and
    /// always answers, so the client never stays hidden in its teleport animation. Says in the server log why a jump was refused.
    /// </summary>
    private static async ValueTask TryMobaBlinkAsync(Player player, Point requested)
    {
        var skillEntry = player.SkillList?.GetSkill(TeleportSkillId);
        var target = requested;
        string? refusal = null;
        if (!player.IsActive() || player.CurrentMap is null)
        {
            refusal = "the champion is not active";
        }
        else if (skillEntry?.Skill is null)
        {
            refusal = "the champion has no Teleport skill";
        }
        else if (!PlugIns.Moba.MobaBlink.TryClampTarget(player, requested, skillEntry.Skill.Range, out target))
        {
            refusal = $"no walkable tile towards {requested}";
        }
        else if (!PlugIns.Moba.MobaBlink.Allows(player, target))
        {
            refusal = "pre-match, enemy base or no AG";
        }
        else if (!CanPlayerBeTeleported(player))
        {
            refusal = "crowd control";
        }
        else if (!await player.TryConsumeForSkillAsync(skillEntry).ConfigureAwait(false))
        {
            refusal = "not enough mana";
        }

        if (refusal is null && skillEntry?.Skill is { } skill)
        {
            PlugIns.Moba.MobaBlink.PayGauge(player);
            _ = Task.Run(() => player.TeleportAsync(target, skill));
            return;
        }

        player.Logger.LogInformation("[MOBA-BLINK] {Player} cannot teleport to {Target}: {Reason}.", player, requested, refusal);
        await PlugIns.Moba.MobaBlink.TellMissingGaugeAsync(player).ConfigureAwait(false);
        await player.InvokeViewPlugInAsync<ITeleportPlugIn>(p => p.ShowTeleportedAsync()).ConfigureAwait(false);
    }

    private static bool CanPlayerBeTeleported(Player player)
    {
        var currentEffects = player.MagicEffectList.ActiveEffects;
        if (currentEffects.Count == 0)
        {
            return true;
        }

        for (int i = 0; i < PreventingMagicEffects.Length; i++)
        {
            if (currentEffects.ContainsKey(PreventingMagicEffects[i]))
            {
                return false;
            }
        }

        return true;
    }
}