// See https://aka.ms/new-console-template for more information


using Microsoft.Extensions.Configuration;
using System.Diagnostics;

namespace Daenet.AzureBestPractices.MyJob
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            int n = 0;
            //
            // Fluent API
            var builder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
             .AddEnvironmentVariables()
             .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
             .AddCommandLine(args);

            for (int i = 0; i < 120; i++)
            {
                Console.WriteLine($"{n++}");

                AllocateMemory();

                TraceUsage();

                Thread.Sleep(1000);
            }            
        }

        private static void TraceUsage()
        {


            Console.WriteLine($"PrivateMemorySize64:\t{((double)Process.GetCurrentProcess().PrivateMemorySize64 /1024 / 1024/1024).ToString("#.##")}");
        }

        private static List<string> list = new List<string>();

        private static void AllocateMemory()
        {
            var proc = Process.GetCurrentProcess();
            var sz = ((double)proc.PrivateMemorySize64 / 1024 / 1024 / 1024);
            if (sz < 2)
            {
                for (int i = 0; i < 10000000; i++)
                {
                    list.Add("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
                }
            }
        }
    }
}