// <copyright file="IMobaPingPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tells the MOBA client a ping of a teammate: the mark the team sees on the ground and on the minimap.
/// </summary>
public interface IMobaPingPlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows the ping.
    /// </summary>
    /// <param name="kind">The kind of ping.</param>
    /// <param name="team">The team of the sender (1 blue, 2 red).</param>
    /// <param name="x">The tile X.</param>
    /// <param name="y">The tile Y.</param>
    /// <param name="senderKey">The object id of the sender, as the receiving client knows it.</param>
    /// <param name="senderName">The name of the sender.</param>
    /// <returns>A task.</returns>
    ValueTask ShowPingAsync(MobaPingKind kind, byte team, byte x, byte y, ushort senderKey, string senderName);
}
