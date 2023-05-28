namespace XI.Host.Common
{
    public sealed class Couple<T>
    {
        public string Key { get; private set; }
        public T Value { get; private set; }

        public Couple(string key, in T value)
        {
            Key = key;
            Value = value;
        }
    }
}
