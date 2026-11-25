// <copyright file="RecoverConsumeHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.ItemConsumeActions;

using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Consume handler which can recover attributes.
/// </summary>
public abstract class RecoverConsumeHandlerPlugIn : BaseConsumeHandlerPlugIn, ISupportCustomConfiguration<RecoverConsumeHandlerConfiguration>, ISupportDefaultCustomConfiguration
{
    /// <summary>
    /// Gets or sets the configuration.
    /// </summary>
    public RecoverConsumeHandlerConfiguration? Configuration { get; set; }

    /// <summary>
    /// Gets the attribute which contains the value which should get recovered.
    /// </summary>
    protected abstract AttributeDefinition CurrentAttribute { get; }

    /// <summary>
    /// Gets the attribute which contains the maximum value of the <see cref="CurrentAttribute"/>.
    /// </summary>
    protected abstract AttributeDefinition MaximumAttribute { get; }

    /// <inheritdoc/>
    public override async ValueTask<bool> ConsumeItemAsync(Player player, Item item, Item? targetItem, FruitUsage fruitUsage)
    {
        if (await base.ConsumeItemAsync(player, item, targetItem, fruitUsage).ConfigureAwait(false))
        {
            await this.RecoverAsync(player, item).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    /// <summary>The time a MOBA champion waits between two potions (health or mana share it).</summary>
    public static readonly TimeSpan MobaPotionCooldown = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the fraction of the native health potion recovery that MOBA champions get (tuned with <c>/mobaf10 pocion</c>).</summary>
    /// <remarks>
    /// 0.14 chosen on 2026-11-25 (the user's rule: one potion per second, adding about 2 s to a duel). Measured with the duels of levels 10, 20 and 30: mean duration
    /// +1.2 / +2.1 / +3.7 s (median +1.2 / +2.6 / +1.2 s) against no potions. With it a medium potion +7 gives about 3.8 % of the maximum life and a large one 5.3-5.5 %.
    /// </remarks>
    public static double MobaHealthPotionScale { get; set; } = 0.14;

    /// <inheritdoc />
    public abstract object CreateDefaultConfig();

    /// <summary>
    /// Recovers the attributes of the specified player.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="item">The item.</param>
    internal async ValueTask RecoverAsync(Player player, Item item)
    {
        if (player.Attributes is null)
        {
            return;
        }

        var configuration = this.Configuration ??= (RecoverConsumeHandlerConfiguration)this.CreateDefaultConfig();

        var recoverPercentage = configuration.TotalRecoverPercentage + (item.Level * configuration.RecoverPercentageIncreaseByPotionLevel);
        var additionalRecover = Math.Max(0, configuration.AdditionalRecoverMinusCharacterLevel - player.Attributes[Stats.Level]);
        var totalRecoverAmount = (player.Attributes[this.MaximumAttribute] * recoverPercentage / 100.0) + additionalRecover;

        // MOBA: potions heal a small share of the native amount (see MobaHealthPotionScale): at one per second the native ~28 % made champions practically unkillable.
        if (player.IsMobaClone && this.CurrentAttribute == Stats.CurrentHealth)
        {
            totalRecoverAmount *= MobaHealthPotionScale;
        }

        var delayReduction = configuration.RecoverDelayReductionByPotionLevel * item.Level;
        if (configuration.RecoverSteps.Count == 0 || delayReduction >= 1)
        {
            player.Attributes[this.CurrentAttribute] = (uint)Math.Min(player.Attributes[this.MaximumAttribute], player.Attributes[this.CurrentAttribute] + totalRecoverAmount);
            await this.OnAfterRecoverAsync(player).ConfigureAwait(false);
        }
        else
        {
            _ = this.RecoverByStepsAsync(player, delayReduction, configuration, totalRecoverAmount);
        }

        player.PotionCooldownUntil = DateTime.UtcNow.Add(player.IsMobaClone ? MobaPotionCooldown : this.Configuration.CooldownTime);
    }

    /// <inheritdoc />
    protected override bool CheckPreconditions(Player player, Item item)
    {
        return base.CheckPreconditions(player, item)
               && player.PotionCooldownUntil <= DateTime.UtcNow;
    }

    /// <summary>
    /// Called after the attribute was recovered. Can be handled to update
    /// the view.
    /// </summary>
    /// <param name="player">The player.</param>
    protected virtual ValueTask OnAfterRecoverAsync(Player player)
    {
        return default;
    }

    private async Task RecoverByStepsAsync(Player player, double delayReduction, RecoverConsumeHandlerConfiguration configuration, double totalRecoverAmount)
    {
        foreach (var step in configuration.RecoverSteps)
        {
            if (!player.IsAlive || player.Attributes is not { } playerAttributes)
            {
                break;
            }

            var delay = TimeSpan.FromMilliseconds(step.Delay.TotalMilliseconds * (1.0 - delayReduction));
            if (delay.TotalMilliseconds > 0)
            {
                await Task.Delay(delay).ConfigureAwait(false);
            }

            var recoverAmount = totalRecoverAmount * (step.RecoverPercentage / 100.0);
            if (this.CurrentAttribute == Stats.CurrentHealth)
            {
                recoverAmount = PlugIns.Moba.MobaItemTraits.ScaleHealing(player, recoverAmount);
            }

            playerAttributes[this.CurrentAttribute] = (uint)Math.Min(playerAttributes[this.MaximumAttribute], playerAttributes[this.CurrentAttribute] + recoverAmount);
            await this.OnAfterRecoverAsync(player).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The base class for health and mana /.
    /// </summary>
    public abstract class ManaHealthConsumeHandlerPlugIn : RecoverConsumeHandlerPlugIn
    {
        /// <summary>
        /// Gets the multiplier of 50 for the additional recover.
        /// </summary>
        protected abstract int Multiplier { get; }

        /// <inheritdoc />
        public override object CreateDefaultConfig()
        {
            return new RecoverConsumeHandlerConfiguration
            {
                TotalRecoverPercentage = this.Multiplier * 10,
                AdditionalRecoverMinusCharacterLevel = (this.Multiplier + 1) * 50,
                RecoverDelayReductionByPotionLevel = 1.0 / 16.0,
                RecoverPercentageIncreaseByPotionLevel = 1,
                CooldownTime = TimeSpan.FromSeconds(0.5),
                RecoverSteps =
                {
                    new RecoverStep
                    {
                        Delay = TimeSpan.FromMilliseconds(200),
                        RecoverPercentage = 20,
                    },
                    new RecoverStep
                    {
                        Delay = TimeSpan.FromMilliseconds(600),
                        RecoverPercentage = 60,
                    },
                    new RecoverStep
                    {
                        Delay = TimeSpan.FromMilliseconds(200),
                        RecoverPercentage = 20,
                    },
                },
            };
        }
    }
}