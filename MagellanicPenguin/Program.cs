
global using BabyPenguin.SemanticNode;
global using BabyPenguin.Symbol;
global using BabyPenguin.SemanticPass;
global using BabyPenguin.SemanticInterface;
global using BabyPenguin.VirtualMachine;
global using BabyPenguin;
global using PenguinLangSyntax;
global using PenguinLangSyntax.SyntaxNodes;
global using System.Text;
global using System.Linq;
global using System.Collections.Generic;

using CommandLine;

namespace MagellanicPenguin
{
    public class Options
    {
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, world!");
        }

        static void RunDAP(Options options)
        {
            var dap = new DAP();
            dap.Protocol.Run();
        }

    }
}