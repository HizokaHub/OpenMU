// <copyright file="MobaTeamStatusPeriodicPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Periodically pushes the team + health-percent of every MOBA-match participant
/// (creeps and champion clones) near each MOBA player to that player's client, so the
/// client can draw always-on team-coloured HP bars over creeps and refuse to target
/// allies. Custom packet C1 D5 01:
/// <code>
/// C1 len D5 01 count  [ idHi idLo team hpPercent ] * count
/// </code>
/// team: 1 = blue, 2 = red. hpPercent: 0..100. The first entry whose id matches the
/// receiving hero tells the client its own team.
/// </summary>
[PlugIn]
[Display(Name = "MOBA: team status broadcast", Description = "Pushes MOBA participant team + HP% to nearby clients for team-coloured HP bars.")]
[Guid("3F2A6C11-8D74-4E90-BB25-9A0C7E4F1D63")]
public class MobaTeamStatusPeriodicPlugIn : IPeriodicTaskPlugIn
{
    private const byte PacketCode = 0xD5;
    private const byte PacketSubCode = 0x01;

    /// <summary>Tiles around the player whose MOBA participants are reported.</summary>
    private const int BroadcastRange = 30;

    /// <summary>Cap so the C1 length byte never overflows (5 + n*4 &lt;= 255).</summary>
    private const int MaxEntries = 60;

    /// <inheritdoc />
    public void ForceStart()
    {
        // Nothing to force; the task is cheap and just runs on every periodic tick.
    }

    /// <inheritdoc />
    public async ValueTask ExecuteTaskAsync(GameContext gameContext)
    {
        var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        foreach (var player in players)
        {
            if (MobaTeams.GetTeam(player) == MobaTeam.None
                || player is not RemotePlayer { Connection: { Connected: true } connection } remotePlayer
                || player.CurrentMap is not { } map)
            {
                continue;
            }

            var entries = map.GetAttackablesInRange(player.Position, BroadcastRange)
                .Where(a => a.IsAlive && MobaTeams.GetTeam(a) != MobaTeam.None)
                .Take(MaxEntries)
                .Select(a => ((ushort Id, byte Team, byte Hp))(a.GetId(player), (byte)MobaTeams.GetTeam(a), HealthPercent(a)))
                .ToList();

            if (entries.Count == 0)
            {
                continue;
            }

            var length = 5 + (entries.Count * 4);

            int Write()
            {
                var span = connection.Output.GetSpan(length)[..length];
                span[0] = 0xC1;
                span[1] = (byte)length;
                span[2] = PacketCode;
                span[3] = PacketSubCode;
                span[4] = (byte)entries.Count;
                var offset = 5;
                foreach (var (id, team, hp) in entries)
                {
                    span[offset] = (byte)(id >> 8);
                    span[offset + 1] = (byte)id;
                    span[offset + 2] = team;
                    span[offset + 3] = hp;
                    offset += 4;
                }

                return length;
            }

            await connection.SendAsync(Write).ConfigureAwait(false);
        }
    }

    private static byte HealthPercent(IAttackable attackable)
    {
        double current;
        double maximum;
        if (attackable is AttackableNpcBase npc)
        {
            current = npc.Health;
            maximum = npc.Attributes[Stats.MaximumHealth];
        }
        else
        {
            current = attackable.Attributes[Stats.CurrentHealth];
            maximum = attackable.Attributes[Stats.MaximumHealth];
        }

        if (maximum <= 0)
        {
            return 0;
        }

        var ratio = Math.Clamp(current / maximum, 0d, 1d);
        return (byte)Math.Max(current > 0 ? 1 : 0, Math.Round(ratio * 100));
    }
}
