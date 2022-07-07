using System.Diagnostics;

namespace Daenet.WebBalancer
{

    /// <summary>
    /// Simulates an API that consumes a lot of RAM.
    /// </summary>
    public class HeavyObject
    {

        private string id;

        public HeavyObject(string id)
        {
            this.id = id;

            // Grows the object size to 1GB.
           AllocateMemory(1);
        }

        #region RAM Allocation
        private static List<string> list = new List<string>();

        private static void AllocateMemory(int maxSizeInGb)
        {
            var sz1 = ((double)Process.GetCurrentProcess().PrivateMemorySize64 / 1024 / 1024 / 1024);

            double sz2 = sz1;

            int cnt = 0;

            while ((sz2 - sz1) < maxSizeInGb)
            {
                for (int i = 0; i < 10000000; i++)
                {
                    list.Add("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
                    if (++cnt % 2000000 == 0)
                        Thread.Sleep(100);
                }

                sz2 = ((double)Process.GetCurrentProcess().PrivateMemorySize64 / 1024 / 1024 / 1024);
            }

            Debug.WriteLine("Allocation..");
        }
        #endregion


        public async Task<string> Run(int delay)
        {
            var rand = new Random();
            //var time = rand.Next(500, delay);
            await Task.Delay(delay);

            return $"({id}) - Hello :)";
        }
    }
}
