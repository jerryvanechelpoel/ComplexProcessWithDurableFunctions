using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace Carfactory.Functions;

public static class FunctionNames
{
    public const string OrchestratorName = nameof(CarFactoryFunction);
    public const string TriggerInstanceName = $"{OrchestratorName}_RunInstance";
    public const string TriggerSingletonName = $"{OrchestratorName}_RunSingleton";
    public const string KillSwitchName = $"{OrchestratorName}_KillSwitch";

    public const string RetrieveModelSpecsActivityName = $"{OrchestratorName}_RetrieveModelSpecs";
    public const string CreateChassisActivityName = $"{OrchestratorName}_CreateChassis";
    public const string SprayPaintActivityName = $"{OrchestratorName}_SprayPaint";
    public const string InstallWindowsActivityName = $"{OrchestratorName}_InstallWindows";
    public const string InstallInteriorActivityName = $"{OrchestratorName}_InstallInterior";
    public const string AddWheelsActivityName = $"{OrchestratorName}_AddWheels";
}

public static class CarFactoryFunction
{
    #region Trigger instance orchestration

    [Function(FunctionNames.TriggerInstanceName)]
    public static async Task<HttpResponseData> RunInstanceAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req,
        [DurableClient] DurableTaskClient orchestrationClient,
        FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(FunctionNames.TriggerInstanceName);
        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        CarRequest request = JsonSerializer.Deserialize<CarRequest>(requestBody);

        string instanceId = await orchestrationClient.ScheduleNewOrchestrationInstanceAsync(FunctionNames.OrchestratorName, request);

        logger.LogWarning("Starting orchestration with ID = '{instanceId}'.", instanceId);

        return await orchestrationClient.CreateCheckStatusResponseAsync(req, instanceId);
    }

    #endregion

    #region Trigger singleton orchestration

    private const string InstanceId = "3F9CAE52-89ED-48CC-B7D6-4A317233F76B";

    [Function(FunctionNames.TriggerSingletonName)]
    public static async Task<HttpResponseData> RunSingletonAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req,
                                                         [DurableClient] DurableTaskClient orchestrationClient,
                                                         FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(FunctionNames.TriggerInstanceName);

        OrchestrationMetadata orchestrationData = await orchestrationClient.GetInstanceAsync(InstanceId);

        if (orchestrationData?.RuntimeStatus is null or OrchestrationRuntimeStatus.Completed
                                                    or OrchestrationRuntimeStatus.Failed
                                                    or OrchestrationRuntimeStatus.Terminated)
        {

            logger.LogWarning("Starting orchestration with ID = '{instanceId}'.", InstanceId);
            CarRequest request = JsonSerializer.Deserialize<CarRequest>(req.Body);

            StartOrchestrationOptions options = new(InstanceId);
            await orchestrationClient.ScheduleNewOrchestrationInstanceAsync(FunctionNames.OrchestratorName, request, options);

            return await orchestrationClient.CreateCheckStatusResponseAsync(req, InstanceId);
        }

        logger.LogWarning("Orchestration is currently running.");
        return req.CreateResponse(HttpStatusCode.NotModified);
    }

    [Function(FunctionNames.KillSwitchName)]
    public static async Task<HttpResponseData> RunKillSwitchAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req,
                                                         [DurableClient] DurableTaskClient orchestrationClient,
                                                         FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(FunctionNames.TriggerInstanceName);

        OrchestrationMetadata orchestrationData = await orchestrationClient.GetInstanceAsync(InstanceId);

        if (orchestrationData?.RuntimeStatus is null or OrchestrationRuntimeStatus.Pending
                                                     or OrchestrationRuntimeStatus.Running)
        {
            logger.LogWarning("Orchestration with ID = '{instanceId}' is being killed.", InstanceId);
            await orchestrationClient.TerminateInstanceAsync(InstanceId, "Kill switch activated.");
            return req.CreateResponse(HttpStatusCode.OK);
        }

        logger.LogWarning("Orchestration with ID = '{instanceId}' is not running.", InstanceId);
        return req.CreateResponse(HttpStatusCode.NotModified);
    }

    #endregion

    #region Orchestrator

    [Function(nameof(CarFactoryFunction))]
    public static async Task RunOrchestrator([OrchestrationTrigger] TaskOrchestrationContext context, CarRequest carRequest)
    {
        ILogger logger = context.CreateReplaySafeLogger(nameof(CarFactoryFunction));
        logger.LogWarning("Starting car manufactoring.");

        ModelSpecs specs = await context.CallActivityAsync<ModelSpecs>(FunctionNames.RetrieveModelSpecsActivityName, carRequest);

        AssemblyInput input = new() { Request = carRequest, Specs = specs };
        input.Vehicle = await context.CallActivityAsync<Vehicle>(FunctionNames.CreateChassisActivityName, input);

        await context.CallActivityAsync(FunctionNames.SprayPaintActivityName, input);

        var tasks = new Task[specs.WindowAmount];

        for (int i = 0; i < specs.WindowAmount; i++)
        {
            tasks[i] = context.CallActivityAsync(FunctionNames.InstallWindowsActivityName, input);
        }

        logger.LogWarning("Installing windows");
        await Task.WhenAll(tasks);
        logger.LogWarning("Windows installed");

        await context.CallActivityAsync(FunctionNames.InstallInteriorActivityName, input);

        tasks = new Task[specs.WheelAmount];

        for (int i = 0; i < specs.WheelAmount; i++)
        {
            tasks[i] = context.CallActivityAsync(FunctionNames.AddWheelsActivityName, input);
        }

        logger.LogWarning("Installing wheels");
        await Task.WhenAll(tasks);
        logger.LogWarning("Wheels installed");

        logger.LogWarning("{Model} with chassis {ChassisNumber} has been manufactored!", input.Vehicle.ModelName, input.Vehicle.Chassisnumber);
    }

    #endregion

    #region Activities

    [Function(FunctionNames.RetrieveModelSpecsActivityName)]
    public static Task<ModelSpecs> RetrieveModelSpecsAsync([ActivityTrigger] CarRequest input, FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(FunctionNames.RetrieveModelSpecsActivityName);

        logger.LogWarning("Retrieving model specs for {ModelName}.", input.ModelName);

        return input.ModelName switch
        {
            "Bumblebee" => Task.FromResult(new ModelSpecs { WheelAmount = 4, WindowAmount = 6 }),
            "Optimus" => Task.FromResult(new ModelSpecs { WheelAmount = 6, WindowAmount = 3 }),
            "Ironhide" => Task.FromResult(new ModelSpecs { WheelAmount = 6, WindowAmount = 5 }),
            _ => Task.FromResult(new ModelSpecs { WheelAmount = 4, WindowAmount = 8 }),
        };
    }

    [Function(FunctionNames.CreateChassisActivityName)]
    public static Task<Vehicle> CreateChassisAsync([ActivityTrigger] AssemblyInput input, FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(FunctionNames.CreateChassisActivityName);

        logger.LogWarning("Creating chassis for {ModelName}.", input.Request.ModelName);

        return Task.FromResult(new Vehicle { ModelName = input.Request.ModelName, Chassisnumber = Guid.NewGuid() });
    }

    [Function(FunctionNames.SprayPaintActivityName)]
    public static Task SprayPaintAsync([ActivityTrigger] AssemblyInput input, FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(FunctionNames.SprayPaintActivityName);

        logger.LogWarning("Spray painting {ModelName} in {Color}.", input.Vehicle.ModelName, input.Request.Color);
        input.Vehicle.Color = input.Request.Color;

        return Task.CompletedTask;
    }

    [Function(FunctionNames.InstallWindowsActivityName)]
    public static Task InstallWindowsAsync([ActivityTrigger] AssemblyInput input, FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(FunctionNames.SprayPaintActivityName);

        logger.LogWarning("Installing window on {ModelName}.", input.Vehicle.ModelName);

        return Task.CompletedTask;
    }

    [Function(FunctionNames.InstallInteriorActivityName)]
    public static Task InstallInteriorAsync([ActivityTrigger] AssemblyInput input, FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(FunctionNames.InstallInteriorActivityName);

        logger.LogWarning("Installing interior in {ModelName} in {Material}.", input.Vehicle.ModelName, input.Request.InteriorMaterial);
        input.Vehicle.InteriorMaterial = input.Request.InteriorMaterial;

        return Task.CompletedTask;
    }

    [Function(FunctionNames.AddWheelsActivityName)]
    public static Task AddWheelsAsync([ActivityTrigger] AssemblyInput input, FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(FunctionNames.SprayPaintActivityName);

        logger.LogWarning("Adding wheel on {ModelName}.", input.Vehicle.ModelName);

        return Task.CompletedTask;
    }

    #endregion
}