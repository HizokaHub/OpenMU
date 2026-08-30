// <copyright file="MobaTeams.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;

/// <summary>
/// Process-wide, RAM-only mapping of match participants (clone players, creeps,
/// turrets, the nexus) to their <see cref="MobaTeam"/>.
/// </summary>
/// <remarks>
/// Backed by a <see cref="ConditionalWeakTable{TKey,TValue}"/> so entries vanish
/// when the participant is collected (dead creeps, ended matches) - no manual
/// cleanup needed. A dedicated match context owning this comes later.
/// </remarks>
public static class MobaTeams
{
    private static readonly ConditionalWeakTable<object, object> TeamByParticipant = new();

    /// <summary>
    /// Assigns a participant to a team.
    /// </summary>
    /// <param name="participant">The participant (a <see cref="Player"/> clone, a creep <see cref="NPC.Monster"/>, etc.).</param>
    /// <param name="team">The team.</param>
    public static void Set(object participant, MobaTeam team) => TeamByParticipant.AddOrUpdate(participant, team);

    /// <summary>
    /// Clears a participant's team assignment.
    /// </summary>
    /// <param name="participant">The participant.</param>
    public static void Clear(object participant) => TeamByParticipant.Remove(participant);

    /// <summary>
    /// Gets the team of a participant, or <see cref="MobaTeam.None"/> if it has none.
    /// </summary>
    /// <param name="participant">The participant.</param>
    /// <returns>The team.</returns>
    public static MobaTeam GetTeam(object? participant)
    {
        if (participant is not null && TeamByParticipant.TryGetValue(participant, out var boxed) && boxed is MobaTeam team)
        {
            return team;
        }

        return MobaTeam.None;
    }

    /// <summary>
    /// Determines whether two participants are on opposing MOBA teams (both must have a team).
    /// </summary>
    /// <param name="a">The first participant.</param>
    /// <param name="b">The second participant.</param>
    /// <returns><see langword="true"/> if both have a team and the teams differ.</returns>
    public static bool AreEnemies(object? a, object? b)
    {
        var teamA = GetTeam(a);
        var teamB = GetTeam(b);
        return teamA != MobaTeam.None && teamB != MobaTeam.None && teamA != teamB;
    }

    /// <summary>
    /// Determines whether two participants are on the same MOBA team (both must have a team).
    /// </summary>
    /// <param name="a">The first participant.</param>
    /// <param name="b">The second participant.</param>
    /// <returns><see langword="true"/> if both have the same team.</returns>
    public static bool AreAllies(object? a, object? b)
    {
        var teamA = GetTeam(a);
        return teamA != MobaTeam.None && teamA == GetTeam(b);
    }
}
