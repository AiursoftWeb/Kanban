using Aiursoft.Kanban.ExamRunner.Configuration;
using Aiursoft.Kanban.ExamRunner.Execution;

namespace Aiursoft.Kanban.ExamRunner;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is not ["--config", var configurationPath])
        {
            Console.Error.WriteLine("Usage: Aiursoft.Kanban.ExamRunner --config <path>");
            return 1;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            var configuration = await ExamConfigurationLoader.LoadAsync(
                configurationPath,
                cancellation.Token);
            var result = await new ExamOrchestrator().RunAsync(
                configuration,
                cancellation.Token);
            Console.WriteLine($"Agent exam reports: {result.OutputDirectory}");
            return result.ExitCode;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Agent exam was cancelled.");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }
}
