using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;
using Nelsar.Axia.Infrastructure.Identity;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Whether a directory's certificate is believed — against a real TLS handshake where it counts.
///
/// The connection exists to keep a password from being read on the way to the directory, so a
/// certificate that is merely present is not enough: it has to chain to something trusted and name
/// the host that was dialled. A client that accepts any certificate is safe from someone listening
/// and hands the password to anyone in the middle.
/// </summary>
public sealed class DirectoryTlsTests : IDisposable
{
    /// <summary>
    /// Dialled by address rather than as "localhost", which resolves to ::1 first on some hosts and
    /// 127.0.0.1 on others, while the listener is on one of them. It also means the handshake
    /// tests check an IP address against the certificate's names, not just a DNS name.
    /// </summary>
    private const string Loopback = "127.0.0.1";

    private readonly X509Certificate2 _authority = Certificates.Authority("Axia Test CA");
    private readonly X509Certificate2 _otherAuthority = Certificates.Authority("Somebody Else's CA");
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var file in _files)
            File.Delete(file);
        _authority.Dispose();
        _otherAuthority.Dispose();
    }

    // ---- The decision itself -----------------------------------------------------------------

    [Fact]
    public void ACertificateTheSystemTrusts_IsAccepted()
    {
        using var server = Certificates.Server(_authority, "localhost");

        Assert.True(Validator([]).Validate(this, server, null, SslPolicyErrors.None));
    }

    [Fact]
    public void AnUntrustedChain_IsRefused_WhenNoCaIsConfigured()
    {
        using var server = Certificates.Server(_authority, "localhost");
        var validator = Validator([]);

        Assert.False(validator.Validate(this, server, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.True(validator.Rejected);
    }

    [Fact]
    public void AnUntrustedChain_IsAccepted_WhenItLeadsToTheConfiguredCa()
    {
        using var server = Certificates.Server(_authority, "localhost");

        Assert.True(Validator([_authority]).Validate(this, server, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void AChainToADifferentCa_IsRefused()
    {
        using var server = Certificates.Server(_otherAuthority, "localhost");

        Assert.False(Validator([_authority]).Validate(this, server, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors)]
    public void AWrongName_IsRefused_WhateverTheCa(SslPolicyErrors errors)
    {
        // The right CA, the wrong server: what an interception with a stolen or mis-issued
        // certificate looks like.
        using var server = Certificates.Server(_authority, "elsewhere.example.com");

        Assert.False(Validator([_authority]).Validate(this, server, null, errors));
    }

    [Fact]
    public void NoCertificateAtAll_IsRefused()
    {
        Assert.False(Validator([_authority]).Validate(this, null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
    }

    [Fact]
    public void ARefusal_NamesTheServerAndTheCertificate()
    {
        var log = new ListLogger();
        using var server = Certificates.Server(_authority, "localhost");

        new DirectoryCertificateValidator([], "ldap.example.com:636", log)
            .Validate(this, server, null, SslPolicyErrors.RemoteCertificateChainErrors);

        var line = Assert.Single(log.Lines);
        Assert.Contains("ldap.example.com:636", line);
        Assert.Contains("CN=localhost", line);
        Assert.Contains(server.Thumbprint, line);
    }

    // ---- Reading the configured CA -----------------------------------------------------------

    [Fact]
    public void ACaCanBeGivenAsPem_OrAsDer()
    {
        var pem = WriteFile(".pem", System.Text.Encoding.ASCII.GetBytes(_authority.ExportCertificatePem()));
        var der = WriteFile(".cer", _authority.Export(X509ContentType.Cert));

        Assert.Equal(_authority.Thumbprint, Assert.Single(TrustedCertificateAuthorities.Load(pem)).Thumbprint);
        Assert.Equal(_authority.Thumbprint, Assert.Single(TrustedCertificateAuthorities.Load(der)).Thumbprint);
    }

    [Fact]
    public void APemBundle_IsReadWhole()
    {
        var bundle = WriteFile(".pem", System.Text.Encoding.ASCII.GetBytes(
            _authority.ExportCertificatePem() + "\n" + _otherAuthority.ExportCertificatePem()));

        Assert.Equal(2, TrustedCertificateAuthorities.Load(bundle).Count);
    }

    [Fact]
    public void AFileThatIsNotACertificate_IsRefusedWithItsPath()
    {
        var junk = WriteFile(".pem", "not a certificate"u8.ToArray());

        var error = Assert.Throws<InvalidOperationException>(() => TrustedCertificateAuthorities.Load(junk));

        Assert.Contains(junk, error.Message);
    }

    [Fact]
    public void NoPath_MeansNoExtraCa()
    {
        Assert.Empty(TrustedCertificateAuthorities.Load(null));
        Assert.Empty(TrustedCertificateAuthorities.Load("  "));
    }

    // ---- Through a real handshake ------------------------------------------------------------

    [Fact]
    public async Task Ldaps_Connects_WhenTheCertificateLeadsToTheConfiguredCa()
    {
        using var certificate = Certificates.Server(_authority, Loopback);
        await using var server = TlsServer.Start(certificate);

        using var connection = Factory(server.Port, LdapSecurity.Ldaps, CaFile(_authority)).Open();

        Assert.True(connection.Connected);
        // Bounded: a connection that never starts TLS leaves the server waiting for a handshake
        // that will not come, and that should fail this test rather than hang it.
        Assert.True(await server.HandshakeCompleted.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Ldaps_Refuses_ACertificateNothingVouchesFor_AndSaysItWasTheCertificate()
    {
        using var certificate = Certificates.Server(_authority, Loopback);
        await using var server = TlsServer.Start(certificate);
        var log = new ListLogger();

        var error = Assert.Throws<DirectoryUnavailableException>(
            () => Factory(server.Port, LdapSecurity.Ldaps, trustedCa: null, log).Open());

        Assert.Equal(ApplicationMessages.DirectoryCertificateRejected(), error.Message);
        Assert.Contains(log.Lines, line => line.Contains("Refused the TLS certificate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ldaps_Refuses_TheRightCaForTheWrongServer()
    {
        using var certificate = Certificates.Server(_authority, "elsewhere.example.com");
        await using var server = TlsServer.Start(certificate);

        Assert.Throws<DirectoryUnavailableException>(
            () => Factory(server.Port, LdapSecurity.Ldaps, CaFile(_authority)).Open());
    }

    [Fact]
    public async Task StartTls_ThatTheServerRefuses_FailsInsteadOfCarryingOnInPlainText()
    {
        // Carrying on is the downgrade an attacker arranges by answering StartTLS with an error
        // in place of the server: the bind that follows would then go out unencrypted.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new List<byte>();
        var serving = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];

            // The extended request: 30 len 02 01 <id> 77 ... — the reply has to carry its id.
            var read = await stream.ReadAsync(buffer);
            var messageId = buffer[4];
            // ExtendedResponse, resultCode protocolError (2), empty matchedDN and message.
            await stream.WriteAsync(new byte[] { 0x30, 0x0c, 0x02, 0x01, messageId, 0x78, 0x07, 0x0a, 0x01, 0x02, 0x04, 0x00, 0x04, 0x00 });

            // Whatever the client sends after the refusal, until it hangs up.
            while ((read = await stream.ReadAsync(buffer)) > 0)
                received.AddRange(buffer.AsSpan(0, read).ToArray());
        });
        var log = new ListLogger();

        Assert.ThrowsAny<Exception>(() => Factory(port, LdapSecurity.StartTls, trustedCa: null, log).Open());
        await serving.WaitAsync(TimeSpan.FromSeconds(10));

        // Nothing followed the refusal but the goodbye: every message is 30 len 02 01 <id> <op>,
        // and the only operation allowed is an unbind (0x42) — above all, no bind (0x60).
        var operations = new List<byte>();
        for (var i = 0; i + 5 < received.Count && received[i] == 0x30; i += 2 + received[i + 1])
            operations.Add(received[i + 5]);
        Assert.All(operations, operation => Assert.Equal(0x42, operation));
        Assert.Contains(log.Lines, line => line.Contains("did not accept StartTLS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlainLdap_IsWarnedAbout_OncePerDirectory()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = new List<TcpClient>();
        var accepting = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
                accepted.Add(await listener.AcceptTcpClientAsync());
        });
        var log = new ListLogger();

        using (Factory(port, LdapSecurity.None, trustedCa: null, log).Open()) { }
        using (Factory(port, LdapSecurity.None, trustedCa: null, log).Open()) { }
        await accepting;

        var warning = Assert.Single(log.Lines);
        Assert.Contains("plain LDAP", warning);
        Assert.Contains($"{Loopback}:{port}", warning);
        accepted.ForEach(client => client.Dispose());
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static DirectoryCertificateValidator Validator(X509Certificate2[] trusted) =>
        new(new X509Certificate2Collection(trusted), "localhost:636", new ListLogger());

    private static LdapConnectionFactory Factory(int port, LdapSecurity security, string? trustedCa, ListLogger? log = null) =>
        new(Options.Create(new LdapSettings
        {
            Server = Loopback,
            Port = port,
            Security = security,
            TrustedCaCertificatePath = trustedCa
        }), log ?? new ListLogger());

    private string CaFile(X509Certificate2 authority) =>
        WriteFile(".pem", System.Text.Encoding.ASCII.GetBytes(authority.ExportCertificatePem()));

    private string WriteFile(string extension, byte[] content)
    {
        // A new name every time: the loaded CAs are cached by path for the life of the process.
        var path = Path.Combine(Path.GetTempPath(), $"axia-tls-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, content);
        _files.Add(path);
        return path;
    }

    private sealed class ListLogger : ILogger<LdapConnectionFactory>
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyList<string> Lines => [.. _lines];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => _lines.Enqueue(formatter(state, exception));
    }

    /// <summary>Accepts connections and completes a server-side TLS handshake on each.</summary>
    private sealed class TlsServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource<bool> _handshake = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<IDisposable> _open = [];
        private readonly Task _loop;

        private TlsServer(X509Certificate2 certificate)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _loop = Task.Run(() => AcceptAsync(certificate));
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        /// <summary>True once a client has finished a handshake; false if one gave up on it.</summary>
        public Task<bool> HandshakeCompleted => _handshake.Task;

        public static TlsServer Start(X509Certificate2 certificate) => new(certificate);

        private async Task AcceptAsync(X509Certificate2 certificate)
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var tls = new SslStream(client.GetStream());
                lock (_open)
                {
                    _open.Add(client);
                    _open.Add(tls);
                }

                try
                {
                    // Cancelled on dispose: a client that never starts TLS would otherwise leave
                    // this waiting for a handshake forever, and the test hanging instead of failing.
                    await tls.AuthenticateAsServerAsync(
                        new SslServerAuthenticationOptions { ServerCertificate = certificate }, _stop.Token);
                    _handshake.TrySetResult(true);
                }
                catch (Exception)
                {
                    // The client refused the certificate and hung up — which is what some tests
                    // are for — or the test is over.
                    _handshake.TrySetResult(false);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _loop;
            lock (_open)
                _open.ForEach(item => item.Dispose());
            _stop.Dispose();
        }
    }

    private static class Certificates
    {
        public static X509Certificate2 Authority(string name)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            return Reload(created);
        }

        public static X509Certificate2 Server(X509Certificate2 authority, string hostName)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest($"CN={hostName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var names = new SubjectAlternativeNameBuilder();
            if (IPAddress.TryParse(hostName, out var address))
                names.AddIpAddress(address);
            else
                names.AddDnsName(hostName);
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")], false)); // server authentication
            request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(authority, true, false));

            using var issued = request.Create(authority, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(29),
                RandomNumberGenerator.GetBytes(12));
            using var withKey = issued.CopyWithPrivateKey(key);
            return Reload(withKey);
        }

        // Through PKCS#12 so the private key is one the platform's TLS stack will use — an
        // ephemeral key straight from CertificateRequest is refused by SChannel on Windows.
        private static X509Certificate2 Reload(X509Certificate2 certificate) =>
            X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }
}
