// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
// Modified version of the Kingdom Come: Together relay protocol (https://github.com/DeepFriedDepp/KingdomCome-Together).
using System.Text;

namespace KcdUs.Wire;

/// <summary>
/// The relay wire protocol of Kingdom Come: Deliver Us.
///
/// Framing (every packet):  [type:1][payloadLen:2 LE][payload:N], N at most 65535. Every payload is UTF-8 text with
/// '|' between fields, so a packet can be read in a log and a new field can be appended without breaking an old reader.
///
/// C to S  0x01 Hello       proto|release|name|role|password       role: host or guest
///         0x02 State       x,y,z|yaw|vx,vy,vz|flags|hp|stam|anim|worldTime
///         0x03 Chat        text
///         0x04 Ping        stamp
///         0x05 Event       kind|payload           broadcast to every other player
///         0x06 HostEvent   kind|payload           accepted from the host only, relayed to the guests
///         0x07 Bye         reason
///         0x08 InfoRequest (empty)                answered without a Hello: the server list's ping
/// S to C  0x81 Welcome     id|hostId|proto|release|serverName
///         0x82 Reject      code|detail            code: version, password, full, host-taken, protocol
///         0x83 PlayerList  id:name:role;id:name:role;...
///         0x84 PlayerJoined id|name|role
///         0x85 PlayerLeft  id|reason
///         0x86 PState      id|state fields as sent
///         0x87 PChat       id|name|text
///         0x88 Pong        stamp
///         0x89 PEvent      id|kind|payload
///         0x8A PHostEvent  id|kind|payload
///         0x8B Info        serverName|players|max|release|hostName
///
/// A Hello whose release differs from the relay's is rejected with code "version": two builds that share a protocol
/// number but not a release do not talk (the version string is the whole contract).
/// </summary>
public static class Proto
{
    /// <summary>Bumped when a frame's meaning changes. A relay refuses a different number outright.</summary>
    public const int ProtocolVersion = 1;
    public const int MaxPayload = 65535;
    public const int DefaultPort = 7788;
}

public enum MessageType : byte
{
    Hello = 0x01,
    State = 0x02,
    Chat = 0x03,
    Ping = 0x04,
    Event = 0x05,
    HostEvent = 0x06,
    Bye = 0x07,
    InfoRequest = 0x08,

    Welcome = 0x81,
    Reject = 0x82,
    PlayerList = 0x83,
    PlayerJoined = 0x84,
    PlayerLeft = 0x85,
    PState = 0x86,
    PChat = 0x87,
    Pong = 0x88,
    PEvent = 0x89,
    PHostEvent = 0x8A,
    Info = 0x8B,
}

public readonly record struct Frame(MessageType Type, string Text)
{
    public string[] Fields => Text.Split('|');
}

public static class FrameIO
{
    public static byte[] Encode(MessageType type, string text)
    {
        var body = Encoding.UTF8.GetBytes(text ?? "");
        if (body.Length > Proto.MaxPayload)
            throw new ArgumentException("payload too large: " + body.Length);
        var buf = new byte[3 + body.Length];
        buf[0] = (byte)type;
        buf[1] = (byte)(body.Length & 0xFF);
        buf[2] = (byte)(body.Length >> 8);
        Buffer.BlockCopy(body, 0, buf, 3, body.Length);
        return buf;
    }

    public static async Task WriteAsync(Stream s, MessageType type, string text, CancellationToken ct = default)
    {
        var buf = Encode(type, text);
        await s.WriteAsync(buf, ct).ConfigureAwait(false);
        await s.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads one frame; null when the stream ended cleanly at a frame boundary.</summary>
    public static async Task<Frame?> ReadAsync(Stream s, CancellationToken ct = default)
    {
        var head = new byte[3];
        if (!await ReadExactAsync(s, head, ct).ConfigureAwait(false))
            return null;
        int len = head[1] | (head[2] << 8);
        var body = new byte[len];
        if (len > 0 && !await ReadExactAsync(s, body, ct).ConfigureAwait(false))
            return null;
        return new Frame((MessageType)head[0], Encoding.UTF8.GetString(body));
    }

    private static async Task<bool> ReadExactAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        int got = 0;
        while (got < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(got), ct).ConfigureAwait(false);
            if (n == 0) return false;
            got += n;
        }
        return true;
    }
}
