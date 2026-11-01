// <copyright file="MobaCommanderBoard.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;

/// <summary>
/// The economy of the commanders of one arena: how much essence each team has, how much it earns per second and who
/// commands it. Pure state, thread-safe; the map and the creeps are not its business (see <see cref="MobaCommanderOrders"/>).
/// </summary>
public sealed class MobaCommanderBoard
{
    /// <summary>Essence per second a team earns before any upgrade.</summary>
    public const double BaseIncomePerSecond = 6;

    /// <summary>Essence per second each income level adds.</summary>
    public const double IncomeStepPerLevel = 3;

    /// <summary>The highest income level.</summary>
    public const int MaxIncomeLevel = 5;

    /// <summary>Essence each team starts the match with.</summary>
    public const double StartEssence = 200;

    /// <summary>The most essence a team can hold: it has to be spent.</summary>
    public const double EssenceCap = 3000;

    private static readonly int[] IncomeUpgradeCosts = [150, 250, 400, 600, 900];

    private static readonly ConditionalWeakTable<MobaArena, MobaCommanderBoard> Boards = new();

    private readonly object _sync = new();
    private readonly TeamState _blue = new();
    private readonly TeamState _red = new();

    /// <summary>Gets a value indicating whether the economy runs (the match started and has not ended).</summary>
    public bool Running
    {
        get
        {
            lock (this._sync)
            {
                return this._running;
            }
        }
    }

    private bool _running;

    /// <summary>
    /// Gets the board of an arena.
    /// </summary>
    /// <param name="arena">The arena.</param>
    /// <returns>The board.</returns>
    public static MobaCommanderBoard Of(MobaArena arena) => Boards.GetValue(arena, _ => new MobaCommanderBoard());

    /// <summary>
    /// Gets the essence a team holds.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <returns>The essence.</returns>
    public double Essence(MobaTeam team)
    {
        lock (this._sync)
        {
            return this.StateOf(team).Essence;
        }
    }

    /// <summary>
    /// Gets the income level of a team.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <returns>The level, from 0.</returns>
    public int IncomeLevel(MobaTeam team)
    {
        lock (this._sync)
        {
            return this.StateOf(team).IncomeLevel;
        }
    }

    /// <summary>
    /// Gets the essence per second a team earns.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <returns>The income.</returns>
    public double IncomePerSecond(MobaTeam team) => BaseIncomePerSecond + (IncomeStepPerLevel * this.IncomeLevel(team));

    /// <summary>
    /// Gets what the next income upgrade of a team costs.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <returns>The cost, or <see langword="null"/> if the team is at the highest level.</returns>
    public int? NextIncomeCost(MobaTeam team)
    {
        var level = this.IncomeLevel(team);
        return level >= MaxIncomeLevel ? null : IncomeUpgradeCosts[level];
    }

    /// <summary>
    /// Starts the economy: both teams get the starting essence and income level 0. Whoever claimed command keeps it.
    /// </summary>
    public void Begin()
    {
        lock (this._sync)
        {
            foreach (var state in new[] { this._blue, this._red })
            {
                state.Essence = StartEssence;
                state.IncomeLevel = 0;
            }

            this._running = true;
        }
    }

    /// <summary>
    /// Ends the economy and forgets everything, the commanders included.
    /// </summary>
    public void Reset()
    {
        lock (this._sync)
        {
            foreach (var state in new[] { this._blue, this._red })
            {
                state.Essence = 0;
                state.IncomeLevel = 0;
                state.Commander = null;
            }

            this._running = false;
        }
    }

    /// <summary>
    /// Lets time pass: both teams earn their income. Does nothing while the economy does not run.
    /// </summary>
    /// <param name="seconds">The seconds that passed.</param>
    public void Tick(double seconds)
    {
        lock (this._sync)
        {
            if (!this._running)
            {
                return;
            }

            foreach (var state in new[] { this._blue, this._red })
            {
                var income = BaseIncomePerSecond + (IncomeStepPerLevel * state.IncomeLevel);
                state.Essence = Math.Min(EssenceCap, state.Essence + (income * seconds));
            }
        }
    }

    /// <summary>
    /// Spends essence of a team.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <param name="cost">The cost.</param>
    /// <returns><see langword="true"/> if the team had it and it was spent.</returns>
    public bool TrySpend(MobaTeam team, int cost)
    {
        lock (this._sync)
        {
            var state = this.StateOf(team);
            if (!this._running || state.Essence < cost)
            {
                return false;
            }

            state.Essence -= cost;
            return true;
        }
    }

    /// <summary>
    /// Gives back essence which could not be used (the order failed).
    /// </summary>
    /// <param name="team">The team.</param>
    /// <param name="amount">The amount.</param>
    public void Refund(MobaTeam team, int amount)
    {
        lock (this._sync)
        {
            var state = this.StateOf(team);
            state.Essence = Math.Min(EssenceCap, state.Essence + amount);
        }
    }

    /// <summary>
    /// Buys the next income level of a team.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <returns><see langword="true"/> if it was bought.</returns>
    public bool TryBuyIncome(MobaTeam team)
    {
        lock (this._sync)
        {
            var state = this.StateOf(team);
            if (!this._running || state.IncomeLevel >= MaxIncomeLevel || state.Essence < IncomeUpgradeCosts[state.IncomeLevel])
            {
                return false;
            }

            state.Essence -= IncomeUpgradeCosts[state.IncomeLevel];
            state.IncomeLevel++;
            return true;
        }
    }

    /// <summary>
    /// Gets who commands a team.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <returns>The account of the commander, or <see langword="null"/> if the team has no human commander.</returns>
    public Guid? CommanderOf(MobaTeam team)
    {
        lock (this._sync)
        {
            return this.StateOf(team).Commander;
        }
    }

    /// <summary>
    /// Claims the command of a team.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <param name="account">The account which claims it.</param>
    /// <returns><see langword="true"/> if the account commands the team now (it already did, or the command was free).</returns>
    public bool TryClaim(MobaTeam team, Guid account)
    {
        if (team == MobaTeam.None)
        {
            return false;
        }

        lock (this._sync)
        {
            var state = this.StateOf(team);
            if (state.Commander is { } current && current != account)
            {
                return false;
            }

            state.Commander = account;
            return true;
        }
    }

    /// <summary>
    /// Gives up the command of a team.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <param name="account">The account which commands it.</param>
    /// <returns><see langword="true"/> if the account commanded the team and does not any more.</returns>
    public bool Release(MobaTeam team, Guid account)
    {
        lock (this._sync)
        {
            var state = this.StateOf(team);
            if (state.Commander != account)
            {
                return false;
            }

            state.Commander = null;
            return true;
        }
    }

    private TeamState StateOf(MobaTeam team) => team == MobaTeam.Red ? this._red : this._blue;

    private sealed class TeamState
    {
        public double Essence { get; set; }

        public int IncomeLevel { get; set; }

        public Guid? Commander { get; set; }
    }
}
