using Daenet.WebBalancer;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTests
{
    [TestClass]
    public class UnitTest1
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public void TestMethod1()
        {
            List<HeavyObject> list = new List<HeavyObject>()
            {
                new HeavyObject("Object 1"),
                new HeavyObject("Object 2"),
                new HeavyObject("Object 3"),
            };

            var pool = new Daenet.WebBalancer.ObjectPool<HeavyObject>(list);

            bool isBussy;

            int cnt = list.Count;

            for (int i = 0; i < cnt; i++)
            {
                HeavyObject obj = pool.Get(out isBussy);
                Assert.IsNotNull(obj);
                Assert.IsFalse(isBussy);
            }

            HeavyObject lastObj = pool.Get(out isBussy);
            Assert.IsNull(lastObj);
            Assert.IsTrue(isBussy);
        }

        [TestMethod]
        public void TestMethod2()
        {
            List<HeavyObject> list = new List<HeavyObject>()
            {
                new HeavyObject("Object 1"),
                new HeavyObject("Object 2"),
                new HeavyObject("Object 3"),
            };

            var pool = new Daenet.WebBalancer.ObjectPool<HeavyObject>(list);

            bool isBussy;

            int cnt = list.Count;

            for (int i = 0; i < 10 * cnt; i++)
            {
                HeavyObject obj = pool.Get(out isBussy);
                Assert.IsNotNull(obj);
                pool.Return(obj);
            }
        }

        private int systBusyCallsCounter;
        private int successfulCallsCounter;
        private int serverErrCallsCounter;

        [TestMethod]
        [DataRow(1, 1, 3, "doit")]
        ///<summary>
        /// Execute requests in parallel without request queueing optimization in the object pool.
        ///</summary>
        public void InitObjectPool(int concurrentCalls, int numOfRequestsInSequence, int objectsToConsume, string operationToExec)
        {
            ExecuteLoadTest(concurrentCalls, numOfRequestsInSequence, objectsToConsume, operationToExec, false);
        }

        [TestMethod]
        [DataRow(100, 1, 3, "doit")]
        ///<summary>
        /// Execute requests in parallel without request queueing optimization in the object pool.
        ///</summary>
        public void DoItLoadTest(int concurrentCalls, int numOfRequestsInSequence, int objectsToConsume, string operationToExec)
        {
            ExecuteLoadTest(concurrentCalls, numOfRequestsInSequence, objectsToConsume, operationToExec, false);
        }

        [TestMethod]
        [DataRow(100, 1, 3, "doit")]
        ///<summary>
        /// Execute requests in parallel WITH request queueing optimization in the object pool.
        ///</summary>
        public void DoItOptimizedLoadTest(int concurrentCalls, int numOfRequestsInSequence, int objectsToConsume, string operationToExec)
        {
            ExecuteLoadTest(concurrentCalls, numOfRequestsInSequence, objectsToConsume, operationToExec, true);
        }

        [TestMethod]
        [DataRow(1000, 1, 3, "ping")]
        ///<summary>
        /// This test parallelly sends requests.
        ///</summary>
        public void PingLoadTest(int concurrentCalls, int numOfRequestsInSequence, int objectsToConsume, string operationToExec)
        {
            ExecuteLoadTest(concurrentCalls, numOfRequestsInSequence, objectsToConsume, operationToExec, false);
        }

        [TestMethod]
        [DataRow(2, 1, 3, "ConsumeBigObject")]
        ///<summary>
        /// This test parallelly sends requests.
        ///</summary>
        public void ConsumeBigObjectLoadTest(int concurrentCalls, int numOfRequestsInSequence, int objectsToConsume, string operationToExec)
        {
            ExecuteLoadTest(concurrentCalls, numOfRequestsInSequence, objectsToConsume, operationToExec, false);
        }

        private void ExecuteLoadTest(int concurrentCalls, int numOfRequestsInSequence, int objectsToConsume, string operationToExec, bool waitOnFreeObject)
        {
            var baseUrl = "https://localhost:7232";
           //baseUrl = "https://webbalancer.azurewebsites.net";

            List<string> tenants = new List<string>();
            List<string> scopes = new List<string>();
            ConcurrentBag<double> execTimeList = new ConcurrentBag<double>();

            //
            // create thread to start
            List<Thread> threads = new List<Thread>();
            for (int i = 0; i < concurrentCalls; i++)
            {
                HttpClientHandler handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                HttpClient client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(600);
                var t = new Thread(async () => await ExecuteRequests(client, baseUrl, numOfRequestsInSequence, (i % objectsToConsume).ToString(), operationToExec, waitOnFreeObject, execTimeList));
                threads.Add(t);
            }

            Console.WriteLine($"Time Start: {DateTime.Now.ToString("yyyyMMddHHmmssfff")}");

            foreach (var thread in threads)
            {
                thread.Start();
            }

            while (execTimeList.Count() != concurrentCalls)
            {
                Thread.Sleep(500);
            }

            Console.WriteLine($"Time End{DateTime.Now.ToString("yyyyMMddHHmmssfff")}");

            TestContext.WriteLine("");
            TestContext.WriteLine("");

            TestContext.WriteLine($"SystBusy Calls: {systBusyCallsCounter}.");
            TestContext.WriteLine($"Successful Calls: {successfulCallsCounter}.");
            TestContext.WriteLine($"ServerErr Calls: {serverErrCallsCounter}.\n");

            TestContext.WriteLine($"Number of execute times: {execTimeList.Count()}.");
            TestContext.WriteLine($"Max execute time: {execTimeList.Max()}.");
            TestContext.WriteLine($"Min execute time: {execTimeList.Min()}.");
            TestContext.WriteLine($"Average execute time: {execTimeList.Average()}.");
        }


        /// <summary>
        /// Executes sequentionaly the number of requests defined by amount.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="baseUrl"></param>
        /// <param name="instanceId"></param>
        /// <param name="executedTimes"></param>
        /// <param name="repeats"></param>
        /// <returns></returns>
        private async Task ExecuteRequests(HttpClient client, string baseUrl, int repeats, string instanceId, string operationToExec, bool waitOnFreeObject, ConcurrentBag<double> executedTimes)
        {
            for (int i = 0; i < repeats; i++)
            {
                Stopwatch timer = new Stopwatch();

                try
                {
                    timer.Start();

                    var response = await client.GetAsync($"{baseUrl}/api/Values/{operationToExec}/{1000}/{waitOnFreeObject}");
                    
                    timer.Stop();

                    executedTimes.Add(timer.Elapsed.TotalSeconds);

                    if (response.IsSuccessStatusCode)
                    {
                        var result = response.Content.ReadAsStringAsync().Result;
                        if (result.Contains("not been found"))
                        {
                            CountBusyCalls();
                        }
                        else
                        {
                            CountSuccessfulCalls();
                        }
                    }
                    else
                    {
                        var result = response.Content.ReadAsStringAsync().Result;
                        if (result.Contains("bussy"))
                        {
                            CountBusyCalls();
                        }
                        else
                        {
                            CountServerErrCalls();
                        }
                    }
                }
                catch (Exception ex)
                {
                    timer.Stop();
                    executedTimes.Add(timer.Elapsed.TotalSeconds);
                    CountServerErrCalls();
                }
                client.Dispose();
                await Task.Delay(100);
            }
        }

        private void CountBusyCalls()
        {
            Interlocked.Increment(ref systBusyCallsCounter);
        }
        private void CountSuccessfulCalls()
        {
            Interlocked.Increment(ref successfulCallsCounter);
        }
        private void CountServerErrCalls()
        {
            Interlocked.Increment(ref serverErrCallsCounter);
        }
    }
}