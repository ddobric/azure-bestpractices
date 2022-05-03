// See https://aka.ms/new-console-template for more information


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Daenet.AzureBestPractices
{
    public static class Program
    {
        private static ILoggerFactory logFactory;

        public static async Task Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            DeployerConfig dpCfg = Init(args);

            Deployer deployer = new Deployer(dpCfg, logFactory.CreateLogger<Deployer>());

            await deployer.RunAsync("job1", progressCallback: (msg) => Console.WriteLine(msg));
        }

        private static DeployerConfig Init(string[] args)
        {
            var builder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
             .AddEnvironmentVariables()
             .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
             .AddCommandLine(args);

            var cfg = builder.Build();

            logFactory = InitLogging(cfg);

            DeployerConfig dpCfg = new DeployerConfig();

            cfg.GetSection("DeployerConfig").Bind(dpCfg);
            return dpCfg;
        }


        /// <summary>
        /// Create Logging infrastructure in the Trainer Workload
        /// </summary>
        /// <returns></returns>
        private static ILoggerFactory InitLogging(IConfigurationRoot configRoot)
        {
            //create logger from the appsettings addConsole Debug to Logg 
            return LoggerFactory.Create(logBuilder =>
            {
                ConsoleLoggerOptions logCfg = new ConsoleLoggerOptions();

                logBuilder.AddConfiguration(configRoot.GetSection("Logging"));

                logBuilder.AddConsole((opts) =>
                {
                    opts.IncludeScopes = true;
                }).AddDebug();
            });
        }

    }
}