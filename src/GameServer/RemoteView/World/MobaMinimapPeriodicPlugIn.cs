// <copyright file="MobaMinimapPeriodicPlugIn.cs" company="MUnique">
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
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Pushes what the whole team knows to each MOBA player's minimap, wherever it is on the map (the scene
/// range of the client doesn't cover it). Creeps are deliberately not sent. Custom packet C1 D5 10:
/// <code>
/// C1 len D5 10 count  [ kind team x y hp% ] * count
/// </code>
/// kind: 1 champion, 2 turret, 3 nexus, 4 ward. team: 1 blue, 2 red. Allied champions, structures
/// of both teams (static knowledge) and allied wards are always sent; enemy champions only while the
/// player's team sees them (<see cref="MobaVision.IsVisibleTo"/>). The receiving player is left out.
/// </summary>
[PlugIn]
[Display(Name = "MOBA: minimap broadcast", Description = "Pushes allied champions, structures, wards and the enemies the team sees to the MOBA minimap.")]
[Guid("6B1E0D52-4C7A-4F83-9D21-A5E3B8C07F14")]
public class MobaMinimapPeriodicPlugIn : IPeriodicTaskPlugIn
{
    private const byte PacketCode = 0xD5;
    private const byte PacketSubCode = 0x10;
    private const int EntrySize = 5;

    /// <summary>Cap so the C1 length byte never overflows (5 + n*5 &lt;= 255).</summary>
    private const int MaxEntries = 48;

    /// <inheritdoc />
    public void ForceStart()
    {
        // Nothing to force; the task is cheap and just runs on every periodic tick.
    }

    /// <inheritdoc />
    public async ValueTask ExecuteTaskAsync(GameContext gameContext)
    {
        var players = (await gameContext.GetPlayersAsync().ConfigureAwait(false)).ToList();
        foreach (var player in players)
        {
            var team = MobaTeams.GetTeam(player);
            if (team == MobaTeam.None
                || player is not RemotePlayer { Connection: { Connected: true } connection }
                || player.CurrentMap is not { } map)
            {
                continue;
            }

            var entries = new List<(byte Kind, byte Team, byte X, byte Y, byte Hp)>();
            foreach (var champion in players)
            {
                var championTeam = MobaTeams.GetTeam(champion);
                if (champion == player || !champion.IsMobaClone || !champion.IsAlive || championTeam == MobaTeam.None
                    || !ReferenceEquals(champion.CurrentMap, map)
                    || (championTeam != team && !MobaVision.IsVisibleTo(team, champion)))
                {
                    continue;
                }

                entries.Add((1, (byte)championTeam, (byte)champion.Position.X, (byte)champion.Position.Y, HealthPercent(champion)));
            }

            foreach (var attackable in map.GetAttackablesInRange(player.Position, 255))
            {
                if (!attackable.IsAlive || attackable is not AttackableNpcBase npc)
                {
                    continue;
                }

                var kind = MobaStructures.GetStructureType(attackable) switch
                {
                    MobaStructureType.Turret => (byte)2,
                    MobaStructureType.Nexus => (byte)3,
                    _ => (byte)0,
                };

                if (kind != 0)
                {
                    entries.Add((kind, (byte)MobaTeams.GetTeam(attackable), (byte)npc.Position.X, (byte)npc.Position.Y, HealthPercent(attackable)));
                }
            }

            foreach (var ward in MobaVision.WardsOf(team, map))
            {
                entries.Add((4, (byte)team, (byte)ward.X, (byte)ward.Y, 100));
            }

            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            }

            var length = 5 + (entries.Count * EntrySize);

            int Write()
            {
                var span = connection.Output.GetSpan(length)[..length];
                span[0] = 0xC1;
                span[1] = (byte)length;
                span[2] = PacketCode;
                span[3] = PacketSubCode;
                span[4] = (byte)entries.Count;
                var offset = 5;
                foreach (var (kind, entryTeam, x, y, hp) in entries)
                {
                    span[offset] = kind;
                    span[offset + 1] = entryTeam;
                    span[offset + 2] = x;
                    span[offset + 3] = y;
                    span[offset + 4] = hp;
                    offset += EntrySize;
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
