// <copyright file="MobaVision.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Vision v1 of the MOBA: a rule on who can be targeted, without fog on the client. An enemy champion can only
/// be targeted (basic attacks and targeted skills) while one of the attacker team's vision sources sees it: an
/// allied champion or creep within <see cref="UnitRadius"/> tiles, an allied structure within
/// <see cref="StructureRadius"/> or an allied ward within <see cref="WardRadius"/>. Wards are placed by the
/// champion (<see cref="TryPlaceWard"/>), last <see cref="WardSeconds"/> s, at most <see cref="MaxWardsPerChampion"/>
/// per champion, and the sweeper (<see cref="TrySweep"/>) destroys the enemy wards around the champion.
/// Creeps and turrets always see what is in their own range, so they need no rule.
/// </summary>
public static class MobaVision
{
    /// <summary>Gold a ward costs.</summary>
    public const int WardCost = 75;

    /// <summary>Seconds a ward lasts.</summary>
    public const int WardSeconds = 150;

    /// <summary>Wards one champion can have standing at once (the oldest is replaced).</summary>
    public const int MaxWardsPerChampion = 3;

    /// <summary>Vision radius of champions and creeps, in tiles.</summary>
    public const int UnitRadius = 9;

    /// <summary>Vision radius of turrets and the nexus, in tiles.</summary>
    public const int StructureRadius = 11;

    /// <summary>Vision radius of a ward, in tiles.</summary>
    public const int WardRadius = 8;

    /// <summary>Radius in which the sweeper destroys enemy wards, in tiles.</summary>
    public const int SweepRadius = 6;

    /// <summary>Cooldown of the sweeper in seconds.</summary>
    public const int SweepCooldownSeconds = 90;

    private static readonly object WardLock = new();

    private static readonly List<Ward> Wards = new();

    private static readonly Dictionary<Player, DateTime> SweepReadyAt = new();

    /// <summary>Gets the number of standing wards (expired ones are dropped first).</summary>
    public static int WardCount
    {
        get
        {
            lock (WardLock)
            {
                Wards.RemoveAll(w => w.ExpiresUtc <= DateTime.UtcNow);
                return Wards.Count;
            }
        }
    }

    /// <summary>Whether an attacker may target the target: only enemy champions hidden from its team are off limits.</summary>
    /// <param name="attacker">The attacking player.</param>
    /// <param name="target">The target.</param>
    /// <returns><c>false</c> if the target is an enemy champion the attacker team doesn't see.</returns>
    public static bool CanTarget(Player attacker, IAttackable target)
        => !attacker.IsMobaClone
           || target is not Player { IsMobaClone: true } champion
           || !MobaTeams.AreEnemies(attacker, champion)
           || IsVisibleTo(MobaTeams.GetTeam(attacker), champion);

    /// <summary>Whether a team sees a champion.</summary>
    /// <param name="team">The watching team.</param>
    /// <param name="target">The champion.</param>
    /// <returns><c>true</c> if any vision source of the team covers it.</returns>
    public static bool IsVisibleTo(MobaTeam team, Player target)
    {
        if (target.CurrentMap is not { } map)
        {
            return true;
        }

        var position = target.Position;
        if (map.GetAttackablesInRange(position, StructureRadius).Any(a => a.IsActive()
            && MobaTeams.GetTeam(a) == team
            && a.GetDistanceTo(position) <= (MobaStructures.IsStructure(a) ? StructureRadius : UnitRadius)))
        {
            return true;
        }

        return IsWardedBy(team, map, position);
    }

    /// <summary>Gets the positions of the standing wards of a team on a map (for the minimap).</summary>
    /// <param name="team">The team.</param>
    /// <param name="map">The map.</param>
    /// <returns>The ward positions.</returns>
    public static IReadOnlyList<Point> WardsOf(MobaTeam team, GameMap map)
    {
        lock (WardLock)
        {
            Wards.RemoveAll(w => w.ExpiresUtc <= DateTime.UtcNow);
            return Wards.Where(w => w.Team == team && ReferenceEquals(w.Map, map)).Select(w => w.Position).ToList();
        }
    }

    /// <summary>Places a ward of the champion at its position, paying <see cref="WardCost"/> gold.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>A message for the champion (success or why not).</returns>
    public static string TryPlaceWard(Player champion)
    {
        if (!champion.IsMobaClone || champion.CurrentMap is not { } map)
        {
            return "[MOBA] You need to be in a match.";
        }

        if (champion.Money < WardCost)
        {
            return $"[MOBA] A ward costs {WardCost} gold.";
        }

        champion.Money -= WardCost;
        lock (WardLock)
        {
            Wards.RemoveAll(w => w.ExpiresUtc <= DateTime.UtcNow);
            var own = Wards.Where(w => ReferenceEquals(w.Owner, champion)).OrderBy(w => w.ExpiresUtc).ToList();
            if (own.Count >= MaxWardsPerChampion)
            {
                Wards.Remove(own[0]);
            }

            Wards.Add(new Ward(champion, MobaTeams.GetTeam(champion), map, champion.Position, DateTime.UtcNow.AddSeconds(WardSeconds)));
        }

        return $"[MOBA] Ward placed ({WardSeconds} s, radius {WardRadius}).";
    }

    /// <summary>Destroys the enemy wards around the champion (sweeper), with a cooldown.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>A message for the champion.</returns>
    public static string TrySweep(Player champion)
    {
        if (!champion.IsMobaClone || champion.CurrentMap is not { } map)
        {
            return "[MOBA] You need to be in a match.";
        }

        lock (WardLock)
        {
            if (SweepReadyAt.TryGetValue(champion, out var ready) && ready > DateTime.UtcNow)
            {
                return $"[MOBA] Sweeper ready in {(int)(ready - DateTime.UtcNow).TotalSeconds} s.";
            }

            SweepReadyAt[champion] = DateTime.UtcNow.AddSeconds(SweepCooldownSeconds);
            var team = MobaTeams.GetTeam(champion);
            var removed = Wards.RemoveAll(w => w.ExpiresUtc > DateTime.UtcNow && w.Team != team && ReferenceEquals(w.Map, map)
                                               && w.Position.EuclideanDistanceTo(champion.Position) <= SweepRadius);
            return removed > 0 ? $"[MOBA] Sweeper destroyed {removed} ward(s)." : "[MOBA] No enemy wards nearby.";
        }
    }

    /// <summary>Removes every ward and cooldown (a match ended).</summary>
    public static void Reset()
    {
        lock (WardLock)
        {
            Wards.Clear();
            SweepReadyAt.Clear();
        }
    }

    /// <summary>Adds a ward without paying (tests).</summary>
    /// <param name="owner">The owner.</param>
    /// <param name="team">The team.</param>
    /// <param name="map">The map.</param>
    /// <param name="position">The position.</param>
    /// <param name="expiresUtc">When it expires.</param>
    internal static void AddWardForTest(Player owner, MobaTeam team, GameMap map, Point position, DateTime expiresUtc)
    {
        lock (WardLock)
        {
            Wards.Add(new Ward(owner, team, map, position, expiresUtc));
        }
    }

    private static bool IsWardedBy(MobaTeam team, GameMap map, Point position)
    {
        lock (WardLock)
        {
            Wards.RemoveAll(w => w.ExpiresUtc <= DateTime.UtcNow);
            return Wards.Any(w => w.Team == team && ReferenceEquals(w.Map, map) && w.Position.EuclideanDistanceTo(position) <= WardRadius);
        }
    }

    private sealed record Ward(Player Owner, MobaTeam Team, GameMap Map, Point Position, DateTime ExpiresUtc);
}
