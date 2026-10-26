// <copyright file="MobaAnnouncement.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The events the MOBA announcer tells the clients (they play a voice line and write the kill feed). The numbers are part
/// of the packet <c>C1 len D5 32</c>: the client has the same table, so never renumber, only append.
/// </summary>
public enum MobaAnnouncement : byte
{
    /// <summary>A champion killed another one (kill feed only, no voice).</summary>
    Kill = 1,

    /// <summary>The first champion kill of the match.</summary>
    FirstBlood = 2,

    /// <summary>Two kills of the same champion within the multi-kill window.</summary>
    DoubleKill = 3,

    /// <summary>Three kills within the multi-kill window.</summary>
    TripleKill = 4,

    /// <summary>Four kills within the multi-kill window.</summary>
    QuadraKill = 5,

    /// <summary>Five (or more) kills within the multi-kill window.</summary>
    PentaKill = 6,

    /// <summary>The last living champion of a team died.</summary>
    Ace = 7,

    /// <summary>A champion ended the kill streak of an enemy.</summary>
    Shutdown = 8,

    /// <summary>Three kills in a row without dying.</summary>
    KillingSpree = 9,

    /// <summary>Five kills in a row without dying.</summary>
    Dominating = 10,

    /// <summary>Seven kills in a row without dying.</summary>
    Unstoppable = 11,

    /// <summary>Nine kills in a row without dying.</summary>
    Legendary = 12,

    /// <summary>A turret fell (the team field is the team that lost it).</summary>
    TurretDestroyed = 13,

    /// <summary>A nexus is taking damage (the team field is the team that owns it).</summary>
    NexusUnderAttack = 14,

    /// <summary>The countdown ended and the match began.</summary>
    MatchStart = 15,

    /// <summary>The player's team won the match.</summary>
    Victory = 16,

    /// <summary>The player's team lost the match.</summary>
    Defeat = 17,

    /// <summary>The golden dragons start to fly over the map.</summary>
    GoldenFlyover = 18,

    /// <summary>The golden invaders arrive.</summary>
    GoldenInvaders = 19,

    /// <summary>A Great Golden Dragon was slain (the team field is the team that killed it).</summary>
    GreatDragonSlain = 20,

    /// <summary>An inhibitor fell (the team field is the team that lost it).</summary>
    InhibitorDestroyed = 21,

    /// <summary>An inhibitor is back (the team field is the team that owns it).</summary>
    InhibitorRestored = 22,

    /// <summary>The first super-minion of a lane was sent (the team field is the team that gets it).</summary>
    SuperMinions = 23,
}
