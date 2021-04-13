using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
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

            using (var log = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Logentries(_token, region: "eu", batchPostingLimit: 1, period: TimeSpan.FromMilliseconds(500))
                .CreateLogger())
            {
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Error("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");
            log.Information("Hello, Serilog!");

            }
        }
    }
}
