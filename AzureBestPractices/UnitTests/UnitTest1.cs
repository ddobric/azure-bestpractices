using Daenet.WebBalancer;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace UnitTests
{
    [TestClass]
    public class UnitTest1
    {
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

            for (int i = 0; i < 10*cnt; i++)
            {
                HeavyObject obj = pool.Get(out isBussy);
                Assert.IsNotNull(obj);
                pool.Return(obj);
            }
        }
    }
}