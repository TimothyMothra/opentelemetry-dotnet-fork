// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#if !NETFRAMEWORK
extern alias OpenTelemetryProtocol;

using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Benchmarks.Helper;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Internal;
using OpenTelemetry.Tests;
using OpenTelemetryProtocol::OpenTelemetry.Exporter;
using OtlpCollector = OpenTelemetryProtocol::OpenTelemetry.Proto.Collector.Trace.V1;

/*
BenchmarkDotNet v0.13.12, Windows 11 (10.0.26100.2314) (Hyper-V)
AMD EPYC 7763, 1 CPU, 16 logical and 8 physical cores
.NET SDK 9.0.100
  [Host]     : .NET 8.0.11 (8.0.1124.51707), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.11 (8.0.1124.51707), X64 RyuJIT AVX2


| Method                        | Mean     | Error   | StdDev  | Gen0   | Gen1   | Allocated |
|------------------------------ |---------:|--------:|--------:|-------:|-------:|----------:|
| OtlpTraceExporter_Http        | 133.9 us | 2.19 us | 2.05 us | 0.4883 | 0.2441 |   9.52 KB |
| OtlpTraceExporter_Http_Custom | 120.7 us | 1.53 us | 1.43 us | 0.2441 |      - |      5 KB |
| OtlpTraceExporter_Grpc        | 189.6 us | 3.74 us | 5.36 us |      - |      - |   8.98 KB |
| OtlpTraceExporter_Grpc_Custom | 168.7 us | 3.21 us | 3.15 us |      - |      - |   6.56 KB |



BenchmarkDotNet v0.13.12, Windows 11 (10.0.26100.2314)
Snapdragon X1E78100, 1 CPU, 12 logical and 12 physical cores
.NET SDK 9.0.100
  [Host]     : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD
  DefaultJob : .NET 8.0.11 (8.0.1124.51707), Arm64 RyuJIT AdvSIMD


| Method                        | Mean     | Error    | StdDev   | Gen0   | Gen1   | Allocated |
|------------------------------ |---------:|---------:|---------:|-------:|-------:|----------:|
| OtlpTraceExporter_Http        | 43.34 us | 0.537 us | 0.820 us | 2.4414 | 2.4414 |   9.52 KB |
| OtlpTraceExporter_Http_Custom | 41.75 us | 0.688 us | 0.819 us | 1.2207 | 1.0986 |      5 KB |
| OtlpTraceExporter_Grpc        | 58.74 us | 0.381 us | 0.318 us | 2.1973 |      - |   8.99 KB |
| OtlpTraceExporter_Grpc_Custom | 55.96 us | 0.286 us | 0.253 us | 1.5869 |      - |   6.55 KB |
*/

namespace Benchmarks.Exporter;

public class OtlpTraceExporterBenchmarks
{
    private OtlpTraceExporter? exporter;
    private ProtobufOtlpTraceExporter? newExporter;
    private Activity? activity;
    private CircularBuffer<Activity>? activityBatch;

    private IHost? host;
    private IDisposable? server;
    private string? serverHost;
    private int serverPort;

    [GlobalSetup(Target = nameof(OtlpTraceExporter_Grpc))]
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
                      endpoints.MapGrpcService<MockTraceService>();
                  });
              }))
          .Start();

        var options = new OtlpExporterOptions();
        this.exporter = new OtlpTraceExporter(options);

        this.activity = ActivityHelper.CreateTestActivity();
        this.activityBatch = new CircularBuffer<Activity>(1);
        this.activityBatch.Add(this.activity);
    }

    [GlobalSetup(Target = nameof(OtlpTraceExporter_Grpc_Custom))]
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
                      endpoints.MapGrpcService<MockTraceService>();
                  });
              }))
          .Start();

        var options = new OtlpExporterOptions();
        this.newExporter = new ProtobufOtlpTraceExporter(options);

        this.activity = ActivityHelper.CreateTestActivity();
        this.activityBatch = new CircularBuffer<Activity>(1);
        this.activityBatch.Add(this.activity);
    }

    [GlobalSetup(Target = nameof(OtlpTraceExporter_Http))]
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
        this.exporter = new OtlpTraceExporter(options);

        this.activity = ActivityHelper.CreateTestActivity();
        this.activityBatch = new CircularBuffer<Activity>(1);
        this.activityBatch.Add(this.activity);
    }

    [GlobalSetup(Target = nameof(OtlpTraceExporter_Http_Custom))]
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
        this.newExporter = new ProtobufOtlpTraceExporter(options);

        this.activity = ActivityHelper.CreateTestActivity();
        this.activityBatch = new CircularBuffer<Activity>(1);
        this.activityBatch.Add(this.activity);
    }

    [GlobalCleanup(Target = nameof(OtlpTraceExporter_Grpc))]
    public void GlobalCleanupGrpc()
    {
        this.exporter?.Shutdown();
        this.exporter?.Dispose();
        this.activity?.Dispose();
        this.host?.Dispose();
    }

    [GlobalCleanup(Target = nameof(OtlpTraceExporter_Grpc_Custom))]
    public void GlobalCleanupGrpcCustom()
    {
        this.newExporter?.Shutdown();
        this.newExporter?.Dispose();
        this.activity?.Dispose();
        this.host?.Dispose();
    }

    [GlobalCleanup(Target = nameof(OtlpTraceExporter_Http))]
    public void GlobalCleanupHttp()
    {
        this.exporter?.Shutdown();
        this.exporter?.Dispose();
        this.server?.Dispose();
        this.activity?.Dispose();
    }

    [GlobalCleanup(Target = nameof(OtlpTraceExporter_Http_Custom))]
    public void GlobalCleanupHttpCustom()
    {
        this.newExporter?.Shutdown();
        this.newExporter?.Dispose();
        this.server?.Dispose();
        this.activity?.Dispose();
    }

    [Benchmark]
    public void OtlpTraceExporter_Http()
    {
        this.exporter!.Export(new Batch<Activity>(this.activityBatch!, 1));
    }

    [Benchmark]
    public void OtlpTraceExporter_Http_Custom()
    {
        this.newExporter!.Export(new Batch<Activity>(this.activityBatch!, 1));
    }

    [Benchmark]
    public void OtlpTraceExporter_Grpc()
    {
        this.exporter!.Export(new Batch<Activity>(this.activityBatch!, 1));
    }

    [Benchmark]
    public void OtlpTraceExporter_Grpc_Custom()
    {
        this.newExporter!.Export(new Batch<Activity>(this.activityBatch!, 1));
    }

    private sealed class MockTraceService : OtlpCollector.TraceService.TraceServiceBase
    {
        private static OtlpCollector.ExportTraceServiceResponse response = new OtlpCollector.ExportTraceServiceResponse();

        public override Task<OtlpCollector.ExportTraceServiceResponse> Export(OtlpCollector.ExportTraceServiceRequest request, ServerCallContext context)
        {
            return Task.FromResult(response);
        }
    }
}
#endif
