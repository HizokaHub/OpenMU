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
using MUnique.OpenMU.GameLogic.Views;
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
    private const byte GoldenDotsSubCode = 0x31;
    private const int MaxGoldenDots = 400;

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
            await SendSummonAsync(connection, player).ConfigureAwait(false);
            await SendSummonSizesAsync(connection, players, map).ConfigureAwait(false);
            await SendExtraPetsAsync(connection, player, players, map).ConfigureAwait(false);
            await SendGoldenAsync(connection, map).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The creature of the Elf for her HUD and minimap: <c>C1 0C D5 29 present mode(0 follows, 1 commanded, 2 holds its post) keyLo keyHi hp% x y rank</c>
    /// (present 0 = she has none). Sent to every champion; the ones without a summon get the short "none" once in a while,
    /// which also clears a stale icon.
    /// </summary>
    private static async ValueTask SendSummonAsync(IConnection connection, Player player)
    {
        const int Length = 14;
        var summon = MobaSummons.Of(player);
        if (summon is null && !player.MobaSummonControl && MobaLoadouts.SummonsFor(player.SelectedCharacter?.CharacterClass?.Number ?? 0).Count == 0)
        {
            return;
        }

        int Write()
        {
            var span = connection.Output.GetSpan(Length)[..Length];
            span[0] = 0xC1;
            span[1] = Length;
            span[2] = PacketCode;
            span[3] = 0x29;
            span[4] = summon is null ? (byte)0 : (byte)1;
            span[5] = player.MobaSummonControl ? (byte)1 : (player.MobaSummonStay ? (byte)2 : (byte)0);
            span[6] = summon is { } s1 ? (byte)(s1.Monster.Id & 0xFF) : (byte)0;
            span[7] = summon is { } s2 ? (byte)(s2.Monster.Id >> 8) : (byte)0;
            span[8] = summon is { } s3 ? HealthPercent(s3.Monster) : (byte)0;
            span[9] = summon is { } s4 ? (byte)s4.Monster.Position.X : (byte)0;
            span[10] = summon is { } s5 ? (byte)s5.Monster.Position.Y : (byte)0;
            span[11] = (byte)MobaSummons.RankOf(player);
            span[12] = (byte)Math.Clamp(player.MobaSummonLevel, 1, MobaSummons.MaxSummonLevel);
            span[13] = (byte)MobaSummons.ProgressPercent(player);
            return Length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }

    /// <summary>
    /// The level of every living creature of the match, so each client can draw it bigger the higher its level is (also for the ones
    /// of the other champions): <c>C1 len D5 2B count [ keyLo keyHi level ] * count</c>.
    /// </summary>
    private static async ValueTask SendSummonSizesAsync(IConnection connection, IEnumerable<Player> champions, GameMap map)
    {
        var entries = new List<(ushort Key, byte Level)>();
        foreach (var champion in champions)
        {
            if (champion.IsMobaClone && ReferenceEquals(champion.CurrentMap, map) && MobaSummons.Of(champion) is { } summon)
            {
                entries.Add(((ushort)summon.Monster.Id, (byte)Math.Clamp(champion.MobaSummonLevel, 1, MobaSummons.MaxSummonLevel)));
            }
        }

        var length = 5 + (entries.Count * 3);
        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = (byte)length;
            span[2] = PacketCode;
            span[3] = 0x2B;
            span[4] = (byte)entries.Count;
            var offset = 5;
            foreach (var (key, level) in entries)
            {
                span[offset] = (byte)(key & 0xFF);
                span[offset + 1] = (byte)(key >> 8);
                span[offset + 2] = level;
                offset += 3;
            }

            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }

    /// <summary>
    /// The golden invasion for the clients: <c>C1 len D5 2E count [ keyLo keyHi percent ] * count</c> (the size of the growing dragons, in percent of a grown one) and
    /// <c>C1 len D5 2F count [ team x y radius burning ] * count</c> (the cities: the circle around each nexus, burning while golden monsters are in it).
    /// Sent only while the invasion lasts.
    /// </summary>
    private static async ValueTask SendGoldenAsync(IConnection connection, GameMap map)
    {
        if (!MobaGolden.HasStarted(map))
        {
            return;
        }

        var sizes = MobaGolden.GrowthSizes(map);
        var sizesLength = 5 + (sizes.Count * 3);
        int WriteSizes()
        {
            var span = connection.Output.GetSpan(sizesLength)[..sizesLength];
            span[0] = 0xC1;
            span[1] = (byte)sizesLength;
            span[2] = PacketCode;
            span[3] = 0x2E;
            span[4] = (byte)sizes.Count;
            var offset = 5;
            foreach (var (key, percent) in sizes)
            {
                span[offset] = (byte)(key & 0xFF);
                span[offset + 1] = (byte)(key >> 8);
                span[offset + 2] = percent;
                offset += 3;
            }

            return sizesLength;
        }

        await connection.SendAsync(WriteSizes).ConfigureAwait(false);

        var cities = MobaGolden.Cities(map);
        var citiesLength = 5 + (cities.Count * 5);
        int WriteCities()
        {
            var span = connection.Output.GetSpan(citiesLength)[..citiesLength];
            span[0] = 0xC1;
            span[1] = (byte)citiesLength;
            span[2] = PacketCode;
            span[3] = 0x2F;
            span[4] = (byte)cities.Count;
            var offset = 5;
            foreach (var (team, center, radius, burning) in cities)
            {
                span[offset] = (byte)team;
                span[offset + 1] = center.X;
                span[offset + 2] = center.Y;
                span[offset + 3] = radius;
                span[offset + 4] = burning ? (byte)1 : (byte)0;
                offset += 5;
            }

            return citiesLength;
        }

        await connection.SendAsync(WriteCities).ConfigureAwait(false);
        await SendGoldenDotsAsync(connection, map).ConfigureAwait(false);
    }

    /// <summary>
    /// The living golden monsters for the minimap dots and the counter of the scoreboard: <c>C2 hi lo D5 31 count(u16 LE) [ x y tier ] * count</c>
    /// (tier as <see cref="MobaGolden.Tier"/>: 1 small ... 4 Great Golden Dragon). They are sent to everybody: the invasion is public knowledge.
    /// </summary>
    private static async ValueTask SendGoldenDotsAsync(IConnection connection, GameMap map)
    {
        var dots = new List<(byte X, byte Y, byte Tier)>();
        foreach (var monster in MobaGolden.LiveMonsters(map))
        {
            dots.Add((monster.Position.X, monster.Position.Y, (byte)MobaGolden.TierOf(monster)));
            if (dots.Count >= MaxGoldenDots)
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
            span[4] = GoldenDotsSubCode;
            span[5] = (byte)(dots.Count & 0xFF);
            span[6] = (byte)(dots.Count >> 8);
            var offset = CreepHeaderLength;
            foreach (var (x, y, tier) in dots)
            {
                span[offset] = x;
                span[offset + 1] = y;
                span[offset + 2] = tier;
                offset += CreepEntrySize;
            }

            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }

    /// <summary>
    /// The pets every champion switched on from its inventory, so each client draws them next to it:
    /// <c>C1 len D5 2C count [ keyLo keyHi maskLo maskHi ] * count</c> (the mask is <see cref="MobaPets.ExtraMaskOf"/>). Only champions with a pet are listed.
    /// </summary>
    private static async ValueTask SendExtraPetsAsync(IConnection connection, Player viewer, IEnumerable<Player> champions, GameMap map)
    {
        var entries = new List<(ushort Key, ushort Mask)>();
        foreach (var champion in champions)
        {
            if (champion.IsMobaClone && ReferenceEquals(champion.CurrentMap, map) && MobaPets.ExtraMaskOf(champion) is var mask and not 0)
            {
                entries.Add((champion.GetId(viewer), mask));
            }
        }

        var length = 5 + (entries.Count * 4);
        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = (byte)length;
            span[2] = PacketCode;
            span[3] = 0x2C;
            span[4] = (byte)entries.Count;
            var offset = 5;
            foreach (var (key, mask) in entries)
            {
                span[offset] = (byte)(key & 0xFF);
                span[offset + 1] = (byte)(key >> 8);
                span[offset + 2] = (byte)(mask & 0xFF);
                span[offset + 3] = (byte)(mask >> 8);
                offset += 4;
            }

            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
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
