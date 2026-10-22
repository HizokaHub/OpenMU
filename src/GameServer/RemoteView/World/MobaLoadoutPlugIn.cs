// <copyright file="MobaLoadoutPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Opens the loadout window of the MOBA client. Layout:
/// <code>
/// C1 len D5 19  baseClass  min  max  selectableCount  selectedCount  selectable(u16 LE)...  selected(u16 LE)...
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: loadout window", Description = "Opens the window where the player picks the skills of its champion.")]
[Guid("A47C3E19-6B02-4D85-9F3A-1E8D5C7B2046")]
public class MobaLoadoutPlugIn : IMobaLoadoutPlugIn
{
    private const int HeaderLength = 9;
    private const int MaxSkills = 40;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaLoadoutPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaLoadoutPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowLoadoutAsync(MobaLoadoutInfo info)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var selectable = info.Selectable.Take(MaxSkills).ToArray();
        var selected = info.Selected.Take(MaxSkills).ToArray();
        var length = HeaderLength + (2 * (selectable.Length + selected.Length));

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = (byte)length;
            span[2] = 0xD5;
            span[3] = 0x19;
            span[4] = info.BaseClass;
            span[5] = (byte)info.MinSkills;
            span[6] = (byte)info.MaxSkills;
            span[7] = (byte)selectable.Length;
            span[8] = (byte)selected.Length;
            var offset = HeaderLength;
            foreach (var skill in selectable.Concat(selected))
            {
                BitConverter.TryWriteBytes(span.Slice(offset, 2), (ushort)skill);
                offset += 2;
            }

            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
