namespace Daenet.WebBalancer
{
    public class ObjectPool<THeavyObject> where THeavyObject : class
    {
        private List<THeavyObject> objects;

        public ObjectPool(List<THeavyObject> objects) 
        {
            this.objects = objects;
        }

        private List<THeavyObject> GetModelsInterlocked()
        {
            List<THeavyObject> localModels;

            while (true)
            {
                localModels = Interlocked.Exchange<List<THeavyObject>>(ref this.objects, null);

                if (localModels != null)
                    break;

                Thread.Sleep(100);
            }

            return localModels;
        }

        public THeavyObject Get(out bool isBusy)
        {
            isBusy = false;

            THeavyObject predictionEngine = null;

            List<THeavyObject> localModels;

            // This waits to access the list of engines in the pool.
            localModels = GetModelsInterlocked();
                     
            if (localModels == null || localModels.Count > 0)
            {
                predictionEngine = localModels[localModels.Count - 1];
                localModels.RemoveAt(localModels.Count - 1);
            }
            else
            {
                isBusy = true;
            }

            Interlocked.Exchange<List<THeavyObject>>(ref this.objects, localModels);

            return predictionEngine;
        }

        public void Return(THeavyObject returningObject)
        {
            List<THeavyObject> localModels = GetModelsInterlocked();

            localModels.Add(returningObject);
         
            Interlocked.Exchange<List<THeavyObject>>(ref this.objects, localModels);
        }
    }
}
