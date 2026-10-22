// <copyright file="MobaRoom.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// A private room: friends who play a match without the queue. The host creates it with a name, the players per team (1 to 5),
/// the map (the three lanes with the jungle, or the middle lane only) and the bots; everybody who joins picks a team and the
/// host starts the match, can move players between the teams and kick them. A room closes by itself after a while without
/// news (see <see cref="MobaRooms.IdleLimit"/>).
/// </summary>
public sealed class MobaRoom
{
    /// <summary>Gets the identifier the players join with.</summary>
    public required int Id { get; init; }

    /// <summary>Gets the name the host gave the room.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the players per team.</summary>
    public required int TeamSize { get; init; }

    /// <summary>Gets a value indicating whether only the middle lane is played (no jungle).</summary>
    public required bool MidOnly { get; init; }

    /// <summary>Gets the format of the match: the layout of the arena.</summary>
    public MobaFormat Format => this.MidOnly ? MobaFormat.Aram : MobaFormat.Classic;

    /// <summary>Gets the password of the room; empty for an open room.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the room needs a password to join (invited players do not).</summary>
    public bool IsLocked => this.Password.Length > 0;

    /// <summary>Gets the players who said they are ready (the host starts when everybody else is).</summary>
    public HashSet<Guid> Ready { get; } = new();

    /// <summary>Gets the accounts the host invited: they join without the password.</summary>
    public HashSet<Guid> Invited { get; } = new();

    /// <summary>Gets the identifier the players of a finished match find their room again with.</summary>
    public Guid RematchId { get; init; } = Guid.NewGuid();

    /// <summary>Gets or sets the account of the host.</summary>
    public Guid Host { get; set; }

    /// <summary>Gets or sets a value indicating whether the free places are filled with bots when the match starts.</summary>
    public bool FillWithBots { get; set; } = true;

    /// <summary>Gets when the room was created.</summary>
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Gets or sets when something happened in the room last (somebody joined, left, wrote...).</summary>
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets a value indicating whether the members were told the room is about to close.</summary>
    public bool Warned { get; set; }

    /// <summary>Gets the members: account to session and team.</summary>
    public Dictionary<Guid, (Player Session, MobaTeam Team)> Members { get; } = new();

    /// <summary>Gets the accounts the host kicked (they cannot join again).</summary>
    public HashSet<Guid> Kicked { get; } = new();

    /// <summary>Gets the last lines of the chat of the room, for the players who join later.</summary>
    public List<(string Name, string Text, bool IsNotice)> Chat { get; } = new();

    /// <summary>Gets a short description of the match ("3v3 carril central").</summary>
    public string Label => $"{this.TeamSize}v{this.TeamSize} {(this.MidOnly ? "carril central" : "3 carriles")}";

    /// <summary>Gets the players of a team.</summary>
    /// <param name="team">The team.</param>
    /// <returns>The sessions.</returns>
    public IReadOnlyList<Player> TeamPlayers(MobaTeam team) => this.Members.Values.Where(m => m.Team == team).Select(m => m.Session).ToList();
}
