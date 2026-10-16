// <copyright file="MobaFountainPeriodicPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Tells every MOBA player who owns each fountain and how far each team got. Custom packet C1 D5 11:
/// <code>
/// C1 len D5 11 count  [ owner blue% red% flags ] * count
/// </code>
/// owner: 0 neutral, 1 blue, 2 red. blue% / red%: capture progress 0-100. flags: bit 0 = contested.
/// The positions are fixed and known by the client: the two fountains first, then the four jungle totems.
/// </summary>
[PlugIn]
[Display(Name = "MOBA: fountain state broadcast", Description = "Pushes the owner and capture progress of the two MOBA fountains to every player in the match.")]
[Guid("8D3A5E61-2C94-4B7F-A1D8-5F0E6B9C3A27")]
public class MobaFountainPeriodicPlugIn : IPeriodicTaskPlugIn
{
    private const byte PacketCode = 0xD5;
    private const byte PacketSubCode = 0x11;
    private const int EntrySize = 4;

    /// <inheritdoc />
    public void ForceStart()
    {
        // Nothing to force; the task is cheap and just runs on every periodic tick.
    }

    /// <inheritdoc />
    public async ValueTask ExecuteTaskAsync(GameContext gameContext)
    {
        var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        var count = MobaFountains.PointCount;
        var length = 5 + (count * EntrySize);

        foreach (var player in players)
        {
            if (MobaTeams.GetTeam(player) == MobaTeam.None || player is not RemotePlayer { Connection: { Connected: true } connection })
            {
                continue;
            }

            int Write()
            {
                var span = connection.Output.GetSpan(length)[..length];
                span[0] = 0xC1;
                span[1] = (byte)length;
                span[2] = PacketCode;
                span[3] = PacketSubCode;
                span[4] = (byte)count;
                for (var i = 0; i < count; i++)
                {
                    var state = MobaFountains.StateOf(i);
                    var offset = 5 + (i * EntrySize);
                    span[offset] = (byte)state.Owner;
                    span[offset + 1] = Percent(state.BlueProgress, state.Required);
                    span[offset + 2] = Percent(state.RedProgress, state.Required);
                    span[offset + 3] = (byte)(state.Contested ? 1 : 0);
                }

                return length;
            }

            await connection.SendAsync(Write).ConfigureAwait(false);
        }
    }

    private static byte Percent(double seconds, double required)
        => (byte)Math.Clamp((int)Math.Round(seconds / required * 100), 0, 100);
}
