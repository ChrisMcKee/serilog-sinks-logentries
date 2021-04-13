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
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Serilog.Debugging;
using System.Threading.Tasks;

namespace Serilog.Sinks.Logentries
{
    class LeClient
    {
        // Port number for logging on Rapid7 Insight DATA server.
        private const int LeUnsecurePort = 80;

        // Port number for SSL logging on Rapid7 Insight DATA server.
        private const int LeSecurePort = 443;

        public LeClient(bool useSsl, string url)
        {
            _useSsl = useSsl;
            _url = url;
            _tcpPort = !_useSsl ? LeUnsecurePort : LeSecurePort;
        }

        private readonly bool _useSsl;
        private readonly string _url;
        private readonly int _tcpPort;

        TcpClient _mClient;
        Stream _mStream;
        SslStream _mSslStream;

        Stream ActiveStream => _useSsl ? _mSslStream : _mStream;

        private void SetSocketKeepAliveValues(TcpClient tcpClient, int keepAliveTime, int keepAliveInterval)
        {
            //KeepAliveTime: default value is 2hr
            //KeepAliveInterval: default value is 1s and Detect 5 times

            uint dummy = 0; //length = 4
            byte[] inOptionValues = new byte[System.Runtime.InteropServices.Marshal.SizeOf(dummy) * 3]; //size = length * 3 = 12
            bool onOff = true;

            BitConverter.GetBytes((uint)(onOff ? 1 : 0)).CopyTo(inOptionValues, 0);
            BitConverter.GetBytes((uint)keepAliveTime).CopyTo(inOptionValues, Marshal.SizeOf(dummy));
            BitConverter.GetBytes((uint)keepAliveInterval).CopyTo(inOptionValues, Marshal.SizeOf(dummy) * 2);
            // of course there are other ways to marshal up this byte array, this is just one way
            // call WSAIoctl via IOControl

            // .net 3.5 type
            tcpClient.Client.IOControl(IOControlCode.KeepAliveValues, inOptionValues, null);
        }

        public async Task ConnectAsync()
        {

            if (_mClient != null && _mClient.Connected) return;

            _mClient = new TcpClient()
            {
                NoDelay = true
            };

            await _mClient.ConnectAsync(_url, _tcpPort).ConfigureAwait(true);

            _mClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            try
            {
                // set timeouts to 10 seconds idle before keepalive, 1 second between repeats,
                SetSocketKeepAliveValues(_mClient, 10 * 1000, 1000);
            }
            catch (PlatformNotSupportedException)
            {
                // .net core on linux does not support modification of that settings at the moment. defaults applied.
            }

            _mStream = _mClient.GetStream();

            if (_useSsl)
            {
                _mSslStream = new SslStream(_mStream);
                await _mSslStream.AuthenticateAsClientAsync(_url).ConfigureAwait(false);
            }

        }

        public async Task WriteAsync(byte[] buffer, int offset, int count)
        {
            await ActiveStream.WriteAsync(buffer, offset, count).ConfigureAwait(true);
            await ActiveStream.FlushAsync();
        }

        public async Task FlushAsync()
        {
            await ActiveStream.FlushAsync().ConfigureAwait(false);
        }

        public void Close()
        {
            if (_mClient != null)
            {
                try
                {
                    ((IDisposable)_mClient).Dispose();
                }
                catch (Exception ex)
                {
                    SelfLog.WriteLine("Exception while closing client: {0}", ex);
                }
                finally
                {
                    _mClient = null;
                    _mStream = null;
                    _mSslStream = null;
                }
            }
        }
    }
}
