// <copyright file="MobaDamageDirectionPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Tells the MOBA client where its champion's damage came from. Layout:
/// <code>
/// C1 08 D5 34  attackerX  attackerY  percent  flags
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: damage direction", Description = "Tells the MOBA client where the damage its champion took came from (the red wedge).")]
[Guid("C17D5E92-4A38-4B60-8F21-3D9A6E0B7C54")]
public class MobaDamageDirectionPlugIn : IMobaDamageDirectionPlugIn
{
    private const int Length = 8;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaDamageDirectionPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaDamageDirectionPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowDamageAsync(byte attackerX, byte attackerY, byte percent, byte flags)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        int Write()
        {
            var span = connection.Output.GetSpan(Length)[..Length];
            span[0] = 0xC1;
            span[1] = Length;
            span[2] = 0xD5;
            span[3] = 0x34;
            span[4] = attackerX;
            span[5] = attackerY;
            span[6] = percent;
            span[7] = flags;
            return Length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
