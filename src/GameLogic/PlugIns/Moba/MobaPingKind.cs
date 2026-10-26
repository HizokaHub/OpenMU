// <copyright file="MobaPingKind.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The pings a champion can mark on the map for the team (the same set as the smart ping wheel of League of Legends). The numbers are part of the
/// packets <c>C1 D5 35</c> and <c>C1 D5 33</c>: the client has the same table, so never renumber, only append.
/// </summary>
public enum MobaPingKind : byte
{
    /// <summary>A plain ping: "look here".</summary>
    Generic = 1,

    /// <summary>Danger / caution.</summary>
    Caution = 2,

    /// <summary>Retreat.</summary>
    Retreat = 3,

    /// <summary>Push the lane.</summary>
    Push = 4,

    /// <summary>On my way.</summary>
    OnMyWay = 5,

    /// <summary>All in: everybody attack.</summary>
    AllIn = 6,

    /// <summary>Assist me.</summary>
    AssistMe = 7,

    /// <summary>Need vision here (a ward).</summary>
    NeedVision = 8,

    /// <summary>An enemy went missing.</summary>
    EnemyMissing = 9,

    /// <summary>Enemy vision here (a ward of the enemy).</summary>
    EnemyVision = 10,
}
