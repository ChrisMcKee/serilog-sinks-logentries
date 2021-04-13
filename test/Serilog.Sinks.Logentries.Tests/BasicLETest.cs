using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Core;
using Xunit;

namespace Serilog.Sinks.Logentries.Tests
{
    public class BasicLETest
    {
        private string _token = Environment.GetEnvironmentVariable("Token");

        [Fact]
        public void Test()
        {

            var runId = Guid.NewGuid().ToString("N");

            using (var log = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Logentries(_token, region: "eu", batchPostingLimit: 50, period: TimeSpan.FromMilliseconds(100))
                .CreateLogger())
            {
                for (var j = 0; j < 5; j++)
                {
                    var i = 5000;
                    while (i-- > 0)
                    {
                        log.Information("RunId={RunId}, MsgNumber={MsgNumber}", runId, i);
                    }

                    Thread.Sleep(10000);
                }

            }
        }
    }
}
