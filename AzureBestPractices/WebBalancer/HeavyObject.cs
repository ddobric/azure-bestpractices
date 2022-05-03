namespace Daenet.WebBalancer
{
    public class HeavyObject
    {
        private string id;

        public HeavyObject(string id)
        {
            this.id = id;
        }

        public string Run()
        {
            return $"({id}) - Hello :)";
        }
    }
}
