using Automata.Backbone;
using Automata.Parser;

namespace automataV2
{
    internal class Program
    {
        static void Main(string[] args)
        {
            while (true)
            {
                var program = "";
                while (true)
                {
                    var line = Console.ReadLine()!;
                    if (line != "")
                        program += line + "\n";
                    else
                    {
                        program = ProgramCleaner.CleanProgram(program);
                        var tokens = Tokenizer.Tokenize(program);
                        Token.PrintTokens(tokens);
                        var blocks = Blocker.BlockTokens(tokens);
                        Console.WriteLine("Successfully blocked program:");
                        foreach(var block in blocks)
                            Console.WriteLine(block);
                        break;
                    }
                }
            }
            // tests
            //foreach(var file in Directory.GetFiles("Tests"))
            //{
            //    if (!file.EndsWith(".amta")) continue;
            //    var globalScope = new Scope();
            //    var program = ProgramCleaner.CleanProgram(File.ReadAllText(file));
            //    var tokens = Tokenizer.Tokenize(program);
            //    Token.PrintTokens(tokens);

            //}
        }
    }
}
