// <copyright file="MobaPuppetPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends the state of the control of a disconnected teammate. Layout:
/// <code>
/// C2 hi lo D5 30  controlling  x  y  candidateCount [ keyLo keyHi baseClass hp% mp% selected nameLen name ]*  skillCount [ skillLo skillHi rank|0x80 buff  leftDs(u16 LE)  fullDs(u16 LE) ]*
/// </code>
/// The cooldowns are in tenths of a second.
/// </summary>
[PlugIn]
[Display(Name = "MOBA: control of a teammate", Description = "Shows the disconnected teammates a player can control, and the skills of the one it controls.")]
[Guid("C3E7A915-5D28-4B61-9A04-1F8D6E2B7C30")]
public class MobaPuppetPlugIn : IMobaPuppetPlugIn
{
    private const int HeaderLength = 5;
    private const int MaxNameBytes = 12;
    private const int MaxSkills = 32;
    private const int MillisecondsPerTenth = 100;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaPuppetPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaPuppetPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowPuppetAsync(MobaPuppetInfo info)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var body = new List<byte> { info.Controlling ? (byte)1 : (byte)0, info.X, info.Y, (byte)Math.Min(info.Candidates.Count, byte.MaxValue) };
        foreach (var candidate in info.Candidates.Take(byte.MaxValue))
        {
            body.Add((byte)(candidate.Key & 0xFF));
            body.Add((byte)(candidate.Key >> 8));
            body.Add(candidate.BaseClass);
            body.Add(candidate.HpPercent);
            body.Add(candidate.MpPercent);
            body.Add(candidate.Selected ? (byte)1 : (byte)0);
            var name = Encoding.UTF8.GetBytes(candidate.Name);
            var count = Math.Min(name.Length, MaxNameBytes);
            body.Add((byte)count);
            body.AddRange(name.AsSpan(0, count).ToArray());
        }

        var skills = info.Skills.Take(MaxSkills).ToList();
        body.Add((byte)skills.Count);
        foreach (var skill in skills)
        {
            body.Add((byte)((ushort)skill.Number & 0xFF));
            body.Add((byte)((ushort)skill.Number >> 8));
            body.Add((byte)(skill.Rank | (skill.IsBuff ? 0x80 : 0)));
            AddTenths(body, skill.CooldownLeftMs);
            AddTenths(body, skill.CooldownFullMs);
        }

        var length = HeaderLength + body.Count;

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC2;
            span[1] = (byte)(length >> 8);
            span[2] = (byte)(length & 0xFF);
            span[3] = 0xD5;
            span[4] = 0x30;
            body.CopyTo(span[HeaderLength..]);
            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }

    private static void AddTenths(List<byte> body, int milliseconds)
    {
        var tenths = (ushort)Math.Clamp(milliseconds / MillisecondsPerTenth, 0, ushort.MaxValue);
        body.Add((byte)(tenths & 0xFF));
        body.Add((byte)(tenths >> 8));
    }
}
