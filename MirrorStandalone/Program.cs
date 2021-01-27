using Mirror.Standalone.Components;

namespace Mirror.Standalone
{
    class Program
    {
        static void Main(string[] args)
        {
            StandaloneNG mirror = new StandaloneNG();

            TestComponent comp = new TestComponent(mirror);

            while (true)
            {
                mirror.Update();
                comp.Update();
            }
        }
    }
}
