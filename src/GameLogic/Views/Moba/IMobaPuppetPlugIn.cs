// <copyright file="IMobaPuppetPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>A teammate whose player left the match, which another teammate may take control of.</summary>
/// <param name="Key">The object id of the champion for the viewer.</param>
/// <param name="BaseClass">The base class index of the champion (the portrait).</param>
/// <param name="HpPercent">The life in percent.</param>
/// <param name="MpPercent">The mana in percent.</param>
/// <param name="Selected">Whether the viewer is controlling this champion.</param>
/// <param name="Name">The name of the champion.</param>
public sealed record MobaPuppetCandidate(ushort Key, byte BaseClass, byte HpPercent, byte MpPercent, bool Selected, string Name);

/// <summary>A skill of the champion the viewer controls, with its cooldown.</summary>
/// <param name="Number">The skill number.</param>
/// <param name="Rank">The rank of the skill (0 = inactive).</param>
/// <param name="IsBuff">Whether it is one of the class buffs (they are ranked with the buff points).</param>
/// <param name="CooldownLeftMs">The cooldown left in milliseconds.</param>
/// <param name="CooldownFullMs">The full cooldown in milliseconds.</param>
public sealed record MobaPuppetSkill(short Number, byte Rank, bool IsBuff, int CooldownLeftMs, int CooldownFullMs);

/// <summary>What a teammate sees of the champions it may control, or controls.</summary>
/// <param name="Controlling">Whether the viewer controls one of the candidates right now.</param>
/// <param name="X">The tile X of the controlled champion.</param>
/// <param name="Y">The tile Y of the controlled champion.</param>
/// <param name="Candidates">The champions without player of the team (an empty list closes the panel).</param>
/// <param name="Skills">The skills of the controlled champion (empty when none is controlled).</param>
public sealed record MobaPuppetInfo(bool Controlling, byte X, byte Y, IReadOnlyList<MobaPuppetCandidate> Candidates, IReadOnlyList<MobaPuppetSkill> Skills);

/// <summary>
/// Tells the client which disconnected teammates it can control (<see cref="MobaPuppetInfo"/>) and, for the controlled one, its skills
/// with their cooldowns, so the HUD can draw its skill bar.
/// </summary>
public interface IMobaPuppetPlugIn : IViewPlugIn
{
    /// <summary>
    /// Pushes the state of the control mode.
    /// </summary>
    /// <param name="info">The state.</param>
    ValueTask ShowPuppetAsync(MobaPuppetInfo info);
}
