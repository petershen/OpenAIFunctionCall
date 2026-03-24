using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAIFunctionCall.Interfaces;
using OpenAIFunctionCall.LLM;
using OpenAIFunctionCall.Services;
using System.Net.Http.Headers;

namespace OpenAIFunctionCall
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Configuration
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .AddUserSecrets<Program>();

            string apiKey = builder.Configuration["OPENAI_API_KEY"] ?? throw new InvalidOperationException("OPENAI_API_KEY is not set in configuration.");

            builder.Services
                .AddSingleton(_ =>
                {
                    var client = new OpenaiApiHttpClient() { BaseAddress = new Uri("https://api.openai.com/v1/chat/completions") };
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    return client;
                })
                .AddSingleton(_ =>
                {
                    var client = new WeatherApiHttpClient() { BaseAddress = new Uri("https://api.weather.gov") };
                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("weather-tool", "1.0"));
                    return client;
                })
                .AddSingleton(new DataApiHttpClient())
                .AddSingleton<IFunctionTools, FunctionTools>()
                .AddHostedService<App>();


            var host = builder.Build();
            await host.RunAsync();
        }
    }

    internal class App : IHostedService
    {
        private readonly OpenaiApiHttpClient _client;
        private readonly IFunctionTools _tools;

        public App(OpenaiApiHttpClient client, IFunctionTools tools)
        {
            _client = client;
            _tools = tools;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OpenAIFunctionCall Started!");
            Console.ResetColor();

            PromptForInput();

            try
            {
                while (Console.ReadLine() is string query && !"exit".Equals(query, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(query))
                    {
                        PromptForInput();
                        continue;
                    }

                    Console.WriteLine();

                    await foreach (var message in OpenAILLM.GetStreamingResponseAsync(_client, query, _tools))
                    {
                        Console.WriteLine(message);
                    }

                    PromptForInput();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                Environment.Exit(0);
            }

            await Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void PromptForInput()
        {
            Console.WriteLine();
            Console.WriteLine("Enter a command (or 'exit' to quit):");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("> ");
            Console.ResetColor();
        }
    }
}
