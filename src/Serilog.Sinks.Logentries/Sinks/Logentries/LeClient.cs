#region Licence
// Copyright 2014 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

/*
The MIT License

Copyright (c) 2014 Logentries

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE
 */
#endregion

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using Serilog.Debugging;
using System.Threading.Tasks;

namespace Serilog.Sinks.Logentries
{
    class LeClient
    {
        private readonly TimeSpan _tlsAuthenticationTimeout = TimeSpan.FromSeconds(6);

        // Port number for logging on Rapid7 Insight DATA server.
        private const int LeUnsecurePort = 80;

        // Port number for SSL logging on Rapid7 Insight DATA server.
        private const int LeSecurePort = 443;

        public LeClient(bool useTls, string url)
        {
            _useTls = useTls;
            _url = url;
            _tcpPort = !_useTls ? LeUnsecurePort : LeSecurePort;


#if HAS_OSPLAT
            // You can't set socket options *and* connect to an endpoint using a hostname - if
            // keep-alive is enabled, resolve the hostname to an IP
            // See https://github.com/dotnet/corefx/issues/26840
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && !IPAddress.TryParse(url, out var addr))
            {
                addr = Dns.GetHostAddresses(url).First(x => x.AddressFamily == AddressFamily.InterNetwork);
                _url = addr.ToString();
            }
#endif

        }

        private readonly bool _useTls;
        private readonly string _url;
        private readonly int _tcpPort;

        private TcpClient _tcpClient;

        private TcpClient TcpClient => _tcpClient;

        private Stream ActiveStream { get; set; }

        private void SetSocketKeepAliveValues(TcpClient tcpClient, int keepAliveTime = 10_000, int keepAliveInterval = 1000)
        {
            //KeepAliveTime: default value is 2hr
            //KeepAliveInterval: default value is 1s and Detect 5 times

            uint dummy = 0; //length = 4
            byte[] inOptionValues = new byte[System.Runtime.InteropServices.Marshal.SizeOf(dummy) * 3]; //size = length * 3 = 12

            BitConverter.GetBytes((uint)(1)).CopyTo(inOptionValues, 0);
            BitConverter.GetBytes((uint)keepAliveTime).CopyTo(inOptionValues, Marshal.SizeOf(dummy));
            BitConverter.GetBytes((uint)keepAliveInterval).CopyTo(inOptionValues, Marshal.SizeOf(dummy) * 2);
            // of course there are other ways to marshal up this byte array, this is just one way
            // call WSAIoctl via IOControl

            // .net 3.5 type
            tcpClient.Client.IOControl(IOControlCode.KeepAliveValues, inOptionValues, null);
        }

        private bool IsConnected()
        {
            if (TcpClient?.Client == null || !TcpClient.Connected)
                return false;

            var socket = TcpClient.Client;

            // Poll will return true if there is no active connection OR if there is an active
            // connection and there is data waiting to be read
            return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
        }


        public async Task EnsureConnected()
        {
            try
            {
                if (IsConnected())
                {
                    return;
                }

                ////// Recreate the TCP client
                //ActiveStream?.Dispose();
                //TcpClient?.Close();
                _tcpClient = new TcpClient();

                _tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                // Reduce latency to a minimum
                _tcpClient.NoDelay = true;
                TcpClient.ConnectAsync(_url, _tcpPort).Wait();

                try
                {
                    SetSocketKeepAliveValues(TcpClient);
                }
                catch (PlatformNotSupportedException)
                {
                    // .net core on linux does not support modification of that settings at the moment. defaults applied.
                }

                ActiveStream = GetStream(TcpClient.GetStream()).Result;
            }
            catch (Exception ex)
            {
                // Attempt to provide meaningful diagnostic messages for common connection problems
                HandleConnectError(ex);
                throw;
            }
        }

        private async Task<Stream> GetStream(Stream baseStream)
        {
            if (!_useTls)
                return baseStream;

            // Authenticate the server, using the provided callback if required
            var sslStream = new SslStream(baseStream, true);

            // Authenticate the client, using the provided client certificate if required
            // Note: this method takes an X509CertificateCollection, rather than an X509Certificate,
            // but providing the full chain does not actually appear to work for many servers
            //
            // Asynchronous calls do not have a default timeout period like most of their synchronous
            // counterparts do. The AuthenticateAsClientAsync() method initiates the TLS handshake,
            // which requires sending AND receiving data from the server. If the server is just a plain
            // socket listener that doesn't have TLS enabled, then it's possible that this method call
            // will wait forever. For example, if you run the Syslog Watcher server program on Windows,
            // it does not support TLS. However, it will gladly accept the TCP connection and the TLS
            // handshake data and not send anything in response. Therefore, this method will wait forever,
            // giving no indication that anything is wrong. So, we'll implement our own timeout that,
            // when elapsed, will dispose of the underlying base stream, causing the call to the
            // AuthenticateAsClientAsync() method to throw an ObjectDisposedException, breaking it out
            // of the asynchronous wait. We'll use 100 seconds, which is the same as the default timeout
            // for a WebRequest under a similar condition.
            var timeoutCts = new CancellationTokenSource(_tlsAuthenticationTimeout);

            using (timeoutCts)
            using (timeoutCts.Token.Register(() => { sslStream.Dispose(); baseStream.Dispose(); }))
            {
                try
                {
                    // Note that with the .NET 5.0 version of this method and .NET Core 2.1+ of this
                    // method, a cancellation token can be passed directly in as a parameter.
                    await sslStream.AuthenticateAsClientAsync(_url).ConfigureAwait(false);

                    // There is a race condition to this point and when the cancellation token's callback
                    // may be called versus when we're able to dispose of it to prevent the callback.
                }
                catch (ObjectDisposedException)
                {
                    // We'd throw the same exception here as we have below in the race condition check,
                    // so we can just ignore it here for now.
                }
            }

            // To mitigate the above mentioned race condition, we can check the cancellation token here
            // as well and error on the side of caution. If the token has been canceled, then we will not
            // proceed.
            if (timeoutCts.IsCancellationRequested)
            {
                // This may have already been done by the cancellation token's callback, but in case
                // we get here due to the race condition, we need to do it and there is no harm in
                // doing it twice.
                sslStream.Dispose();
                baseStream.Dispose();

                throw new OperationCanceledException("Timeout while performing TLS authentication. Check to make sure the server is configured to handle TLS connections.");
            }

            if (!sslStream.IsAuthenticated)
                throw new AuthenticationException("Unable to authenticate secure syslog server");

            return sslStream;
        }

        /// <summary>
        /// Attempt to provide meaningful diagnostic messages for common connection problems
        /// </summary>
        /// <param name="ex">The exception that occurred during the failed connection attempt</param>
        private void HandleConnectError(Exception ex)
        {
            var prefix = $"[{nameof(LeClient)}]";

            string target = $"{_url}:{(_useTls ? LeSecurePort : LeUnsecurePort)}";

            // Server down, blocked by a firewall, unreachable, or malfunctioning
            if (ex is SocketException socketEx)
            {
                var errorCode = socketEx.SocketErrorCode;

                if (errorCode == SocketError.ConnectionRefused)
                {
                    SelfLog.WriteLine($"{prefix} connection refused to {target} - is the server listening?");
                }
                else if (errorCode == SocketError.TimedOut)
                {
                    SelfLog.WriteLine($"{prefix} timed out connecting to {target} - is a firewall blocking traffic?");
                }
                else
                {
                    SelfLog.WriteLine($"{prefix} unable to connect to {target} - {ex.Message}\n{ex.StackTrace}");
                }
            }
            else if (ex is AuthenticationException)
            {
                // Issue with secure channel negotiation (e.g. protocol mismatch)
                var details = ex.InnerException?.Message ?? ex.Message;
                SelfLog.WriteLine($"{prefix} unable to connect to secure server {target} - {details}\n{ex.StackTrace}");
            }
            else
            {
                SelfLog.WriteLine($"{prefix} unable to connect to {target} - {ex.Message}\n{ex.StackTrace}");
            }

            // Tear down the client
            ActiveStream?.Dispose();
            TcpClient?.Close();
        }

        private static readonly byte LF = 0x0A;
        private static readonly byte NUL = 0x00;

        public async Task WriteAsync(string token, string message)
        {
            var buffer = Encoding.UTF8.GetBytes(token + message + "\n");

            for (int i = 0; i < buffer.Length; i++)
            {
                if (i == buffer.Length - 1)
                {
                    buffer[i] = LF;
                    continue;
                }

                if (buffer[i] == LF)
                {
                    buffer[i] = NUL;
                }

            }

            await ActiveStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            await ActiveStream.FlushAsync().ConfigureAwait(false);
        }

        public async Task FlushAsync()
        {
            if (ActiveStream != null) await ActiveStream.FlushAsync().ConfigureAwait(false);
        }

        public void Close()
        {
            if (TcpClient != null)
            {
                try
                {
                    ((IDisposable)TcpClient).Dispose();
                }
                catch (Exception ex)
                {
                    SelfLog.WriteLine("Exception while closing client: {0}", ex);
                }
                finally
                {
                    _tcpClient = null;
                    ActiveStream = null;
                }
            }
        }
    }
}
