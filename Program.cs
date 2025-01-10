using Automata.Backbone;
using Automata.DefaultFunctions;
using Automata.Parser;
using System.Text;

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
                        if (tokens.Count == 0)
                            break;
                        Token.PrintTokens(tokens);
                        var instr = new ProgramParser(tokens).ParseProgram();
                        Console.WriteLine("Parsed program:");
                        foreach (var ins in instr)
                            Console.WriteLine(ins);
                        // create scope and run script
                        Console.WriteLine("Running program:\n");
                        var scope = new Scope();
                        DefaultFunctions.RegisterFunctions(scope);
                        // create fake fn runner
                        var fileRunner = new FunctionRunner([], instr);
                        // scope file
                        var fileScope = new Scope(scope);
                        var retVal = fileRunner.Call(fileScope);
                        Console.WriteLine("\n--- Program exited with value: " + retVal.Stringify().Value);
                        break;
                    }
                }
            }
            // tests
            //foreach (var file in Directory.GetFiles("Tests"))
            //{
            //    if (!file.EndsWith(".amta")) continue;
            //    var program = ProgramCleaner.CleanProgram(File.ReadAllText(file));
            //    var tokens = Tokenizer.Tokenize(program);
            //    try
            //    {
            //        var instr = new ProgramParser(tokens).ParseProgram();
            //        var scope = new Scope();
            //        DefaultFunctions.RegisterFunctions(scope);
            //        // redifine :print function
            //        StringBuilder output = new();
            //        scope.SetVariable(":print", new FunctionValue(new NativeFunction([(new VarNameResolver("!str"), BaseValue.ValueType.AnyType)], args =>
            //        {
            //            output.Append(args[0].Stringify().Value);
            //            return NilValue.Nil;
            //        })));
            //        var fileRunner = new FunctionRunner([], instr);
            //        var fileScope = new Scope(scope);
            //        try
            //        {
            //            var retVal = fileRunner.Call(fileScope);
            //            // compare output to test
            //            var expected = File.ReadAllText(file.Replace(".amta", ".out"));
            //            if (expected == output.ToString())
            //                Console.WriteLine("[PASS] " + Path.GetFileNameWithoutExtension(file));
            //            else
            //            {
            //                Console.WriteLine("[FAIL] " + Path.GetFileNameWithoutExtension(file));
            //                Console.WriteLine("GOT:\n" + output.ToString());
            //            }
            //        }
            //        catch (Exception ex)
            //        {
            //            Console.WriteLine("[ERRR] " + Path.GetFileNameWithoutExtension(file) + "\n" + ex.ToString());
            //            continue;
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        Console.WriteLine("[ERRP] " + Path.GetFileNameWithoutExtension(file) + "\n" + ex.ToString());
            //        continue;
            //    }
            //}
        }
    }
}
