namespace Mirror.Standalone
{
    class Program
    {
        static void Main(string[] args)
        {
            StandaloneNG mirror = new StandaloneNG();

            while(true)
            {
                mirror.Update();
            }
        }
    }
}
