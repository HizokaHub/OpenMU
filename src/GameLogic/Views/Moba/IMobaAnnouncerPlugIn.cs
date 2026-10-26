// <copyright file="IMobaAnnouncerPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tells the MOBA client an event of the match so it plays the announcer voice and writes the kill feed.
/// </summary>
public interface IMobaAnnouncerPlugIn : IViewPlugIn
{
    /// <summary>
    /// Sends the announcement.
    /// </summary>
    /// <param name="kind">What happened.</param>
    /// <param name="team">The team the event is about (see <see cref="MobaAnnouncement"/>); 0 when none.</param>
    /// <param name="killer">The name of the champion that did it (kills and multi-kills); empty when none.</param>
    /// <param name="victim">The name of the champion that suffered it (kills); empty when none.</param>
    /// <returns>A task.</returns>
    ValueTask AnnounceAsync(MobaAnnouncement kind, byte team, string killer, string victim);
}
