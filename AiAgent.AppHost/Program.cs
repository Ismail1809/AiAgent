var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.AiAgent_ApiService>("apiservice");

builder.AddProject<Projects.AiAgent_OpenAiService>("openaiservice");

builder.AddProject<Projects.AiAgent_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService);

builder.AddProject<Projects.AiAgent_GoogleCalService>("aiagent-googlecalservice");

builder.AddProject<Projects.AiAgent_OutLookService>("aiagent-outlookservice");

builder.Build().Run();
