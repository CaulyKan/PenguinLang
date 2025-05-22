
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
        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<Options>(args).MapResult(
                Run,
                _ => -1
            );
        }

        static void RunDAP(Options options)
        {
            var dap = new DAP();
            dap.Protocol.Run();
        }
        static int Run(Options options)
        {
            RunDAP(options);
            return 0;
        }
    }
}