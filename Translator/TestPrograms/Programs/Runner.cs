using System;

namespace Tests
{
    /// <summary>Runs the test program named on the command line.</summary>
    public static class Runner
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("usage: <program name>");
                return 1;
            }
            string name = args[0];
            if (name == "hello") Hello.Run();
            else if (name == "arithmetic") Arithmetic.Run();
            else if (name == "floats") Floats.Run();
            else if (name == "strings") Strings.Run();
            else if (name == "classes") Classes.Run();
            else if (name == "structs") Structs.Run();
            else if (name == "arrays") Arrays.Run();
            else if (name == "controlflow") ControlFlow.Run();
            else if (name == "enums") Enums.Run();
            else if (name == "pointers") Pointers.Run();
            else if (name == "generics") Generics.Run();
            else if (name == "interfaces") Interfaces.Run();
            else if (name == "exceptions") Exceptions.Run();
            else if (name == "delegates") Delegates.Run();
            else if (name == "throw" || name == "index" || name == "null" || name == "divide" || name == "cast") Throws.Run(name);
            else
            {
                Console.WriteLine("unknown program: " + name);
                return 2;
            }
            return 0;
        }
    }
}
