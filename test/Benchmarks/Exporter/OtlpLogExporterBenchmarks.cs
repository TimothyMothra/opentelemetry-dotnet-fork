// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#if !NETFRAMEWORK
extern alias OpenTelemetryProtocol;

using BenchmarkDotNet.Attributes;
using Benchmarks.Helper;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Internal;
using OpenTelemetry.Logs;
using OpenTelemetry.Tests;
using OpenTelemetryProtocol::OpenTelemetry.Exporter;
using OtlpCollector = OpenTelemetryProtocol::OpenTelemetry.Proto.Collector.Logs.V1;

/*
BenchmarkDotNet v0.13.12, Windows 11 (10.0.26100.2314) (Hyper-V)
AMD EPYC 7763, 1 CPU, 16 logical and 8 physical cores
.NET SDK 9.0.100
  [Host]     : .NET 8.0.11 (8.0.1124.51707), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.11 (8.0.1124.51707), X64 RyuJIT AVX2


| Method                      | Mean     | Error   | StdDev  | Gen0   | Gen1   | Allocated |
|---------------------------- |---------:|--------:|--------:|-------:|-------:|----------:|
| OtlpLogExporter_Http        | 139.2 us | 1.78 us | 1.67 us | 0.4883 | 0.2441 |   9.52 KB |
| OtlpLogExporter_Http_Custom | 123.8 us | 1.63 us | 1.52 us | 0.2441 |      - |      5 KB |
| OtlpLogExporter_Grpc        | 199.2 us | 3.97 us | 8.12 us |      - |      - |   8.97 KB |
| OtlpLogExporter_Grpc_Custom | 153.2 us | 2.91 us | 4.95 us |      - |      - |   5.08 KB |


BenchmarkDotNet v0.13.12, Windows 11 (10.0.26100.2314)
Snapdragon X1E78100, 1 CPU, 12 logical and 12 physical cores
.NET SDK 9.0.100
  [Host]     : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD
  DefaultJob : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD


| Method                      | Mean     | Error    | StdDev   | Gen0   | Gen1   | Allocated |
|---------------------------- |---------:|---------:|---------:|-------:|-------:|----------:|
| OtlpLogExporter_Http        | 44.86 us | 0.892 us | 1.160 us | 2.4414 | 2.3193 |   9.52 KB |
| OtlpLogExporter_Http_Custom | 43.59 us | 0.831 us | 1.411 us | 1.2207 | 1.0986 |      5 KB |
| OtlpLogExporter_Grpc        | 59.72 us | 1.182 us | 1.452 us | 2.1973 |      - |   8.98 KB |
| OtlpLogExporter_Grpc_Custom | 54.27 us | 0.407 us | 0.340 us | 1.2207 |      - |   5.08 KB |
*/

namespace Benchmarks.Exporter;

public class OtlpLogExporterBenchmarks
{
    private OtlpLogExporter? exporter;
    private ProtobufOtlpLogExporter? newExporter;
    private LogRecord? logRecord;
    private CircularBuffer<LogRecord>? logRecordBatch;

    private IHost? host;
    private IDisposable? server;
    private string? serverHost;
    private int serverPort;

    [GlobalSetup(Target = nameof(OtlpLogExporter_Grpc))]
    public void GlobalSetupGrpc()
    {
        this.host = new HostBuilder()
          .ConfigureWebHostDefaults(webBuilder => webBuilder
               .ConfigureKestrel(options =>
               {
                   options.ListenLocalhost(4317, listenOptions => listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
               })
              .ConfigureServices(services =>
              {
                  services.AddGrpc();
              })
              .Configure(app =>
              {
                  app.UseRouting();
                  app.UseEndpoints(endpoints =>
                  {
                      endpoints.MapGrpcService<MockLogService>();
                  });
              }))
          .Start();

        var options = new OtlpExporterOptions();
        this.exporter = new OtlpLogExporter(options);

        this.logRecord = LogRecordHelper.CreateTestLogRecord();
        this.logRecordBatch = new CircularBuffer<LogRecord>(1);
        this.logRecordBatch.Add(this.logRecord);
    }

    [GlobalSetup(Target = nameof(OtlpLogExporter_Grpc_Custom))]
    public void GlobalSetupGrpcCustom()
    {
        this.host = new HostBuilder()
          .ConfigureWebHostDefaults(webBuilder => webBuilder
               .ConfigureKestrel(options =>
               {
                   options.ListenLocalhost(4317, listenOptions => listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
               })
              .ConfigureServices(services =>
              {
                  services.AddGrpc();
              })
              .Configure(app =>
              {
                  app.UseRouting();
                  app.UseEndpoints(endpoints =>
                  {
                      endpoints.MapGrpcService<MockLogService>();
                  });
              }))
          .Start();

        var options = new OtlpExporterOptions();
        this.newExporter = new ProtobufOtlpLogExporter(options);

        this.logRecord = LogRecordHelper.CreateTestLogRecord();
        this.logRecordBatch = new CircularBuffer<LogRecord>(1);
        this.logRecordBatch.Add(this.logRecord);
    }

    [GlobalSetup(Target = nameof(OtlpLogExporter_Http))]
    public void GlobalSetupHttp()
    {
        this.server = TestHttpServer.RunServer(
            (ctx) =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.OutputStream.Close();
            },
            out this.serverHost,
            out this.serverPort);

        var options = new OtlpExporterOptions
        {
            Endpoint = new Uri($"http://{this.serverHost}:{this.serverPort}"),
            Protocol = OtlpExportProtocol.HttpProtobuf,
        };
        this.exporter = new OtlpLogExporter(options);

        this.logRecord = LogRecordHelper.CreateTestLogRecord();
        this.logRecordBatch = new CircularBuffer<LogRecord>(1);
        this.logRecordBatch.Add(this.logRecord);
    }

    [GlobalSetup(Target = nameof(OtlpLogExporter_Http_Custom))]
    public void GlobalSetupHttpCustom()
    {
        this.server = TestHttpServer.RunServer(
            (ctx) =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.OutputStream.Close();
            },
            out this.serverHost,
            out this.serverPort);

        var options = new OtlpExporterOptions
        {
            Endpoint = new Uri($"http://{this.serverHost}:{this.serverPort}"),
            Protocol = OtlpExportProtocol.HttpProtobuf,
        };
        this.newExporter = new ProtobufOtlpLogExporter(options);

        this.logRecord = LogRecordHelper.CreateTestLogRecord();
        this.logRecordBatch = new CircularBuffer<LogRecord>(1);
        this.logRecordBatch.Add(this.logRecord);
    }

    [GlobalCleanup(Target = nameof(OtlpLogExporter_Grpc))]
    public void GlobalCleanupGrpc()
    {
        this.exporter?.Shutdown();
        this.exporter?.Dispose();
        this.host?.Dispose();
    }

    [GlobalCleanup(Target = nameof(OtlpLogExporter_Grpc_Custom))]
    public void GlobalCleanupGrpcCustom()
    {
        this.newExporter?.Shutdown();
        this.newExporter?.Dispose();
        this.host?.Dispose();
    }

    [GlobalCleanup(Target = nameof(OtlpLogExporter_Http))]
    public void GlobalCleanupHttp()
    {
        this.exporter?.Shutdown();
        this.exporter?.Dispose();
        this.server?.Dispose();
    }

    [GlobalCleanup(Target = nameof(OtlpLogExporter_Http_Custom))]
    public void GlobalCleanupHttpCustom()
    {
        this.newExporter?.Shutdown();
        this.newExporter?.Dispose();
        this.server?.Dispose();
    }

    [Benchmark]
    public void OtlpLogExporter_Http()
    {
        this.exporter!.Export(new Batch<LogRecord>(this.logRecordBatch!, 1));
    }

    [Benchmark]
    public void OtlpLogExporter_Http_Custom()
    {
        this.newExporter!.Export(new Batch<LogRecord>(this.logRecordBatch!, 1));
    }

    [Benchmark]
    public void OtlpLogExporter_Grpc()
    {
        this.exporter!.Export(new Batch<LogRecord>(this.logRecordBatch!, 1));
    }

    [Benchmark]
    public void OtlpLogExporter_Grpc_Custom()
    {
        this.newExporter!.Export(new Batch<LogRecord>(this.logRecordBatch!, 1));
    }

    private sealed class MockLogService : OtlpCollector.LogsService.LogsServiceBase
    {
        private static OtlpCollector.ExportLogsServiceResponse response = new OtlpCollector.ExportLogsServiceResponse();

        public override Task<OtlpCollector.ExportLogsServiceResponse> Export(OtlpCollector.ExportLogsServiceRequest request, ServerCallContext context)
        {
            return Task.FromResult(response);
        }
    }
}
#endif
