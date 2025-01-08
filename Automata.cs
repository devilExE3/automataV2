using Automata.Backbone;
using Automata.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Xsl;

namespace Automata
{
    namespace Backbone
    {
        public abstract class BaseValue : IEvaluable
        {
            public enum ValueType
            {
                Number,
                String,
                Object,
                Function,
                Nil,
                AnyType,
            }
            public ValueType Type;
            public object? Value;
            public BaseValue Evaluate(Scope currentScope) => this;
            public abstract StringValue Stringify();
            public override bool Equals(object? obj)
            {
                if (obj is not BaseValue objBV) return false;
                return Equals(objBV);
            }
            public abstract bool Equals(BaseValue other);
            public bool HoldsTrue()
            {
                if (Type == ValueType.Nil) return false;
                if (Type != ValueType.Number) return true;
                return (double)Value! != 0;
            }

            public override int GetHashCode() => base.GetHashCode();
            public override string ToString() => (string)Stringify().Value!;
        }
        public class NumberValue : BaseValue
        {
            public NumberValue(double value)
            {
                Type = ValueType.Number;
                Value = value;
            }
            public override StringValue Stringify() => new(((double)Value!).ToString());
            public override bool Equals(BaseValue other)
            {
                if (other.Type != ValueType.Number)
                    return false;
                return (double)Value! == (double)other.Value!;
            }
        }
        public class StringValue : BaseValue
        {
            public StringValue(string value)
            {
                Type = ValueType.String;
                Value = value;
            }
            public override StringValue Stringify() => this;
            public override bool Equals(BaseValue other)
            {
                if (other.Type != ValueType.String)
                    return false;
                return (string)Value! == (string)other.Value!;
            }
        }
        public class ObjectValue : BaseValue
        {
            public ObjectValue()
            {
                Type = ValueType.Object;
                Value = new Dictionary<string, BaseValue>();
            }
            public override StringValue Stringify() => Stringify(0);
            public StringValue Stringify(int indent)
            {
                string ret = "{\n".Indent(indent);
                foreach (var value in (Dictionary<string, BaseValue>)Value!)
                    ret += ("  " + value.Key + ": " + value.Value.Stringify()).Indent(indent) + "\n";
                ret += "}".Indent(indent);
                return new StringValue(ret);
            }
            public override bool Equals(BaseValue other)
            {
                if (other.Type != ValueType.Object)
                    return false;
                var d1 = (Dictionary<string, BaseValue>)Value!;
                var d2 = (Dictionary<string, BaseValue>)other.Value!;
                return d1 == d2 || d1.Count == d2.Count && !d1.Except(d2).Any();
            }
            public BaseValue GetChild(string name)
            {
                var dict = (Dictionary<string, BaseValue>)Value!;
                if (!dict.ContainsKey(name))
                    return NilValue.Nil;
                return dict[name];
            }
            public void SetChild(string name, BaseValue value)
            {
                var dict = (Dictionary<string, BaseValue>)Value!;
                if (value.Type == ValueType.Nil)
                    dict.Remove(name); // clear memory
                else
                    dict[name] = value;
            }
            public bool IsArrayConvention()
            {
                var dict = (Dictionary<string, BaseValue>)Value!;
                if (!dict.ContainsKey("length")) return false; // doesn't have length attribute
                var len_val = dict["length"];
                if (len_val.Type != ValueType.Number) return false; // length value is non-number
                var len = (double)len_val.Value!;
                if (len < 0 || len != Math.Floor(len)) return false; // length value is negative / not whole
                if (dict.Keys.Count != (int)len + 1) return false; // There should be length + 1 keys
                var keys = Enumerable.Range(0, (int)len);
                return dict.Keys.All(x => keys.Contains(int.Parse(x)));
            }
        }
        public class FunctionValue : BaseValue
        {
            public FunctionValue(ICallable value)
            {
                Type = ValueType.Function;
                Value = value;
            }
            public override StringValue Stringify()
            {
                string ret = "fun(";
                bool firstParam = true;
                foreach (var param in ((ICallable)Value!).Head)
                {
                    if (!firstParam)
                        ret += ", ";
                    else
                        firstParam = false;
                    ret += param;
                }
                return new StringValue(ret + ")");
            }
            // functions can only be diferentiated by their head
            public override bool Equals(BaseValue other)
            {
                if (other.Type != ValueType.Function)
                    return false;
                var h1 = ((ICallable)Value!).Head;
                var h2 = ((ICallable)other.Value!).Head;
                return h1 == h2 || h1.Count == h2.Count && !h1.Except(h2).Any();
            }
        }
        public class NilValue : BaseValue
        {
            public static NilValue Nil => nil;
            static readonly NilValue nil = new()
            {
                Type = ValueType.Nil,
                Value = null
            };
            public override StringValue Stringify() => new("<nil>");
            public override bool Equals(BaseValue other)
            {
                return other.Type == ValueType.Nil;
            }
        }

        public interface ICallable
        {
            public BaseValue Call(Scope currentScope);
            public List<(VarResolver, BaseValue.ValueType)> Head { get; }
        }
        public interface IEvaluable
        {
            public BaseValue Evaluate(Scope currentScope);
        }

        public class Expression : IEvaluable
        {
            public IEvaluable lhs;
            public IEvaluable? rhs;
            public ExpressionOperator op;

            public Expression(IEvaluable lhs, ExpressionOperator op, IEvaluable? rhs)
            {
                this.lhs = lhs;
                this.rhs = rhs;
                this.op = op;
            }

            public enum ExpressionOperator
            {
                Plus,
                Minus,
                Times,
                Div,
                Modulo,
                Less,
                LessEqual,
                Greater,
                GreaterEqual,
                Equal,
                NotEqual,
                LogicalNot,
            }

            public BaseValue Evaluate(Scope currentScope)
            {
                BaseValue lhs_value = lhs.Evaluate(currentScope);
                BaseValue? rhs_value = rhs?.Evaluate(currentScope) ?? null;
                switch (op)
                {
                    case ExpressionOperator.Plus:
                        if (rhs == null)
                        { // unary operator +
                            if (lhs_value.Type == BaseValue.ValueType.String)
                                return new NumberValue(double.Parse((string)lhs_value.Value!));
                            throw new Exceptions.InvalidOperationException($"Tried converting non-string value {lhs_value.Stringify().Value} to Number");
                        }
                        if (lhs_value.Type == BaseValue.ValueType.Number && rhs_value!.Type == BaseValue.ValueType.Number)
                            return new NumberValue((double)lhs_value.Value! + (double)rhs_value.Value!);
                        return new StringValue((string)lhs_value.Stringify().Value! + (string)rhs_value!.Stringify().Value!);
                    case ExpressionOperator.Minus:
                        if (lhs_value.Type != BaseValue.ValueType.Number)
                            throw new Exceptions.InvalidOperationException($"LHS value {lhs_value.Stringify().Value} of operator Minus is not Number");
                        if (rhs == null) // unary operator -
                            return new NumberValue(-(double)lhs_value.Value!);
                        if (rhs_value!.Type != BaseValue.ValueType.Number)
                            throw new Exceptions.InvalidOperationException($"RHS value {rhs_value.Stringify().Value} of operator Minus is not Number");
                        return new NumberValue((double)lhs_value.Value! - (double)rhs_value.Value!);
                    case ExpressionOperator.Times:
                        if (rhs == null)
                            throw new Exceptions.NullOperandException("RHS", "Times");
                        if (lhs_value.Type != BaseValue.ValueType.Number)
                            throw new Exceptions.InvalidOperationException($"LHS value {lhs_value.Stringify().Value} of operator Times is not Number");
                        if (rhs_value!.Type != BaseValue.ValueType.Number)
                            throw new Exceptions.InvalidOperationException($"RHS value {rhs_value!.Stringify().Value} of operator Times is not Number");
                        return new NumberValue((double)lhs_value.Value! * (double)rhs_value.Value!);
                    case ExpressionOperator.Div:
                        if (rhs == null)
                            throw new Exceptions.NullOperandException("RHS", "Div");
                        if (lhs_value.Type != BaseValue.ValueType.Number)
                            throw new Exceptions.InvalidOperationException($"LHS value {lhs_value.Stringify().Value} of operator Div is not Number");
                        if (rhs_value!.Type != BaseValue.ValueType.Number)
                            throw new Exceptions.InvalidOperationException($"RHS value {rhs_value!.Stringify().Value} of operator Div is not Number");
                        return new NumberValue((double)lhs_value.Value! / (double)rhs_value.Value!);
                    case ExpressionOperator.Modulo:
                        if (rhs == null)
                            throw new Exceptions.NullOperandException("RHS", "Modulo");
                        if (lhs_value.Type != BaseValue.ValueType.Number)
                            throw new Exceptions.InvalidOperationException($"LHS value {lhs_value.Stringify().Value} of operator Modulo is not Number");
                        if (rhs_value!.Type != BaseValue.ValueType.Number)
                            throw new Exceptions.InvalidOperationException($"RHS value {rhs_value!.Stringify().Value} of operator Modulo is not Number");
                        var val = (double)lhs_value.Value!;
                        var mod = (double)rhs_value.Value!;
                        while (val < 0) val += mod;
                        while (val >= mod) val -= mod;
                        return new NumberValue(val);
                    case ExpressionOperator.Less:
                        if (rhs == null)
                            throw new Exceptions.NullOperandException("RHS", "Less");
                        if (lhs_value.Type == BaseValue.ValueType.Number && rhs_value!.Type == BaseValue.ValueType.Number)
                            return new NumberValue(((double)lhs_value.Value! < (double)rhs_value!.Value!) ? 1 : 0);
                        if (lhs_value.Type == BaseValue.ValueType.String && rhs_value!.Type == BaseValue.ValueType.String)
                            return new NumberValue(((string)lhs_value.Value!).CompareTo((string)rhs_value!.Value!) < 0 ? 1 : 0);
                        throw new Exceptions.InvalidOperationException($"Tried comparing {lhs_value.Type} value {lhs_value.Stringify().Value} to {rhs_value!.Type} value {rhs_value!.Stringify().Value}");
                    case ExpressionOperator.LessEqual:
                        if (rhs == null)
                            throw new Exceptions.NullOperandException("RHS", "LessEqual");
                        if (lhs_value.Type == BaseValue.ValueType.Number && rhs_value!.Type == BaseValue.ValueType.Number)
                            return new NumberValue(((double)lhs_value.Value! <= (double)rhs_value!.Value!) ? 1 : 0);
                        if (lhs_value.Type == BaseValue.ValueType.String && rhs_value!.Type == BaseValue.ValueType.String)
                            return new NumberValue(((string)lhs_value.Value!).CompareTo((string)rhs_value!.Value!) <= 0 ? 1 : 0);
                        throw new Exceptions.InvalidOperationException($"Tried comparting {lhs_value.Type} value {lhs_value.Stringify().Value} to {rhs_value!.Type} value {rhs_value!.Stringify().Value}");
                    case ExpressionOperator.Greater:
                        if (rhs == null)
                            throw new Exceptions.NullOperandException("RHS", "Greater");
                        if (lhs_value.Type == BaseValue.ValueType.Number && rhs_value!.Type == BaseValue.ValueType.Number)
                            return new NumberValue(((double)lhs_value.Value! > (double)rhs_value!.Value!) ? 1 : 0);
                        if (lhs_value.Type == BaseValue.ValueType.String && rhs_value!.Type == BaseValue.ValueType.String)
                            return new NumberValue(((string)lhs_value.Value!).CompareTo((string)rhs_value!.Value!) > 0 ? 1 : 0);
                        throw new Exceptions.InvalidOperationException($"Tried comparting {lhs_value.Type} value {lhs_value.Stringify().Value} to {rhs_value!.Type} value {rhs_value!.Stringify().Value}");
                    case ExpressionOperator.GreaterEqual:
                        if (rhs == null)
                            throw new Exceptions.NullOperandException("RHS", "GreaterEqual");
                        if (lhs_value.Type == BaseValue.ValueType.Number && rhs_value!.Type == BaseValue.ValueType.Number)
                            return new NumberValue(((double)lhs_value.Value! >= (double)rhs_value!.Value!) ? 1 : 0);
                        if (lhs_value.Type == BaseValue.ValueType.String && rhs_value!.Type == BaseValue.ValueType.String)
                            return new NumberValue(((string)lhs_value.Value!).CompareTo((string)rhs_value!.Value!) >= 0 ? 1 : 0);
                        throw new Exceptions.InvalidOperationException($"Tried comparting {lhs_value.Type} value {lhs_value.Stringify().Value} to {rhs_value!.Type} value {rhs_value!.Stringify().Value}");
                    case ExpressionOperator.Equal:
                        if (rhs == null)
                            throw new Exceptions.NullOperandException("RHS", "Equal");
                        return new NumberValue(lhs_value.Equals(rhs_value!) ? 1 : 0);
                    case ExpressionOperator.NotEqual:
                        if (rhs == null)
                            throw new Exceptions.NullOperandException("RHS", "NotEqual");
                        return new NumberValue(lhs_value.Equals(rhs_value!) ? 0 : 1);
                    case ExpressionOperator.LogicalNot:
                        return new NumberValue(lhs_value.HoldsTrue() ? 0 : 1);

                }
                return NilValue.Nil;
            }
        }

        public class Scope
        {
            bool isGlobalScope = false;
            Scope parentScope, globalScope;
            Dictionary<string, BaseValue> variables = new();

            // global scope settings
            int maxWhileLoops;
            public int MaxWhileLoops => globalScope.maxWhileLoops;
            public Scope(int _maxWhileLoops = 10000)
            {
                isGlobalScope = true;
                parentScope = this;
                globalScope = this;
                maxWhileLoops = _maxWhileLoops;
            }
            public Scope(Scope outerScope)
            {
                parentScope = outerScope;
                globalScope = outerScope.globalScope;
            }
            public Scope? GetScopeOfVariable(string var_name)
            {
                if (var_name.StartsWith('!'))
                    return this;
                if (var_name.StartsWith(':'))
                    return globalScope.GetScopeOfVariable(var_name[1..]);
                // try to find variable by traversing scopes
                Scope search = this;
                while (!search.variables.ContainsKey(var_name) && !search.isGlobalScope)
                    search = search.parentScope;
                if (!search.variables.ContainsKey(var_name))
                    return null; // couldn't find variable
                return search;
            }
            public BaseValue GetVariable(string var_name)
            {
                var var_scope = GetScopeOfVariable(var_name);
                if (var_scope == null)
                    return NilValue.Nil;
                if (var_name.StartsWith(':') || var_name.StartsWith('!'))
                    var_name = var_name[1..];
                return var_scope.variables[var_name];
            }
            public void SetVariable(string var_name, BaseValue value)
            {
                var var_scope = GetScopeOfVariable(var_name) ?? this;
                if (var_name.StartsWith(':') || var_name.StartsWith('!'))
                    var_name = var_name[1..];
                if (value.Type == BaseValue.ValueType.Nil)
                    var_scope.variables.Remove(var_name); // remove variable to free up memory
                else
                    var_scope.variables[var_name] = value;
            }
        }

        public abstract class VarResolver : IEvaluable
        {
            public abstract BaseValue Resolve(Scope currentScope);
            public abstract void Assign(Scope currentScope, BaseValue value);
            public override abstract string ToString();
            public BaseValue Evaluate(Scope currentScope) => Resolve(currentScope);
        }
        public class VarNameResolver : VarResolver
        {
            string var_name;
            public VarNameResolver(string var_name)
            {
                this.var_name = var_name;
            }
            public override BaseValue Resolve(Scope currentScope) => currentScope.GetVariable(var_name);
            public override void Assign(Scope currentScope, BaseValue value) => currentScope.SetVariable(var_name, value);
            public override string ToString() => $"VarNameResolver({var_name})";
        }
        public class VarObjectResolver : VarResolver
        {
            VarResolver base_var;
            IEvaluable child_name;
            public VarObjectResolver(VarResolver base_var, IEvaluable child_name)
            {
                this.base_var = base_var;
                this.child_name = child_name;
            }
            public override BaseValue Resolve(Scope currentScope)
            {
                var res = base_var.Resolve(currentScope);
                if (res.Type != BaseValue.ValueType.Object)
                    throw new Exceptions.VariableTypeException($"Base value {res.Value} is not object");
                return ((ObjectValue)res).GetChild((string)child_name.Evaluate(currentScope).Stringify().Value!);
            }
            public override void Assign(Scope currentScope, BaseValue value)
            {
                var res = base_var.Resolve(currentScope);
                if (res.Type != BaseValue.ValueType.Object)
                    throw new Exceptions.VariableTypeException($"Base value {res.Value} is not object");
                ((ObjectValue)res).SetChild((string)child_name.Evaluate(currentScope).Stringify().Value!, value);
            }
            public override string ToString() => $"VarObjectResolver({base_var} -> {child_name})";
        }

        public abstract class Instruction
        {
            public enum InstructionType
            {
                VarAssign,
                FunCall,
                IfBlocks,
                WhileBlocks,
                ForBlocks,
            }
            public InstructionType Type;
            public abstract void Execute(Scope currentScope);
        }
        public class VarAssignInstruction : Instruction
        {
            VarResolver var;
            IEvaluable value;
            public VarAssignInstruction(VarResolver var, IEvaluable value)
            {
                Type = InstructionType.VarAssign;
                this.var = var;
                this.value = value;
            }
            public override void Execute(Scope currentScope) => var.Assign(currentScope, value.Evaluate(currentScope));
        }
        public class FunCallInstruction : Instruction
        { // this instruction is used when ignoring return type
            VarResolver function;
            List<IEvaluable> parameters;
            public FunCallInstruction(VarResolver fun, List<IEvaluable> paramz)
            {
                Type = InstructionType.FunCall;
                function = fun;
                parameters = paramz;
            }
            public override void Execute(Scope currentScope)
            {
                var res = function.Resolve(currentScope);
                if (res.Type != BaseValue.ValueType.Function)
                    throw new Exceptions.VariableTypeException($"Tried calling non-function value {res.Stringify().Value}");
                var fn = (ICallable)res.Value!;
                // ensure parameter count
                if (fn.Head.Count != parameters.Count)
                    throw new Exceptions.InvalidParametersException($"Parameter count of {parameters.Count} doesn't match Head of {res.Stringify().Value}");
                // evaluate parameters
                List<BaseValue> eval = parameters.Select(x => x.Evaluate(currentScope)).ToList();
                // ensure parameter type
                for(int i = 0; i < parameters.Count; i++)
                {
                    if (fn.Head[i].Item2 == BaseValue.ValueType.AnyType) continue;
                    if (fn.Head[i].Item2 != eval[i].Type)
                        throw new Exceptions.InvalidParametersException($"Parameter {i} {eval[i].Stringify().Value} type {eval[i].Type} doesn't match Head {fn.Head[i]}");
                }
                // scope the function
                Scope fnScope = new(currentScope);
                // pass the arguments on the scope
                for (int i = 0; i < parameters.Count; i++)
                    fn.Head[i].Item1.Assign(fnScope, eval[i]);
                // call the function
                fn.Call(fnScope);
            }
        }
        public class IfBlocksInstruction : Instruction
        {
            IEvaluable condition;
            List<Instruction> trueBlock;
            List<Instruction>? falseBlock;
            public IfBlocksInstruction(IEvaluable condition, List<Instruction> trueBlock, List<Instruction>? falseBlock)
            {
                Type = InstructionType.IfBlocks;
                this.condition = condition;
                this.trueBlock = trueBlock;
                this.falseBlock = falseBlock;
            }
            public override void Execute(Scope currentScope)
            {
                if(condition.Evaluate(currentScope).HoldsTrue())
                {
                    // scope block
                    Scope blockScope = new Scope(currentScope);
                    foreach (var instr in trueBlock)
                        instr.Execute(blockScope);
                }
                else if(falseBlock != null)
                {
                    // scope block
                    Scope blockScope = new Scope(currentScope);
                    foreach (var instr in falseBlock)
                        instr.Execute(blockScope);
                }
            }
        }
        public class WhileBlocksInstruction : Instruction
        {
            IEvaluable condition;
            List<Instruction> instructions;
            public WhileBlocksInstruction(IEvaluable condition, List<Instruction> instructions)
            {
                Type = InstructionType.WhileBlocks;
                this.condition = condition;
                this.instructions = instructions;
            }
            public override void Execute(Scope currentScope)
            {
                int loopCount = 0;
                while(condition.Evaluate(currentScope).HoldsTrue())
                {
                    // scope block
                    Scope blockScope = new Scope(currentScope);
                    foreach (var instr in instructions)
                        instr.Execute(blockScope);
                    if(++loopCount > currentScope.MaxWhileLoops)
                        throw new Exceptions.IterationLoopException(currentScope.MaxWhileLoops);
                }
            }
        }
        public class ForBlocksInstruction : Instruction
        {
            VarResolver iter_var;
            IEvaluable iter_array;
            List<Instruction> instructions;
            public ForBlocksInstruction(VarResolver iter_var, IEvaluable iter_array, List<Instruction> instructions)
            {
                Type = InstructionType.ForBlocks;
                this.iter_var = iter_var;
                this.iter_array = iter_array;
                this.instructions = instructions;
            }
            public override void Execute(Scope currentScope)
            {
                // evaluate array which needs to be iterrated
                var iter_val = iter_array.Evaluate(currentScope);
                // check that the value is object
                if (iter_val.Type != BaseValue.ValueType.Object)
                    throw new Exceptions.VariableTypeException($"Tried running for loop over non-object value {iter_val.Stringify().Value}");
                // check that the value is array-convention
                var array = (ObjectValue)iter_val;
                if(!array.IsArrayConvention())
                    throw new Exceptions.VariableTypeException($"Tried running for loop over non-array-covention value {iter_val.Stringify().Value}");
                var length = (int)(double)array.GetChild("length").Value!;
                for(int i = 0; i < length; i++)
                {
                    // scope the block
                    Scope blockScope = new Scope(currentScope);
                    // inject iterator variable
                    iter_var.Assign(blockScope, array.GetChild(i.ToString()));
                    foreach (var instr in instructions)
                        instr.Execute(blockScope);
                }
            }
        }
    }

    namespace Parser
    {
        public class Token
        {
            public Token(string val, TokenType type = TokenType.Unknown)
            {
                Value = val;
                Type = type;
            }

            public string Value;
            public TokenType Type;

            public enum TokenType
            {
                Unknown,
                Operator,
                Constant,
                Variable,
                RoundBracket,
                SquareBracket,
                Comma,
                EmptyObject,
                Assign,
                Keyword,
                VarType,
                NewLine,
                // type used at blocking
                UnaryOperator,
            }

            public static void PrintTokens(List<Token> tokens)
            {
                Console.WriteLine("--- TOKENS ----");
                var maxLen = tokens.MaxBy(x => x.Value.Length)!.Value.Length;
                foreach (var token in tokens)
                    Console.WriteLine(token.Value.PadRight(maxLen) + " " + (token.Type == TokenType.Unknown ? "!! UNKNOWN !!" : token.Type));
                Console.WriteLine("---------------");
            }

            public override string ToString()
            {
                return $"Token({Type} {Value})";
            }
        }

        public static class ProgramCleaner
        {
            // prepare the program to be tokenized
            public static string CleanProgram(string program)
            {
                // collapse multi-line instructions
                program = program.ReplaceLineEndings("\n").Replace("\\\n", "");
                List<string> lines = [];
                foreach (var line in program.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith('#'))
                        continue; // remove comments
                    if (trimmed.Length == 0)
                        continue; // remove empty lines
                    lines.Add(line);
                }
                // remove in-line comments
                for (int i = 0; i < lines.Count; i++)
                {
                    if (!lines[i].Contains('#'))
                        continue;
                    int last_comm = lines[i].LastIndexOf('#');
                    var last_str = lines[i].LastIndexOf('"');
                    if (last_comm > last_str)
                        lines[i] = lines[i][..last_comm];
                }
                return string.Join("\n", [.. lines]);
            }
        }

        public static partial class Tokenizer
        {
            public static List<Token> Tokenize(string program)
            {
                List<Token> tokens = [new(program)];

                tokens = SplitByLines(tokens);
                tokens = ExtractStrings(tokens);
                tokens = ExtractVariables(tokens);
                tokens = ExtractKeywords(tokens);
                tokens = ExtractOperators(tokens);
                tokens = ExtractNumbers(tokens);

                if (tokens.Where(x => x.Type == Token.TokenType.Unknown).Any())
                    throw new Exceptions.UnknownTokenException("Found unknown token " + tokens.Where(x => x.Type == Token.TokenType.Unknown).First().Value);

                return tokens;
            }

            static List<Token> CleanTokens(List<Token> oldTokens)
            {
                List<Token> tokens = [];
                foreach (var token in oldTokens)
                {
                    if (token.Type == Token.TokenType.NewLine)
                    {
                        // treat new lines separately
                        tokens.Add(new("", Token.TokenType.NewLine));
                        continue;
                    }
                    if (token.Value.Trim().Length == 0) continue;
                    tokens.Add(new(token.Value.Trim(), token.Type));
                }
                return tokens;
            }

            static List<Token> SplitByLines(List<Token> oldTokens)
            {
                List<Token> tokens = [];
                foreach (var token in oldTokens)
                {
                    if (token.Type != Token.TokenType.Unknown)
                    {
                        tokens.Add(token);
                        continue;
                    }
                    var tk = token.Value;
                    while (tk.Contains('\n'))
                    {
                        var idx = tk.IndexOf('\n');
                        tokens.Add(new(tk[..idx]));
                        tokens.Add(new("", Token.TokenType.NewLine));
                        tk = tk[(idx + 1)..];
                    }
                    tokens.Add(new(tk));
                }
                // clean successive line breaks
                for (int i = tokens.Count - 1; i > 0; --i)
                    if (tokens[i].Type == Token.TokenType.NewLine && tokens[i - 1].Type == Token.TokenType.NewLine)
                        tokens.RemoveAt(i);
                return CleanTokens(tokens);
            }

            static List<Token> ExtractStrings(List<Token> oldTokens)
            {
                List<Token> tokens = [];
                foreach (var token in oldTokens)
                {
                    if (token.Type != Token.TokenType.Unknown)
                    {
                        tokens.Add(token);
                        continue;
                    }
                    var tk = token.Value;
                    while (tk.Contains('"'))
                    {
                        var idx = tk.IndexOf('"');
                        tokens.Add(new(tk[..idx]));
                        ++idx;
                        while (idx < tk.Length && tk[idx] != '"')
                        {
                            if (tk[idx] == '\\')
                                ++idx;
                            ++idx;
                        }
                        tokens.Add(new(tk.Substring(tk.IndexOf('"'), idx - tk.IndexOf('"') + 1), Token.TokenType.Constant));
                        tk = tk[(idx + 1)..];
                    }
                    tokens.Add(new(tk));
                }
                return CleanTokens(tokens);
            }

            static List<Token> ExtractVariables(List<Token> oldTokens)
            {
                List<Token> tokens = [];
                foreach (var token in oldTokens)
                {
                    if (token.Type != Token.TokenType.Unknown)
                    { // token is final
                        tokens.Add(token);
                        continue;
                    }
                    string tk = token.Value;
                    while (tk.Contains('$'))
                    {
                        var idx = tk.IndexOf('$');
                        tokens.Add(new(tk[..idx]));
                        while (idx < tk.Length && tk[idx].IsVarName())
                            ++idx;
                        tokens.Add(new(tk[tk.IndexOf('$')..idx], Token.TokenType.Variable));
                        tk = tk[idx..];
                    }
                    tokens.Add(new(tk));
                }
                return CleanTokens(tokens);
            }

            static readonly string[] VarTypes = ["number", "string", "function", "object", "nil"]; // nil is keyword for NilValue.Nil
            static readonly string[] Keywords = ["fun", "nfu", "if", "el", "fi", "while", "ewhil", "for", "rfo", "return", "continue", .. VarTypes];
            static List<Token> ExtractKeywords(List<Token> oldTokens)
            {
                List<Token> tokens = [];
                foreach (var token in oldTokens)
                {
                    if (token.Type != Token.TokenType.Unknown)
                    {
                        tokens.Add(token);
                        continue;
                    }
                    var tk = token.Value;
                    string firstAp;
                    while ((firstAp = tk.ContainsAny(Keywords)) != "")
                    {
                        var idx = tk.IndexOf(firstAp);
                        tokens.Add(new(tk[..idx]));
                        if (VarTypes.Contains(firstAp))
                            tokens.Add(new(firstAp, Token.TokenType.VarType));
                        else
                            tokens.Add(new(firstAp, Token.TokenType.Keyword));
                        tk = tk[(idx + firstAp.Length)..];
                    }
                    tokens.Add(new(tk));
                }
                return CleanTokens(tokens);
            }

            static List<Token> ExtractOperators(List<Token> oldTokens) => ExtractOperatorsOneChar(ExtractOperatorsTwoChars(oldTokens));
            static readonly string[] OperatorsTwoChars = ["<=", ">=", "==", "!=", "{}" /*empty object*/];
            static List<Token> ExtractOperatorsTwoChars(List<Token> oldTokens)
            {
                List<Token> tokens = [];
                foreach (var token in oldTokens)
                {
                    if (token.Type != Token.TokenType.Unknown)
                    {
                        tokens.Add(token);
                        continue;
                    }
                    var tk = token.Value;
                    string firstAp;
                    while ((firstAp = tk.ContainsAny(OperatorsTwoChars)) != "")
                    {
                        var idx = tk.IndexOf(firstAp);
                        tokens.Add(new(tk[..idx]));
                        tokens.Add(new(firstAp, firstAp == "{}" ? Token.TokenType.EmptyObject : Token.TokenType.Operator));
                        tk = tk[(idx + 2)..];
                    }
                    tokens.Add(new(tk));
                }
                return CleanTokens(tokens);
            }
            static readonly string[] OperatorsOneChar = ["+", "-", "*", "/", "%", "(", ")", ",", "!", "<", ">", "[", "]", "="];
            static List<Token> ExtractOperatorsOneChar(List<Token> oldTokens)
            {
                List<Token> tokens = [];
                foreach (var token in oldTokens)
                {
                    if (token.Type != Token.TokenType.Unknown)
                    {
                        tokens.Add(token);
                        continue;
                    }
                    var tk = token.Value;
                    string firstAp;
                    while ((firstAp = tk.ContainsAny(OperatorsOneChar)) != "")
                    {
                        var idx = tk.IndexOf(firstAp);
                        tokens.Add(new(tk[..idx]));
                        if (firstAp == "(" || firstAp == ")")
                            tokens.Add(new(firstAp, Token.TokenType.RoundBracket));
                        else if (firstAp == "[" || firstAp == "]")
                            tokens.Add(new(firstAp, Token.TokenType.SquareBracket));
                        else if (firstAp == "=")
                            tokens.Add(new(firstAp, Token.TokenType.Assign));
                        else if (firstAp == ",")
                            tokens.Add(new(firstAp, Token.TokenType.Comma));
                        else
                            tokens.Add(new(firstAp, Token.TokenType.Operator));
                        tk = tk[(idx + 1)..];
                    }
                    tokens.Add(new(tk));
                }
                return CleanTokens(tokens);
            }

            [GeneratedRegex(@"(\d+(\.\d+)?)|(\.\d+)")]
            private static partial Regex NumberRegex();

            static List<Token> ExtractNumbers(List<Token> oldTokens)
            {
                List<Token> tokens = [];
                foreach (var token in oldTokens)
                {
                    if (token.Type != Token.TokenType.Unknown)
                    {
                        tokens.Add(token);
                        continue;
                    }
                    var tk = token.Value;
                    while (NumberRegex().Count(tk) > 0)
                    {
                        var match = NumberRegex().Match(tk);
                        tokens.Add(new(tk[..match.Index]));
                        tokens.Add(new(match.Value, Token.TokenType.Constant));
                        tk = tk[(match.Index + match.Length)..];
                    }
                    tokens.Add(new(tk));
                }
                return CleanTokens(tokens);
            }
        }

        public abstract class Block
        {
            public enum BlockType
            {
                BaseToken,
                // ->
                Constant,
                Variable,
                FunctionCall,
                // ->
                Evaluable,
                // ->
                Instructions,
            }
            public BlockType Type;
            public override string ToString() => ToString(0);
            public abstract string ToString(int indent);
            public delegate List<Block> extractorFunction(List<Block> blocks);
            public void IfHasBlocksExtract(extractorFunction fn)
            {
                if (Type == BlockType.FunctionCall)
                {
                    var fnBlock = (FunctionCallBlock)this;
                    List<List<Block>> newArgs = new();
                    foreach (var argList in fnBlock.args)
                        newArgs.Add(fn(argList));
                    fnBlock.args = newArgs;
                    return;
                }
                if (Type == BlockType.Evaluable)
                {
                    var evBlock = (EvaluableBlock)this;
                    evBlock.components = fn(evBlock.components);
                    return;
                }
            }
        }
        public class BaseTokenBlock : Block
        {
            public Token baseToken;
            public BaseTokenBlock(Token baseToken)
            {
                Type = BlockType.BaseToken;
                this.baseToken = baseToken;
            }
            public override string ToString(int indent) => $"BaseTokenBlock({baseToken})".Indent(indent);
        }
        public class ConstantBlock : Block
        {
            public BaseValue constValue;
            public ConstantBlock(Token baseToken)
            {
                Type = BlockType.Constant;
                var value = baseToken.Value;
                if(value.StartsWith('"'))
                {
                    // extract string
                    value = value[1..^1];
                    // parse escape characters
                    StringBuilder sb = new();
                    for(int i = 0; i < value.Length; i++)
                    {
                        if (value[i] != '\\')
                        {
                            sb.Append(value[i]);
                            continue;
                        }
                        if (value[i + 1] == '\\' || value[i + 1] == '"')
                        {
                            sb.Append(value[i + 1]);
                            i++;
                            continue;
                        }
                        if (value[i + 1] == 'n')
                        {
                            sb.Append('\n');
                            i++;
                            continue;
                        }
                        if (value[i + 1] == 'x')
                        {
                            // check that we have enough characters
                            if (i + 3 >= value.Length)
                                throw new Exceptions.InvalidStringFormatException($"Tried escaping ASCII code but string was too short '{value}'");
                            uint asciiCode = Convert.ToUInt32(value.Substring(i + 2, 2), 16);
                            sb.Append((char)asciiCode);
                            i += 3;
                            continue;
                        }
                        throw new Exceptions.InvalidStringFormatException($"Unknown string escape code {value[i + 1]} in string '{value}'");
                    }
                    constValue = new StringValue(sb.ToString());
                }
                else
                {
                    constValue = new NumberValue(double.Parse(value));
                }
            }
            public override string ToString(int indent) => $"ConstantBlock({constValue})".Indent(indent);
        }
        public class VariableBlock : Block
        {
            public VarResolver resolver;
            public VariableBlock(VarResolver resolver)
            {
                Type = BlockType.Variable;
                this.resolver = resolver;
            }
            public override string ToString(int indent) => $"VariableBlock({resolver})".Indent(indent);
        }
        public class FunctionCallBlock : Block, IEvaluable
        {
            public VarResolver function;
            public List<List<Block>> args;
            public FunctionCallBlock(VarResolver function, List<Block> betweenBrackets)
            {
                Type = BlockType.FunctionCall;
                this.function = function;
                args = new();
                if (betweenBrackets.Count == 0)
                    return;
                List<Block> currentArg = new();
                int depth = 0;
                foreach(var arg in betweenBrackets)
                {
                    if(arg.Type == BlockType.BaseToken)
                    {
                        var baseTk = ((BaseTokenBlock)arg).baseToken;
                        if (baseTk.Type == Token.TokenType.RoundBracket)
                        {
                            if (baseTk.Value == "(")
                                ++depth;
                            else
                                --depth;
                        }
                        if(baseTk.Type == Token.TokenType.Comma && depth == 0)
                        {
                            args.Add(currentArg);
                            currentArg = new();
                            continue;
                        }
                    }
                    currentArg.Add(arg);
                }
                args.Add(currentArg);
            }
            public override string ToString(int indent)
            {
                string ret = $"FunctionCallBlock({function}, (".Indent(indent);
                bool firstArg = true;
                foreach(var argList in args)
                {
                    if (firstArg)
                    {
                        ret += "\n";
                        firstArg = false;
                    }
                    ret += "(".Indent(indent + 2);
                    bool fa2 = true;
                    foreach (var arg in argList)
                    {
                        if(fa2)
                        {
                            ret += "\n";
                            fa2 = false;
                        }
                        ret += arg.ToString(indent + 4) + ",\n";
                    }
                    ret += "),\n".Indent(indent + 2);
                }
                return ret + (ret.EndsWith('\n') ? "))".Indent(indent) : "))");
            }
            public BaseValue Evaluate(Scope currentScope)
            {
                FunctionValue fn = (FunctionValue)function.Resolve(currentScope);
                var callable = (ICallable)fn.Value!;
                callable.
            }
        }

        public class EvaluableBlock : Block
        {
            public List<Block> components;
            public EvaluableBlock(List<Block> components)
            {
                Type = BlockType.Evaluable;
                this.components = components;
            }
            public override string ToString(int indent)
            {
                string ret = "EvaluableBlock(\n".Indent(indent);
                foreach (var comp in components)
                    ret += comp.ToString(indent + 2) + ",\n";
                return ret + ")".Indent(indent);
            }
        }

        public class UnaryOperatorBlock : Block
        {
            public Token op;
            public UnaryOperatorBlock(Token op)
            {
                Type = BlockType.Instructions;
                this.op = op;
            }
            public override string ToString(int indent) => $"UnaryOperatorBlock({op})".Indent(indent);
        }

        public static class Blocker
        {
            public static List<Block> BlockTokens(List<Token> tokens)
            {
                int maxDepthRound = 0, maxDepthSquare = 0, maxDepthAll = 0;
                int depthRound = 0, depthSquare = 0, depthAll = 0;
                foreach(var token in tokens)
                {
                    if (token.Type == Token.TokenType.RoundBracket)
                    {
                        if (token.Value == "(")
                        {
                            ++depthRound;
                            if (depthRound > maxDepthRound)
                                maxDepthRound = depthRound;
                            ++depthAll;
                            if (depthAll > maxDepthAll)
                                maxDepthAll = depthAll;
                        }
                        else
                        {
                            --depthRound;
                            --depthAll;
                        }
                    }
                    if(token.Type == Token.TokenType.SquareBracket)
                    {
                        if (token.Value == "[")
                        {
                            ++depthSquare;
                            if (depthSquare > maxDepthSquare)
                                maxDepthSquare = depthSquare;
                            ++depthAll;
                            if (depthAll > maxDepthAll)
                                maxDepthAll = depthAll;
                        }
                        else
                        {
                            --depthSquare;
                            --depthAll;
                        }
                    }
                }

                List<Block> blocks = tokens.Select(x => (Block)new BaseTokenBlock(x)).ToList();

                blocks = ExtractConstants(blocks);
                blocks = ExtractSimpleVariables(blocks);
                blocks = ExtractFunctionCalls(blocks);
                blocks = ConvertSimpleEvaluables(blocks);

                for(int i = 0; i <= maxDepthRound; i++)
                {
                    blocks = ExtractUnaryOperators(blocks);
                    blocks = ExtractEvaluables(blocks);
                    blocks = ReExtractUnaryOperators(blocks);
                }

                for (int i = 0; i <= maxDepthSquare; i++)
                    blocks = ExtractComplexVariables(blocks);
                //blocks = ReConvertSimpleEvaluables(blocks);

                return blocks;
            }

            static List<Block> ExtractConstants(List<Block> oldBlocks)
            {
                List<Block> blocks = new();
                foreach(var block in oldBlocks)
                {
                    block.IfHasBlocksExtract(ExtractConstants);
                    if(block.Type != Block.BlockType.BaseToken)
                    {
                        blocks.Add(block);
                        continue;
                    }
                    var crnt = (BaseTokenBlock)block;
                    if(crnt.baseToken.Type != Token.TokenType.Constant)
                    {
                        blocks.Add(block);
                        continue;
                    }
                    blocks.Add(new ConstantBlock(crnt.baseToken));
                }
                return blocks;
            }

            static VarResolver ResolverFromVariableName(string var_name)
            {
                var_name = var_name[1..]; // remove '$'
                // simple name variable
                if (!var_name.Contains(':') || var_name.LastIndexOf(':') == 0)
                    return new VarNameResolver(var_name);
                // object variable
                int idx = var_name.IndexOf(':', 1);
                VarResolver objResolver = new VarNameResolver(var_name[..idx]);
                var_name = var_name[(idx + 1)..];
                while (var_name.Contains(':'))
                {
                    idx = var_name.IndexOf(':');
                    objResolver = new VarObjectResolver(objResolver, new StringValue(var_name[..idx]));
                    var_name = var_name[(idx + 1)..];
                }
                objResolver = new VarObjectResolver(objResolver, new StringValue(var_name));
                return objResolver;
            }
            static List<Block> ExtractSimpleVariables(List<Block> oldBlocks)
            {
                List<Block> blocks = new();
                foreach(var block in oldBlocks)
                {
                    block.IfHasBlocksExtract(ExtractSimpleVariables);
                    if (block.Type != Block.BlockType.BaseToken)
                    {
                        blocks.Add(block);
                        continue;
                    }
                    var crnt = (BaseTokenBlock)block;
                    if(crnt.baseToken.Type != Token.TokenType.Variable)
                    {
                        blocks.Add(block);
                        continue;
                    }
                    // convert variable names to blocks
                    blocks.Add(new VariableBlock(ResolverFromVariableName(crnt.baseToken.Value)));
                }
                return blocks;
            }

            static List<Block> ExtractFunctionCalls(List<Block> oldBlocks)
            {
                List<Block> blocks = new();
                for(int i = 0; i < oldBlocks.Count; i++)
                {
                    if (oldBlocks[i].Type != Block.BlockType.Variable)
                    {
                        blocks.Add(oldBlocks[i]);
                        continue; // function call can only be performed on a variable
                    }
                    if (i + 1 >= oldBlocks.Count)
                    {
                        blocks.Add(oldBlocks[i]);
                        continue; // out of range
                    }
                    if (oldBlocks[i + 1].Type != Block.BlockType.BaseToken || ((BaseTokenBlock)oldBlocks[i + 1]).baseToken.Type != Token.TokenType.RoundBracket || ((BaseTokenBlock)oldBlocks[i + 1]).baseToken.Value != "(")
                    {
                        blocks.Add(oldBlocks[i]);
                        continue; // not function call
                    }
                    // find matching ')'
                    int idx = i + 2;
                    int depth = 1;
                    while(depth > 0 && idx < oldBlocks.Count)
                    {
                        if (oldBlocks[idx].Type != Block.BlockType.BaseToken)
                        {
                            ++idx;
                            continue;
                        }
                        var tkBlock = (BaseTokenBlock)oldBlocks[idx];
                        if(tkBlock.baseToken.Type != Token.TokenType.RoundBracket)
                        {
                            ++idx;
                            continue;
                        }
                        if (tkBlock.baseToken.Value == "(")
                            ++depth;
                        else
                            --depth;
                        ++idx;
                    }
                    blocks.Add(new FunctionCallBlock(((VariableBlock)oldBlocks[i]).resolver, oldBlocks[(i + 2)..(idx - 1)]));
                    blocks.Last().IfHasBlocksExtract(ExtractFunctionCalls);
                    i = idx - 1;
                }
                return blocks;
            }

            static List<Block> ConvertSimpleEvaluables(List<Block> oldBlocks)
            {
                List<Block> blocks = new();
                foreach(var block in oldBlocks)
                {
                    block.IfHasBlocksExtract(ConvertSimpleEvaluables);
                    if (block.Type == Block.BlockType.Constant || block.Type == Block.BlockType.FunctionCall)
                        blocks.Add(new EvaluableBlock([block]));
                    else
                        blocks.Add(block);
                }
                return blocks;
            }

            static List<Block> ExtractUnaryOperators(List<Block> oldBlocks)
            {
                List<Block> blocks = new();
                // unary operators can be applied if:
                // \null + a -> [+a]
                // (not: ev, ')') + a -> [+a]
                bool shouldReExtract = false;
                for(int i = 0; i < oldBlocks.Count; i++)
                {
                    if (oldBlocks[i].Type != Block.BlockType.BaseToken || i + 1 >= oldBlocks.Count)
                    {
                        oldBlocks[i].IfHasBlocksExtract(ExtractUnaryOperators);
                        blocks.Add(oldBlocks[i]);
                        continue;
                    }
                    var baseTk = ((BaseTokenBlock)oldBlocks[i]).baseToken;
                    if(baseTk.Type != Token.TokenType.Operator)
                    {
                        blocks.Add(oldBlocks[i]);
                        continue;
                    }
                    if(baseTk.Value != "+" && baseTk.Value != "-" && baseTk.Value != "!")
                    { // this operator can't be unary
                        blocks.Add(oldBlocks[i]);
                        continue;
                    }
                    // operator is binary
                    if(i > 0 && (oldBlocks[i - 1].Type == Block.BlockType.Evaluable || oldBlocks[i - 1].Type == Block.BlockType.BaseToken && ((BaseTokenBlock)oldBlocks[i - 1]).baseToken.Type == Token.TokenType.RoundBracket && ((BaseTokenBlock)oldBlocks[i - 1]).baseToken.Value == ")" || oldBlocks[i - 1].Type == Block.BlockType.BaseToken && ((BaseTokenBlock)oldBlocks[i - 1]).baseToken.Type == Token.TokenType.SquareBracket && ((BaseTokenBlock)oldBlocks[i - 1]).baseToken.Value == "]"))
                    {
                        blocks.Add(oldBlocks[i]);
                        continue;
                    }
                    if (oldBlocks[i + 1].Type == Block.BlockType.Evaluable)
                    {
                        // mark as UnaryOperator, integrate into evaluable
                        ((BaseTokenBlock)oldBlocks[i]).baseToken.Type = Token.TokenType.UnaryOperator;
                        blocks.Add(new EvaluableBlock([oldBlocks[i], .. ((EvaluableBlock)oldBlocks[i + 1]).components]));
                        i++;
                        shouldReExtract = true;
                        continue;
                    }
                    if(oldBlocks[i + 1].Type == Block.BlockType.BaseToken && ((BaseTokenBlock)oldBlocks[i + 1]).baseToken.Type == Token.TokenType.RoundBracket && ((BaseTokenBlock)oldBlocks[i + 1]).baseToken.Value == "(" || oldBlocks[i + 1].Type == Block.BlockType.BaseToken && ((BaseTokenBlock)oldBlocks[i + 1]).baseToken.Type == Token.TokenType.SquareBracket && ((BaseTokenBlock)oldBlocks[i + 1]).baseToken.Value == "[")
                    {
                        // mark token as UnaryOperator, will be later picked up by re-extracting unary operators
                        ((BaseTokenBlock)oldBlocks[i]).baseToken.Type = Token.TokenType.UnaryOperator;
                        blocks.Add(oldBlocks[i]);
                        continue;
                    }
                    blocks.Add(oldBlocks[i]);
                }
                if (shouldReExtract)
                    return ExtractUnaryOperators(blocks);
                return blocks;
            }

            static List<Block> ExtractEvaluables(List<Block> oldBlocks)
            {
                List<Block> blocks = new();
                // evaluables can be extended if:
                // (a) -> a
                // a + b -> [a+b]
                // a,b - evaluable ; + - operator ; [a+b] -> new evaluable from a, +, b
                bool shouldReExtract = false;
                for(int i = 0; i < oldBlocks.Count; i++)
                {
                    oldBlocks[i].IfHasBlocksExtract(ExtractEvaluables);
                    if(i + 2 >= oldBlocks.Count)
                    {
                        blocks.Add(oldBlocks[i]);
                        continue;
                    }
                    // (a) -> [(a)]
                    if (oldBlocks[i].Type == Block.BlockType.BaseToken && ((BaseTokenBlock)oldBlocks[i]).baseToken.Type == Token.TokenType.RoundBracket && ((BaseTokenBlock)oldBlocks[i]).baseToken.Value == "(" &&
                        oldBlocks[i + 1].Type == Block.BlockType.Evaluable &&
                        oldBlocks[i + 2].Type == Block.BlockType.BaseToken && ((BaseTokenBlock)oldBlocks[i + 2]).baseToken.Type == Token.TokenType.RoundBracket && ((BaseTokenBlock)oldBlocks[i + 2]).baseToken.Value == ")")
                    {
                        blocks.Add(new EvaluableBlock([oldBlocks[i], .. ((EvaluableBlock)oldBlocks[i + 1]).components, oldBlocks[i + 2]]));
                        i += 2;
                        shouldReExtract = true;
                        continue;
                    }
                    // a + b -> [a+b]
                    if (oldBlocks[i].Type == Block.BlockType.Evaluable &&
                        oldBlocks[i + 1].Type == Block.BlockType.BaseToken && ((BaseTokenBlock)oldBlocks[i + 1]).baseToken.Type == Token.TokenType.Operator &&
                        oldBlocks[i + 2].Type == Block.BlockType.Evaluable)
                    {
                        blocks.Add(new EvaluableBlock([.. ((EvaluableBlock)oldBlocks[i]).components, oldBlocks[i + 1], .. ((EvaluableBlock)oldBlocks[i + 2]).components]));
                        i += 2;
                        shouldReExtract = true;
                        continue;
                    }
                    blocks.Add(oldBlocks[i]);
                }
                if(shouldReExtract)
                    return ExtractEvaluables(blocks);
                return blocks;
            }

            static List<Block> ReExtractUnaryOperators(List<Block> oldBlocks)
            {
                List<Block> blocks = new();
                bool shouldReExtract = false;
                for(int i = 0; i < oldBlocks.Count; i++)
                {
                    if (oldBlocks[i].Type != Block.BlockType.BaseToken)
                    {
                        oldBlocks[i].IfHasBlocksExtract(ReExtractUnaryOperators);
                        blocks.Add(oldBlocks[i]);
                        continue;
                    }
                    var baseTk = (BaseTokenBlock)oldBlocks[i];
                    if(baseTk.baseToken.Type != Token.TokenType.UnaryOperator)
                    {
                        blocks.Add(oldBlocks[i]);
                        continue;
                    }
                    if (oldBlocks[i + 1].Type != Block.BlockType.Evaluable)
                    {
                        blocks.Add(oldBlocks[i]);
                        continue; // multiple unary operators
                    }
                    shouldReExtract = true;
                    blocks.Add(new EvaluableBlock([oldBlocks[i], .. ((EvaluableBlock)oldBlocks[i + 1]).components]));
                    i++;
                    continue;
                }
                if (shouldReExtract)
                    return ReExtractUnaryOperators(blocks);
                return blocks;
            }

            static List<Block> ExtractComplexVariables(List<Block> oldBlocks)
            {
                // varblock '[' evalblock ']' -> varblock
                // varblock '[' varblock ']'  -> varblock
                bool condition(int i, Block.BlockType name_type, bool bypassFirst = false)
                {
                    if (i + 3 >= oldBlocks.Count)
                        return false;
                    return
                        (oldBlocks[i].Type == Block.BlockType.Variable || bypassFirst) &&
                        oldBlocks[i + 1].Type == Block.BlockType.BaseToken && ((BaseTokenBlock)oldBlocks[i + 1]).baseToken.Type == Token.TokenType.SquareBracket && ((BaseTokenBlock)oldBlocks[i + 1]).baseToken.Value == "[" &&
                        oldBlocks[i + 2].Type == name_type &&
                        oldBlocks[i + 3].Type == Block.BlockType.BaseToken && ((BaseTokenBlock)oldBlocks[i + 3]).baseToken.Type == Token.TokenType.SquareBracket && ((BaseTokenBlock)oldBlocks[i + 3]).baseToken.Value == "]";
                }
                List<Block> blocks = new();
                for(int i = 0; i < oldBlocks.Count; i++)
                {
                    oldBlocks[i].IfHasBlocksExtract(ExtractComplexVariables);
                    if(i + 3 >=  oldBlocks.Count)
                    {
                        blocks.Add(oldBlocks[i]);
                        continue;
                    }
                    VariableBlock? prevBlock = null;
                    if (condition(i, Block.BlockType.Evaluable, prevBlock != null) || condition(i, Block.BlockType.Variable, prevBlock != null))
                    {
                        while (condition(i, Block.BlockType.Evaluable, prevBlock != null) || condition(i, Block.BlockType.Variable, prevBlock != null))
                        {
                            while (condition(i, Block.BlockType.Evaluable, prevBlock != null))
                            {
                                if (prevBlock != null)
                                    prevBlock = new VariableBlock(new VarObjectResolver(prevBlock.resolver, ExpressionParser.ParseEvaluable(((EvaluableBlock)oldBlocks[i + 2]).components)));
                                else
                                    prevBlock = new VariableBlock(new VarObjectResolver(((VariableBlock)oldBlocks[i]).resolver, ExpressionParser.ParseEvaluable(((EvaluableBlock)oldBlocks[i + 2]).components)));
                                i += 3;
                                continue;
                            }
                            while (condition(i, Block.BlockType.Variable, prevBlock != null))
                            {
                                if (prevBlock != null)
                                    prevBlock = new VariableBlock(new VarObjectResolver(prevBlock.resolver, ((VariableBlock)oldBlocks[i + 2]).resolver));
                                else
                                    prevBlock = new VariableBlock(new VarObjectResolver(((VariableBlock)oldBlocks[i]).resolver, ((VariableBlock)oldBlocks[i + 2]).resolver));
                                i += 3;
                                continue;
                            }
                        }
                        blocks.Add(prevBlock!);
                        continue;
                    }
                    blocks.Add(oldBlocks[i]);
                }
                return blocks;
            }
        }

        public static class ExpressionParser
        {
            public static IEvaluable ParseEvaluable(List<Block> blocks)
            {
                if (blocks.Count == 1)
                {
                    if(blocks[0].Type == Block.BlockType.Constant)
                        return ((ConstantBlock)blocks[0]).constValue;
                    if (blocks[0].Type == Block.BlockType.FunctionCall)
                        return ((FunctionCallBlock)blocks[0]).
                }
                // find top-level operators
                List<Token> operators = new();
                int depth = 0;
                foreach(var block in blocks)
                {
                    if (block.Type != Block.BlockType.BaseToken)
                        continue;
                    var baseTk = ((BaseTokenBlock)block).baseToken;
                    if(baseTk.Type == Token.TokenType.RoundBracket)
                    {
                        if (baseTk.Value == "(")
                            ++depth;
                        else
                            --depth;
                    }
                    if (baseTk.Type == Token.TokenType.Operator && depth == 0)
                        operators.Add(baseTk);
                }
                return null;
            }
        }
    }

    namespace Exceptions
    {
        [Serializable]
        public class InvalidOperationException : Exception
        {
            public InvalidOperationException(string message) : base(message) { }
        }

        [Serializable]
        public class NullOperandException : Exception
        {
            public NullOperandException(string side, string op) : base(side + " value of operator " + op + " is null") { }
        }

        [Serializable]
        public class UnknownTokenException : Exception
        {
            public UnknownTokenException(string message) : base(message) { }
        }

        [Serializable]
        public class VariableTypeException : Exception
        {
            public VariableTypeException(string message) : base(message) { }
        }

        [Serializable]
        public class InvalidParametersException : Exception
        {
            public InvalidParametersException(string message) : base(message) { }
        }

        [Serializable]
        public class IterationLoopException : Exception
        {
            public IterationLoopException(int maxTimes) : base("Loop ran more than " + maxTimes + " times") { }
        }

        [Serializable]
        public class InvalidStringFormatException : Exception
        {
            public InvalidStringFormatException(string message) : base(message) { }
        }

        [Serializable]
        public class UnexpectedTokenException : Exception
        {
            public UnexpectedTokenException(string message) : base(message) { }
        }

        [Serializable]
        public class InvalidUnaryOperatorException : Exception
        {
            public InvalidUnaryOperatorException(string message) : base(message) { }
        }
    }

    namespace Utils
    {
        public static class StringExtensions
        {
            public static string Indent(this string s, int indent)
            {
                return new string(' ', indent) + s;
            }
            public static bool IsStringToken(this string s) => s[0] == '"' && s.Last() == '"';
            public static bool IsVarName(this char c)
            {
                if (c >= 'A' && c <= 'Z') return true;
                if (c >= 'a' && c <= 'z') return true;
                if (c >= '0' && c <= '9') return true;
                if (c == '$' || c == '_' || c == ':' || c == '!') return true;
                return false;
            }
            public static string ContainsAny(this string s, string[] values)
            {
                int minIdx = -1;
                string matchVal = "";
                foreach (var value in values)
                    if (s.Contains(value))
                    {
                        if (minIdx == -1 || s.IndexOf(value) < minIdx)
                        {
                            minIdx = s.IndexOf(value);
                            matchVal = value;
                        }
                    }
                return minIdx == -1 ? "" : matchVal;
            }
        }
    }
}
