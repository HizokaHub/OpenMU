// <copyright file="MobaScoreboardPeriodicPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Periodically pushes the full MOBA scoreboard - every champion in the match with its
/// team, class, champion level and K/D/A - to every MOBA player, so the client can draw
/// a League-style TAB panel. Custom packet C1 D5 05:
/// <code>
/// C1 len D5 05 count  [ name(10, null-padded)  team(1)  classNumber(1)  level(1)  kills(1)  deaths(1)  assists(1) ] * count
/// </code>
/// team: 1 = blue, 2 = red.
/// </summary>
[PlugIn]
[Display(Name = "MOBA: scoreboard broadcast", Description = "Pushes the full champion scoreboard (level + K/D/A) to MOBA players for the TAB panel.")]
[Guid("7A1E4C82-9B36-4D07-8F52-2C0B9E7F4A16")]
public class MobaScoreboardPeriodicPlugIn : IPeriodicTaskPlugIn
{
    private const byte PacketCode = 0xD5;
    private const byte PacketSubCode = 0x05;
    private const int NameBytes = 10;
    private const int RowBytes = NameBytes + 6;
    private const int MaxRows = 12;

    /// <inheritdoc />
    public void ForceStart()
    {
        // Nothing to force; cheap, runs on the periodic tick.
    }

    /// <inheritdoc />
    public async ValueTask ExecuteTaskAsync(GameContext gameContext)
    {
        var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);

        var champions = players
            .Where(p => p.IsMobaClone && MobaTeams.GetTeam(p) != MobaTeam.None)
            .OrderBy(p => (int)MobaTeams.GetTeam(p))
            .ThenByDescending(p => p.MobaLevel)
            .Take(MaxRows)
            .ToList();

        if (champions.Count == 0)
        {
            return;
        }

        var rows = champions.Select(c => new
        {
            Name = c.SelectedCharacter?.Name ?? "?",
            Team = (byte)MobaTeams.GetTeam(c),
            ClassNumber = (byte)(c.SelectedCharacter?.CharacterClass?.Number ?? 0),
            Level = (byte)Math.Clamp(c.MobaLevel, 0, 255),
            Kills = (byte)Math.Clamp(c.MobaKills, 0, 255),
            Deaths = (byte)Math.Clamp(c.MobaDeaths, 0, 255),
            Assists = (byte)Math.Clamp(c.MobaAssists, 0, 255),
        }).ToList();

        var length = 5 + (rows.Count * RowBytes);

        foreach (var player in champions)
        {
            if (player is not RemotePlayer { Connection: { Connected: true } connection })
            {
                continue;
            }

            int Write()
            {
                var span = connection.Output.GetSpan(length)[..length];
                span.Clear();
                span[0] = 0xC1;
                span[1] = (byte)length;
                span[2] = PacketCode;
                span[3] = PacketSubCode;
                span[4] = (byte)rows.Count;
                var offset = 5;
                foreach (var row in rows)
                {
                    var nameBytes = Encoding.ASCII.GetBytes(row.Name);
                    var n = Math.Min(nameBytes.Length, NameBytes);
                    nameBytes.AsSpan(0, n).CopyTo(span.Slice(offset, n));
                    span[offset + NameBytes] = row.Team;
                    span[offset + NameBytes + 1] = row.ClassNumber;
                    span[offset + NameBytes + 2] = row.Level;
                    span[offset + NameBytes + 3] = row.Kills;
                    span[offset + NameBytes + 4] = row.Deaths;
                    span[offset + NameBytes + 5] = row.Assists;
                    offset += RowBytes;
                }

                return length;
            }

            await connection.SendAsync(Write).ConfigureAwait(false);
        }
    }
}
