namespace Daenet.WebBalancer
{
    /// <summary>
    /// Implements the object pool pattern. https://en.wikipedia.org/wiki/Object_pool_pattern
    /// </summary>
    /// <typeparam name="THeavyObject"></typeparam>
    public class ObjectPool<THeavyObject> where THeavyObject : class
    {
        private static List<THeavyObject> objects;

        public ObjectPool()
        {

        }

        public ObjectPool(List<THeavyObject> objectList)
        {
            objects = objectList;
        }

        private List<THeavyObject> GetObjectsInterlockedWithQueue()
        {
            List<THeavyObject> localModels;

            while (true)
            {
                localModels = Interlocked.Exchange<List<THeavyObject>>(ref objects, null);

                // If waitOnFreeObject is set on TRUE, we queue requests and wait on the free object.
                if (localModels != null)
                    break;

                Thread.Sleep(100);
            }

            return localModels;
        }

        internal bool IsInitialized { get; set; }

        internal void LoadObjects(List<THeavyObject> objectList)
        {
            objects = objectList;
            IsInitialized = true;
        }

        /// <summary>
        /// Gets the object from the pool.
        /// </summary>
        /// <param name="isBusy"></param>
        /// <returns></returns>
        public THeavyObject Get(out bool isBusy, bool waitOnFreeObject = false)
        {
            isBusy = false;

            int retries = 1;

            THeavyObject objectInstance = null;

            List<THeavyObject> localModels = null;

            while (retries-- > 0)
            {
                // This waits to access the list of engines in the pool.
                localModels = GetObjectsInterlockedWithQueue();

                if (localModels.Count > 0)
                {
                    objectInstance = localModels[localModels.Count - 1];
                    localModels.RemoveAt(localModels.Count - 1);
                    Interlocked.Exchange<List<THeavyObject>>(ref objects, localModels);
                    break;
                }
                else
                    Interlocked.Exchange<List<THeavyObject>>(ref objects, localModels);

                if (waitOnFreeObject == false)
                    break;

                Thread.Sleep(100);
            }

            if (objectInstance != null)
            {
                isBusy = false;
            }
            else
            {
                isBusy = true;
            }

            return objectInstance;
        }


        /// <summary>
        /// Returns the object to the pool.
        /// </summary>
        /// <param name="returningObject"></param>
        public void Return(THeavyObject returningObject)
        {
            List<THeavyObject> localModels = GetObjectsInterlockedWithQueue();

            localModels.Add(returningObject);

            Interlocked.Exchange<List<THeavyObject>>(ref objects, localModels);
        }
    }
}
