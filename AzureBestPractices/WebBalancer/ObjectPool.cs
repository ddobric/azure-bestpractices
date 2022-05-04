namespace Daenet.WebBalancer
{
    /// <summary>
    /// Implements the object pool pattern. https://en.wikipedia.org/wiki/Object_pool_pattern
    /// </summary>
    /// <typeparam name="THeavyObject"></typeparam>
    public class ObjectPool<THeavyObject> where THeavyObject : class
    {
        private List<THeavyObject> objects;

        public ObjectPool()
        {

        }

        public ObjectPool(List<THeavyObject> objectList)
        {
            objects = objectList;
        }

        private List<THeavyObject> GetObjectsInterlockedWithQueue(bool waitOnFreeObject = true)
        {
            int maxRetries = 20;

            List<THeavyObject> localModels = null;

            while (maxRetries-- > 0)
            {
                localModels = Interlocked.Exchange<List<THeavyObject>>(ref objects, null);

                // If waitOnFreeObject is set on FALSE, we do not queue requests.
                if (waitOnFreeObject == false)
                    break;

                // If waitOnFreeObject is set on TRUE, we queue requests and wait on the free object.
                if (localModels != null && localModels.Count > 0)
                    break;

                Thread.Sleep(100);
            }

            return localModels;
        }

        internal bool IsInitialized {get;set;}
        
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

            THeavyObject predictionEngine = null;

            List<THeavyObject> localModels;

            // This waits to access the list of engines in the pool.
            localModels = GetObjectsInterlockedWithQueue(waitOnFreeObject);

            if (localModels != null && localModels.Count > 0)
            {
                predictionEngine = localModels[localModels.Count - 1];
                localModels.RemoveAt(localModels.Count - 1);
            }
            else
            {
                isBusy = true;
            }

            Interlocked.Exchange<List<THeavyObject>>(ref objects, localModels);

            return predictionEngine;
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
