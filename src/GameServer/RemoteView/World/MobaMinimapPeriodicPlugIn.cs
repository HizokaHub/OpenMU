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
/// range of the client doesn't cover it). The lane minions go in their own packet (<c>C2 D5 25</c>, small dots). Custom packet C1 D5 10:
/// <code>
/// C1 len D5 10 count  [ kind team x y hp% ] * count
/// </code>
/// kind is packed (<see cref="MobaMinimapKind"/>): bits 0-3 = 1 champion, 2 turret, 3 nexus, 4 ward;
/// bits 4-6 = base class index 0..6 (champions only: DW, DK, Elf, MG, DL, Summoner, RF);
/// bit 7 = the champion is channelling the recall. Turret/nexus/ward entries keep bits 4-7 at 0.
/// team: 1 blue, 2 red. Allied champions (dead ones too, with hp 0 at the spot where they died),
/// structures of both teams (static knowledge) and allied wards are always sent; enemy champions only
/// while alive and seen by the player's team (<see cref="MobaVision.IsVisibleTo"/>). The receiving
/// player is left out. At most <see cref="MaxEntries"/> entries; champions come first so a trim only
/// ever drops wards and structures.
/// </summary>
[PlugIn]
[Display(Name = "MOBA: minimap broadcast", Description = "Pushes allied champions, structures, wards and the enemies the team sees to the MOBA minimap.")]
[Guid("6B1E0D52-4C7A-4F83-9D21-A5E3B8C07F14")]
public class MobaMinimapPeriodicPlugIn : IPeriodicTaskPlugIn
{
    private const byte PacketCode = 0xD5;
    private const byte PacketSubCode = 0x10;
    private const int EntrySize = 5;
    private const byte CreepsSubCode = 0x25;
    private const int CreepHeaderLength = 7;
    private const int CreepEntrySize = 3;
    private const int MaxCreepDots = 400;

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

            // Champions first: the trim below cuts from the end, so they are never dropped in favour of wards.
            var entries = new List<(byte Kind, byte Team, byte X, byte Y, byte Hp)>();
            foreach (var champion in players)
            {
                var championTeam = MobaTeams.GetTeam(champion);
                if (champion == player || !champion.IsMobaClone || championTeam == MobaTeam.None
                    || !ReferenceEquals(champion.CurrentMap, map)
                    || (championTeam != team && (!champion.IsAlive || !MobaVision.IsVisibleTo(team, champion))))
                {
                    continue;
                }

                // A dead ally stays where it died (until it respawns) with 0 hp, so the client can mark it.
                var hp = champion.IsAlive ? HealthPercent(champion) : (byte)0;
                entries.Add((MobaMinimapKind.PackChampion(champion), (byte)championTeam, (byte)champion.Position.X, (byte)champion.Position.Y, hp));
            }

            foreach (var attackable in map.GetAttackablesInRange(player.Position, 255))
            {
                if (!attackable.IsAlive || attackable is not AttackableNpcBase npc)
                {
                    continue;
                }

                var kind = MobaStructures.GetStructureType(attackable) switch
                {
                    MobaStructureType.Turret => MobaMinimapKind.Turret,
                    MobaStructureType.Nexus => MobaMinimapKind.Nexus,
                    _ => (byte)0,
                };

                if (kind != 0)
                {
                    entries.Add((kind, (byte)MobaTeams.GetTeam(attackable), (byte)npc.Position.X, (byte)npc.Position.Y, HealthPercent(attackable)));
                }
            }

            foreach (var ward in MobaVision.WardsOf(team, map))
            {
                entries.Add((MobaMinimapKind.Ward, (byte)team, (byte)ward.X, (byte)ward.Y, 100));
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
            await SendCreepsAsync(connection, team, map).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The lane minions as small dots: <c>C2 hi lo D5 25 count(u16 LE) [ x y team ] * count</c>. The team's own minions are always
    /// sent; the enemy ones only while the team sees them. Jungle monsters and structures are not minions.
    /// </summary>
    private static async ValueTask SendCreepsAsync(IConnection connection, MobaTeam team, GameMap map)
    {
        var dots = new List<(byte X, byte Y, byte Team)>();
        foreach (var attackable in map.GetAttackablesInRange(new MUnique.OpenMU.Pathfinding.Point(128, 128), 255))
        {
            if (!attackable.IsAlive || attackable is not Monster creep || MobaStructures.IsStructure(creep))
            {
                continue;
            }

            var creepTeam = MobaTeams.GetTeam(creep);
            if (creepTeam == MobaTeam.None
                || (creepTeam != team && !MobaVision.IsPositionVisibleTo(team, map, creep.Position)))
            {
                continue;
            }

            dots.Add((creep.Position.X, creep.Position.Y, (byte)creepTeam));
            if (dots.Count >= MaxCreepDots)
            {
                break;
            }
        }

        var length = CreepHeaderLength + (dots.Count * CreepEntrySize);

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC2;
            span[1] = (byte)(length >> 8);
            span[2] = (byte)(length & 0xFF);
            span[3] = PacketCode;
            span[4] = CreepsSubCode;
            span[5] = (byte)(dots.Count & 0xFF);
            span[6] = (byte)(dots.Count >> 8);
            var offset = CreepHeaderLength;
            foreach (var (x, y, dotTeam) in dots)
            {
                span[offset] = x;
                span[offset + 1] = y;
                span[offset + 2] = dotTeam;
                offset += CreepEntrySize;
            }

            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
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
