// <copyright file="MobaCreepDirectionTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The direction codes of the creeps' walk steps are the ones the client rebuilds the walk from: a step must lead to its target tile.
/// </summary>
[TestFixture]
public class MobaCreepDirectionTests
{
    /// <summary>
    /// Following the direction code of any one-tile step arrives at the tile the step was meant for.
    /// </summary>
    [Test]
    public void TheDirectionCodeOfAStepLeadsToItsTile()
    {
        var from = new Point(100, 100);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                var to = new Point((byte)(100 + dx), (byte)(100 + dy));
                var direction = MobaLaneCreepIntelligence.GetDirection(from, to);
                Assert.That(from.CalculateTargetPoint(direction), Is.EqualTo(to), $"step ({dx},{dy}) coded as {direction}");
            }
        }
    }
}
