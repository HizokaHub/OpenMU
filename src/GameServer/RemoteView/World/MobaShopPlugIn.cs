// <copyright file="MobaShopPlugIn.cs" company="MUnique">
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
/// Sends the MOBA shop packets:
/// <code>
/// Menu:   C2 lenHi lenLo D5 06  menuId  titleLen(u8) title(UTF-8)  textLen(u16 LE) text(UTF-8)
///                               count(u8)  count * [ len(u8) option(UTF-8) ]
/// Prices: C2 lenHi lenLo D5 08  sellPercent(u8)  count(u16 LE)
///                               count * [ type(u16 LE) level optLevel flags excCount price(u32 LE) ]
///         flags: 0x01 = luck, 0x02 = price is per unit of a stack.
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: shop", Description = "Sends the MOBA shop category menu and the server-side item prices.")]
[Guid("8E3B51C7-2D94-4A6F-B0E8-5C71A9D24F36")]
public class MobaShopPlugIn : IMobaShopPlugIn
{
    private const byte PacketCode = 0xD5;
    private const byte MenuSubCode = 0x06;
    private const byte PricesSubCode = 0x08;
    private const int HeaderLength = 5;
    private const int MaxTitleBytes = 64;
    private const int MaxTextBytes = 512;
    private const int MaxOptionBytes = 64;
    private const int MaxOptions = 20;
    private const int PriceEntryLength = 10;
    private const int MaxPrices = 500;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaShopPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaShopPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowMenuAsync(byte menuId, string title, string text, IReadOnlyList<string> options)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var titleBytes = Truncate(title, MaxTitleBytes);
        var textBytes = Truncate(text, MaxTextBytes);
        var optionBytes = options.Take(MaxOptions).Select(o => Truncate(o, MaxOptionBytes)).ToList();
        var length = HeaderLength + 1 + 1 + titleBytes.Length + 2 + textBytes.Length + 1 + optionBytes.Sum(o => 1 + o.Length);

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            WriteHeader(span, length, MenuSubCode);
            var offset = HeaderLength;
            span[offset++] = menuId;
            span[offset++] = (byte)titleBytes.Length;
            titleBytes.CopyTo(span[offset..]);
            offset += titleBytes.Length;
            span[offset++] = (byte)(textBytes.Length & 0xFF);
            span[offset++] = (byte)(textBytes.Length >> 8);
            textBytes.CopyTo(span[offset..]);
            offset += textBytes.Length;
            span[offset++] = (byte)optionBytes.Count;
            foreach (var option in optionBytes)
            {
                span[offset++] = (byte)option.Length;
                option.CopyTo(span[offset..]);
                offset += option.Length;
            }

            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ShowPricesAsync(IReadOnlyCollection<MobaShopPrice> prices, byte sellPercent)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var entries = prices.Take(MaxPrices).ToList();
        var length = HeaderLength + 1 + 2 + (entries.Count * PriceEntryLength);

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            WriteHeader(span, length, PricesSubCode);
            var offset = HeaderLength;
            span[offset++] = sellPercent;
            span[offset++] = (byte)(entries.Count & 0xFF);
            span[offset++] = (byte)(entries.Count >> 8);
            foreach (var price in entries)
            {
                span[offset++] = (byte)(price.ItemType & 0xFF);
                span[offset++] = (byte)(price.ItemType >> 8);
                span[offset++] = price.Level;
                span[offset++] = price.OptionLevel;
                span[offset++] = (byte)((price.HasLuck ? 0x01 : 0) | (price.PerUnit ? 0x02 : 0));
                span[offset++] = price.ExcellentCount;
                span[offset++] = (byte)(price.Price & 0xFF);
                span[offset++] = (byte)((price.Price >> 8) & 0xFF);
                span[offset++] = (byte)((price.Price >> 16) & 0xFF);
                span[offset++] = (byte)((price.Price >> 24) & 0xFF);
            }

            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }

    private static void WriteHeader(Span<byte> span, int length, byte subCode)
    {
        span[0] = 0xC2;
        span[1] = (byte)(length >> 8);
        span[2] = (byte)(length & 0xFF);
        span[3] = PacketCode;
        span[4] = subCode;
    }

    private static byte[] Truncate(string value, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes)
        {
            return bytes;
        }

        // Cut on a character boundary so the client never sees half a UTF-8 sequence.
        var length = maxBytes;
        while (length > 0 && (bytes[length] & 0xC0) == 0x80)
        {
            length--;
        }

        return bytes[..length];
    }
}
