// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Net.Sockets;
using System.Threading.Channels;
using Coop.Contract;
using KcdUs.Wire;

namespace KcdUs.Agent;

/// <summary>The agent's connection to a relay: the seam the session talks through (tests use a real relay on loopback).</summary>
public interface IRelayLink : IAsyncDisposable
{
    event Action<Frame>? Frame;
    /// <summary>The connection came up (the Hello is already sent) or went down.</summary>
    event Action<bool>? ConnectionChanged;
    bool Connected { get; }
    void Send(MessageType type, string text);
    void Start(CancellationToken ct);
}

public sealed class RelayEndpoint
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = Proto.DefaultPort;
    public string Name { get; init; } = "Henry";
    public string Role { get; init; } = "guest";
    public string Password { get; init; } = "";
    public string Release { get; init; } = KcdUs.Wire.Release.Current;
    /// <summary>What this build tells the room about itself (game, versions, payload hashes, capabilities). The default is a bare development handshake.</summary>
    public Func<RoomHandshake>? HandshakeProvider { get; init; }
    public RoomHandshake Handshake { get; init; } = new("kcd1", KcdUs.Wire.Release.Current, Proto.ProtocolVersion, RoomHandshake.CurrentContractVersion, "", "", "", "", "", new Dictionary<string, CapabilityLevel>());
    /// <summary>This player's room identity (a key pair kept on disk); the default is a throw-away one.</summary>
    public ParticipantBindings.Identity Identity { get; init; } = ParticipantBindings.Identity.CreateEphemeral();
}

/// <summary>
/// Connects, says Hello, reads frames, and reconnects after a drop (every 3 s, for ever) unless the relay refused us: a refusal
/// (wrong version, wrong password, full, host taken) is final and is reported, not retried.
/// </summary>
public sealed class RelayClient : IRelayLink
{
    private readonly RelayEndpoint _ep;
    private readonly Channel<byte[]> _out = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(800) { FullMode = BoundedChannelFullMode.DropOldest });
    private CancellationTokenSource? _cts;
    private Task? _run;

    public RelayClient(RelayEndpoint ep) { _ep = ep; }

    public event Action<Frame>? Frame;
    public event Action<bool>? ConnectionChanged;
    public bool Connected { get; private set; }
    /// <summary>Set when the relay refused us: "version|relay is 0.2.0, you are 0.1.0".</summary>
    public string? Refused { get; private set; }
    public string? LastError { get; private set; }

    public void Send(MessageType type, string text)
    {
        if (!Connected) return;
        _out.Writer.TryWrite(FrameIO.Encode(type, text));
    }

    public void Start(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _run = Task.Run(() => Loop(_cts.Token));
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var tcp = new TcpClient { NoDelay = true };
            try
            {
                await tcp.ConnectAsync(_ep.Host, _ep.Port, ct).ConfigureAwait(false);
                var s = tcp.GetStream();
                LastError = null;
                // the relay speaks first: a challenge that the identity proof is signed over
                string challenge;
                using (var wait = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    wait.CancelAfter(TimeSpan.FromSeconds(6));
                    var first = await FrameIO.ReadAsync(s, wait.Token).ConfigureAwait(false);
                    if (first is not { Type: MessageType.Challenge } c0) throw new IOException("the relay did not send its challenge (an older relay: update it to the same build)");
                    challenge = c0.Text;
                }
                await FrameIO.WriteAsync(s, MessageType.Hello,
                    $"{Proto.ProtocolVersion}|{_ep.Release}|{Safe.Name(_ep.Name)}|{_ep.Role}|{Safe.Clean(_ep.Password, 40)}|{(_ep.HandshakeProvider?.Invoke() ?? _ep.Handshake).Encode()}|{_ep.Identity.ParticipantId};{_ep.Identity.PublicKey};{_ep.Identity.Sign(challenge)}", ct).ConfigureAwait(false);
                while (_out.Reader.TryRead(out _)) { }   // nothing queued while down is replayed: a position that old is no use
                Connected = true;
                ConnectionChanged?.Invoke(true);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var writer = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var b in _out.Reader.ReadAllAsync(linked.Token).ConfigureAwait(false))
                            await s.WriteAsync(b, linked.Token).ConfigureAwait(false);
                    }
                    catch { }
                });
                try
                {
                    while (!linked.IsCancellationRequested)
                    {
                        var f = await FrameIO.ReadAsync(s, linked.Token).ConfigureAwait(false);
                        if (f is null) break;
                        if (f.Value.Type == MessageType.Reject)
                        {
                            Refused = f.Value.Text;
                            Frame?.Invoke(f.Value);
                            return;   // final
                        }
                        Frame?.Invoke(f.Value);
                    }
                }
                finally
                {
                    linked.Cancel();
                    try { await writer.ConfigureAwait(false); } catch { }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { LastError = e.Message; }
            finally
            {
                if (Connected) { Connected = false; ConnectionChanged?.Invoke(false); }
            }
            try { await Task.Delay(3000, ct).ConfigureAwait(false); } catch { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Connected) Send(MessageType.Bye, "quit");
        await Task.Delay(30);
        _cts?.Cancel();
        if (_run != null) { try { await _run.ConfigureAwait(false); } catch { } }
    }
}
