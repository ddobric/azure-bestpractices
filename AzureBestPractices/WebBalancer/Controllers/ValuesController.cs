using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Daenet.WebBalancer
{
    [Route("api/[controller]")]
    [ApiController]
    public class ValuesController : ControllerBase
    {
        private ILogger<ValuesController> logger;

        private static ObjectPool<HeavyObject> pool;

        public ValuesController(ObjectPool<HeavyObject> objectPool, ILogger<ValuesController> logger)
        {
            this.logger = logger;

            pool = objectPool;
            
            //logger?.LogInformation($"POOL: {pool.GetHashCode()}");
        }

        private void EnsurePoolLoaded()
        {
            if (!pool.IsInitialized)
            {
                pool.LoadObjects(new List<HeavyObject>()
                {
                    new HeavyObject("Object 1"),
                    new HeavyObject("Object 2"),
                    new HeavyObject("Object 3"),
                });
            }
        }

        [HttpGet("doit/{delay}/{waitOnFreeObject}")]
        public async Task<string> DoIt(int? delay = 10000, bool waitOnFreeObject = true)
        {
            logger?.LogInformation("DoIt entered.");

            EnsurePoolLoaded();

            bool isBussy;

            string res;

            var obj = pool.Get(out isBussy, waitOnFreeObject);

            if (isBussy)
            {
                logger?.LogError("DoIt bussy.");

                res = "bussy";

                throw new Exception("bussy");
            }
            else
            {
                res = await obj.Run(delay.Value);

                pool.Return(obj);

                logger?.LogInformation("DoIt completed.");
            }

            return $"{DateTime.Now.ToString()} - {res}";
        }

        /// <summary>
        /// Creates a large object and invokes an operation on it.
        /// </summary>
        /// <param name="delay"></param>
        /// <returns></returns>
        [HttpGet("ConsumeBigObject/{delay}/{notUsed}")]
        public async Task<string> ConsumeBigObject(int? delay = 100, bool notUsed = false)
        {
            logger?.LogInformation("ConsumeBigObject entered.");

            var sz1 = ((double)Process.GetCurrentProcess().PrivateMemorySize64 / 1024 / 1024 / 1024);

            HeavyObject obj = new HeavyObject(Guid.NewGuid().ToString());

            var res = await obj.Run(delay.Value);

            var sz2 = ((double)Process.GetCurrentProcess().PrivateMemorySize64 / 1024 / 1024 / 1024);

            logger?.LogInformation($"Memory {sz1}, {sz2}, diff:{sz2 - sz1}");

            logger?.LogInformation("ConsumeBigObject completed.");

            return $"{DateTime.Now.ToString()} - {res}";
        }


        [HttpGet("ping/{delay}/{notUsed}")]
        public async Task<string> Ping(int delay = 10000, bool notUsed = true)
        {
            logger?.LogInformation("Ping entered.");

            await Task.Delay(delay);

            logger?.LogInformation("Ping completed.");

            return DateTime.Now.ToString();
        }
    }
}
