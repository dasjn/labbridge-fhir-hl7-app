using LabBridge.Core.Interfaces;
using LabBridge.Infrastructure.FHIR;
using LabBridge.Infrastructure.HL7;
using LabBridge.Infrastructure.Messaging;
using LabBridge.Service;

var builder = Host.CreateApplicationBuilder(args);

// Register HL7 services
builder.Services.AddSingleton<IHL7Parser, NHapiParser>();
builder.Services.AddSingleton<IAckGenerator, AckGenerator>();
builder.Services.AddSingleton<IMllpServer, MllpServer>();

// Register FHIR services
builder.Services.AddSingleton<IHL7ToFhirTransformer, FhirTransformer>();

// Register messaging services
builder.Services.AddSingleton<IMessageQueue, RabbitMqQueue>();

// Register background workers
builder.Services.AddHostedService<MllpListenerWorker>();
builder.Services.AddHostedService<MessageProcessorWorker>();

var host = builder.Build();
host.Run();
