var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.MyHelper_App>("myhelper-app");

builder.Build().Run();
