namespace Clio.Command.ModelBuilder
{
    public static class Factory
    {
        public static T Create<T>() where T : class, new()
        {
            return new T();
        }
    }
}
