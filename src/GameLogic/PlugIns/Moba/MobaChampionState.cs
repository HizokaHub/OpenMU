// <copyright file="MobaChampionState.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// The per-match progress of one champion: what has to survive a disconnect and a reconnect
/// (level, EXP, points, K/D/A, team, cooldowns). The <see cref="Player"/> properties of the same
/// names read and write the state object the player currently holds, so the human session, the
/// ghost that stands in for it while it is offline, and the session after a reconnect all share
/// one object. Runtime only, never persisted.
/// </summary>
public sealed class MobaChampionState
{
    /// <summary>Gets or sets the champion level (1..30).</summary>
    public int Level { get; set; } = 1;

    /// <summary>Gets or sets the EXP toward the next champion level.</summary>
    public long Experience { get; set; }

    /// <summary>Gets or sets the unspent champion skill points.</summary>
    public int SkillPoints { get; set; }

    /// <summary>Gets or sets the kills of this match.</summary>
    public int Kills { get; set; }

    /// <summary>Gets or sets the deaths of this match.</summary>
    public int Deaths { get; set; }

    /// <summary>Gets or sets the assists of this match.</summary>
    public int Assists { get; set; }

    /// <summary>Gets or sets the champion's team.</summary>
    public MobaTeam Team { get; set; } = MobaTeam.None;

    /// <summary>
    /// Gets or sets the map instance this champion's match is played on, or <see langword="null"/> for the arena
    /// the game server hosts for everybody (the <c>/moba</c> test shortcut).
    /// </summary>
    public GameMap? Arena { get; set; }

    /// <summary>Gets or sets a value indicating whether the champion was already placed at its base when the match was entered.</summary>
    public bool Placed { get; set; }

    /// <summary>Gets or sets the respawn wait of the champion's latest death (for the notice to the player).</summary>
    public TimeSpan LastRespawnDelay { get; set; }

    /// <summary>Gets or sets the UTC instant of the champion's last sign of life (see <see cref="MobaAfk"/>).</summary>
    public DateTime LastInputUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets how far the anti-AFK escalation of this champion has gone.</summary>
    public MobaAfkStage AfkStage { get; set; }

    /// <summary>Gets the skill cooldowns: skill number mapped to the UTC instant it is usable again.</summary>
    public Dictionary<short, DateTime> SkillCooldowns { get; } = new();

    /// <summary>Gets the cast-grace window ends: skill number mapped to the UTC instant the window closes.</summary>
    public Dictionary<short, DateTime> SkillGraceEnds { get; } = new();

    /// <summary>
    /// Gets or sets the ghost which stands in the arena for this champion while its human is
    /// disconnected, or <see langword="null"/> while the human is connected.
    /// </summary>
    public Player? Ghost { get; set; }
}

/// <summary>
/// A snapshot of a champion's body: current health, mana, shield and AG, and whether it is alive.
/// Used to hand the body from a disconnecting session to its ghost and back, so a reconnect
/// neither heals nor kills.
/// </summary>
/// <param name="Health">The current health.</param>
/// <param name="Mana">The current mana.</param>
/// <param name="Shield">The current shield.</param>
/// <param name="Ability">The current ability gauge.</param>
public readonly record struct MobaVitals(float Health, float Mana, float Shield, float Ability)
{
    /// <summary>
    /// Captures the vitals of a player, or <see langword="null"/> if it has no attributes.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The vitals.</returns>
    public static MobaVitals? Capture(Player player)
    {
        if (player.Attributes is not { } a)
        {
            return null;
        }

        return new MobaVitals(a[Stats.CurrentHealth], a[Stats.CurrentMana], a[Stats.CurrentShield], a[Stats.CurrentAbility]);
    }

    /// <summary>
    /// Writes the vitals onto a player, never above its maximum.
    /// </summary>
    /// <param name="player">The player.</param>
    public void ApplyTo(Player player)
    {
        if (player.Attributes is not { } a)
        {
            return;
        }

        a[Stats.CurrentHealth] = Math.Clamp(this.Health, 1f, Math.Max(1f, a[Stats.MaximumHealth]));
        a[Stats.CurrentMana] = Math.Clamp(this.Mana, 0f, a[Stats.MaximumMana]);
        a[Stats.CurrentShield] = Math.Clamp(this.Shield, 0f, a[Stats.MaximumShield]);
        a[Stats.CurrentAbility] = Math.Clamp(this.Ability, 0f, a[Stats.MaximumAbility]);
    }
}
