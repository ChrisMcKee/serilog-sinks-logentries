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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Sinks.PeriodicBatching;
using System.Threading.Tasks;
using Serilog.Debugging;

namespace Serilog.Sinks.Logentries
{
    /// <summary>
    /// Writes log events to the Logentries.com service.
    /// </summary>
    public class LogentriesSink : PeriodicBatchingSink
    {
        readonly string _token;
        readonly bool _useSsl;
        private readonly string _url;
        LeClient _client;
        readonly ITextFormatter _textFormatter;

        /// <summary>
        /// UTF-8 output character set.
        /// </summary>
        protected static readonly UTF8Encoding Utf8 = new UTF8Encoding();

        /// <summary>
        /// A reasonable default for the number of events posted in
        /// each batch.
        /// </summary>
        public const int DefaultBatchPostingLimit = 50;

        /// <summary>
        /// A reasonable default time to wait between checking for event batches.
        /// </summary>
        public static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(2);

        private readonly string _region;

        /// <summary>
        /// Construct a sink that sends logs to the specified Logentries log using a <see cref="MessageTemplateTextFormatter"/> to format
        /// the logs as simple display messages.
        /// </summary>
        /// <param name="outputTemplate">A message template describing the format used to write to the sink.</param>
        /// <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>
        /// <param name="token">The input key as found on the Logentries website.</param>
        /// <param name="useSsl">Indicates if you want to use SSL or not.</param>
        /// <param name="region">Region option, e.g: us, eu.</param>
        /// <param name="batchPostingLimit">The maximum number of events to post in a single batch.</param>
        /// <param name="period">The time to wait between checking for event batches.</param>
        /// <param name="url">Url to logentries</param>
        public LogentriesSink(string outputTemplate, IFormatProvider formatProvider, string token, bool useSsl,
            string region, int batchPostingLimit, TimeSpan period, string url)
            : this(new MessageTemplateTextFormatter(outputTemplate, formatProvider), token, useSsl, region, batchPostingLimit, period, url)
        {
        }

        /// <summary>
        /// Construct a sink that sends logs to the specified Logentries log using a provided <see cref="ITextFormatter"/>.
        /// </summary>
        /// <param name="textFormatter">Used to format the logs sent to Logentries.</param>
        /// <param name="token">The input key as found on the Logentries website.</param>
        /// <param name="useSsl">Indicates if you want to use SSL or not.</param>
        /// <param name="region">Region option, e.g: us, eu.</param>
        /// <param name="batchPostingLimit">The maximum number of events to post in a single batch.</param>
        /// <param name="period">The time to wait between checking for event batches.</param>
        /// <param name="url"></param>
        public LogentriesSink(ITextFormatter textFormatter, string token, bool useSsl, string region,
            int batchPostingLimit, TimeSpan period, string url)
             : base(batchPostingLimit, period)
        {
            _textFormatter = textFormatter ?? throw new ArgumentNullException(nameof(textFormatter));
            _token = token;
            _useSsl = useSsl;
            _region = region;
            _url = url;
        }

        /// <summary>
        /// Emit a batch of log events, running to completion synchronously.
        /// </summary>
        /// <param name="events">The events to emit.</param>
        /// <remarks>
        /// Override either <see cref="M:Serilog.Sinks.PeriodicBatching.PeriodicBatchingSink.EmitBatchAsync(System.Collections.Generic.IEnumerable{Serilog.Events.LogEvent})" /> or <see cref="M:Serilog.Sinks.PeriodicBatching.PeriodicBatchingSink.EmitBatchAsync(System.Collections.Generic.IEnumerable{Serilog.Events.LogEvent})" />,
        /// not both.
        /// </remarks>
        protected override async Task EmitBatchAsync(IEnumerable<LogEvent> events)
        {

            if (_client == null)
            {
                _client = new LeClient(_useSsl, _url);
            }

            // Throws if not connected and unable to connect (PeriodicBatchingSink will handle retries)
            await _client.EnsureConnected();

            foreach (var logEvent in events)
            {

                var sw = new StringWriter();
                _textFormatter.Format(logEvent, sw);

                var renderedString = sw.ToString();

                try
                {
                    await _client.WriteAsync(_token, renderedString);
                }
                catch (SocketException ex)
                {
                    // Log and rethrow (PeriodicBatchingSink will handle retries)
                    SelfLog.WriteLine($"[{nameof(LogentriesSink)}] error while sending log event to syslog {this._url}:{(this._useSsl ? "443" : "80")} - {ex.Message}\n{ex.StackTrace}");
                    throw;
                }
            }

            await _client.FlushAsync();
        }

        /// <summary>
        /// Dispose the connection.
        /// </summary>
        /// <param name="disposing"></param>
        protected override void Dispose(bool disposing)
        {
            if (_client != null)
            {
                _client.FlushAsync().Wait();
                _client.Close();
            }
            base.Dispose(disposing);
        }

    }
}
