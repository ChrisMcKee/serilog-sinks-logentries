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
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.Logentries;

namespace Serilog
{
    /// <summary>
    /// Adds the WriteTo.Logentries() extension method to <see cref="LoggerConfiguration"/>.
    /// </summary>
    public static class LoggerConfigurationLogentriesExtensions
    {
        const string DefaultLogentriesOutputTemplate = "{Timestamp:G} [{Level}] {Message}{NewLine}{Exception}";

        // Logentries API server address.
        const string LeDataUrl = "{0}.data.logs.insight.rapid7.com";
        private static string _serverAddr;

        /// <summary>
        /// Adds a sink that writes log events to the Logentries.com webservice.
        /// Create a token TCP input for this on the logentries website.
        /// </summary>
        /// <param name="loggerConfiguration">The logger configuration.</param>
        /// <param name="token">The token as found on the Logentries.com website.</param>
        /// <param name="region">Region option, e.g: us, eu.</param>
        /// <param name="restrictedToMinimumLevel">The minimum log event level required in order to write an event to the sink.</param>
        /// <param name="outputTemplate">A message template describing the format used to write to the sink.
        /// the default is "{Timestamp:G} [{Level}] {Message}{NewLine}{Exception}".</param>
        /// <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>
        /// <param name="useSsl">Specify if the connection needs to be secured.</param>
        /// <param name="batchPostingLimit">The maximum number of events to post in a single batch.</param>
        /// <param name="period">The time to wait between checking for event batches.</param>
        /// <param name="url">Url to logentries; this default to eu.data.logs.insight.rapid7.com if region isn't set</param>
        /// <returns>Logger configuration, allowing configuration to continue.</returns>
        /// <exception cref="ArgumentNullException">A required parameter is null.</exception>
        public static LoggerConfiguration Logentries(
            this LoggerSinkConfiguration loggerConfiguration,
             string token, string region = "eu", bool useSsl = true,
            int batchPostingLimit = LogentriesSink.DefaultBatchPostingLimit,
            TimeSpan? period = null,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            string outputTemplate = DefaultLogentriesOutputTemplate,
            IFormatProvider formatProvider = null,
            string url = "")
        {
            if (loggerConfiguration == null) throw new ArgumentNullException(nameof(loggerConfiguration));

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (region != "eu" && region != "us")
            {
                throw new ArgumentNullException(nameof(region), "Region must be us or eu");
            }

            _serverAddr = string.IsNullOrWhiteSpace(url) ? string.Format(LeDataUrl, region) : url;

            var defaultedPeriod = period ?? LogentriesSink.DefaultPeriod;

            return loggerConfiguration.Sink(
                new LogentriesSink(outputTemplate, formatProvider, token, useSsl, region, batchPostingLimit, defaultedPeriod, _serverAddr),
                restrictedToMinimumLevel);
        }

        /// <summary>
        /// Adds a sink that writes log events to the Logentries.com webservice.
        /// Create a token TCP input for this on the logentries website.
        /// </summary>
        /// <param name="loggerConfiguration">The logger configuration.</param>
        /// <param name="token">The token as found on the Logentries.com website.</param>
        /// <param name="region">Region option, e.g: us, eu.</param>
        /// <param name="restrictedToMinimumLevel">The minimum log event level required in order to write an event to the sink.</param>
        /// <param name="textFormatter">Used to format the logs sent to Logentries.</param>
        /// <param name="useSsl">Specify if the connection needs to be secured.</param>
        /// <param name="batchPostingLimit">The maximum number of events to post in a single batch.</param>
        /// <param name="period">The time to wait between checking for event batches.</param>
        /// <param name="url">Url to logentries; this default to eu.data.logs.insight.rapid7.com if region isn't set</param>
        /// <returns>Logger configuration, allowing configuration to continue.</returns>
        /// <exception cref="ArgumentNullException">A required parameter is null.</exception>
        public static LoggerConfiguration Logentries(
            this LoggerSinkConfiguration loggerConfiguration,
             string token,
            ITextFormatter textFormatter,
            string region = "eu",
            bool useSsl = true,
            int batchPostingLimit = LogentriesSink.DefaultBatchPostingLimit,
            TimeSpan? period = null,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            string url = "")
        {
            if (loggerConfiguration == null) throw new ArgumentNullException(nameof(loggerConfiguration));

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (textFormatter == null)
            {
                throw new ArgumentNullException(nameof(textFormatter));
            }

            if (region != "eu" && region != "us")
            {
                throw new ArgumentNullException(nameof(region), "Region must be us or eu");
            }

            _serverAddr = string.IsNullOrWhiteSpace(url) ? string.Format(LeDataUrl, region) : url;

            var defaultedPeriod = period ?? LogentriesSink.DefaultPeriod;

            return loggerConfiguration.Sink(
                new LogentriesSink(textFormatter, token, useSsl, region, batchPostingLimit, defaultedPeriod, _serverAddr),
                restrictedToMinimumLevel);
        }
    }
}
