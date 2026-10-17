// <copyright file="IMobaMonsterSkillPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// Tells the client that a monster cast one of its own monster skills (client packet 0x69, <c>ReceiveMonsterSkill</c>): the animation and effects of
/// the skill depend on the monster model (for example the nine skills of Selupan, 34-42).
/// </summary>
public interface IMobaMonsterSkillPlugIn : IViewPlugIn
{
    /// <summary>Shows the monster skill.</summary>
    /// <param name="attacker">The monster.</param>
    /// <param name="target">The target (the client turns the monster towards it).</param>
    /// <param name="skillNumber">The monster skill number of the client (<c>ATMON_SKILL_*</c>).</param>
    /// <returns>A task.</returns>
    ValueTask ShowMonsterSkillAsync(IAttacker attacker, IAttackable target, int skillNumber);
}
