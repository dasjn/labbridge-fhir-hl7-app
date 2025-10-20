using LabBridge.Core.Interfaces;
using LabBridge.Infrastructure.HL7;
using LabBridge.Service;

var builder = Host.CreateApplicationBuilder(args);

// Register HL7 services
builder.Services.AddSingleton<IHL7Parser, NHapiParser>();
builder.Services.AddSingleton<IAckGenerator, AckGenerator>();
builder.Services.AddSingleton<IMllpServer, MllpServer>();

// Register background worker
builder.Services.AddHostedService<MllpListenerWorker>();

var host = builder.Build();
host.Run();
