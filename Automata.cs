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

        public class FunctionRunner : ICallable
        {
            public class ReturnValue : Exception
            {
                public BaseValue retVal;
                public ReturnValue(BaseValue val) => retVal = val;
            }

            public List<(VarResolver, BaseValue.ValueType)> Head => head;
            List<(VarResolver, BaseValue.ValueType)> head;
            List<Instruction> body;
            public FunctionRunner(List<(VarResolver, BaseValue.ValueType)> head, List<Instruction> body)
            {
                this.head = head;
                this.body = body;
            }
            public BaseValue Call(Scope currentScope)
            {
                // use exceptions to handle function return value (it's easier)
                try
                {
                    foreach (var instr in body)
                        instr.Execute(currentScope);
                }
                catch (ReturnValue retVal)
                {
                    return retVal.retVal;
                }
                // default return value
                return NilValue.Nil;
            }
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
        public class ObjectAccessor : VarResolver
        {
            public IEvaluable baseExpr;
            public IEvaluable accessor;
            public ObjectAccessor(IEvaluable baseExpr, IEvaluable accessor)
            {
                this.baseExpr = baseExpr;
                this.accessor = accessor;
            }

            public override BaseValue Resolve(Scope currentScope)
            {
                var baseVal = baseExpr.Evaluate(currentScope);
                if (baseVal.Type != BaseValue.ValueType.Object)
                    throw new Exceptions.InvalidOperationException("Tried walking non-object value " + baseVal.Stringify().Value);
                var accessVal = accessor.Evaluate(currentScope);
                return ((ObjectValue)baseVal).GetChild((string)accessVal.Stringify().Value!);
            }
            public override void Assign(Scope currentScope, BaseValue value)
            {
                var baseVal = baseExpr.Evaluate(currentScope);
                if (baseVal.Type != BaseValue.ValueType.Object)
                    throw new Exceptions.InvalidOperationException("Tried walking non-object value " + baseVal.Stringify().Value);
                var accessVal = accessor.Evaluate(currentScope);
                ((ObjectValue)baseVal).SetChild((string)accessVal.Stringify().Value!, value);
            }
            public override string ToString() => $"ObjectAccessor(({baseExpr}) -> ({accessor}))";
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
                FunctionReturn,
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
        public class FunctionReturnInstruction : Instruction
        {
            public IEvaluable returnValue;
            public FunctionReturnInstruction(IEvaluable returnValue)
            {
                this.returnValue = returnValue;
            }
            public override void Execute(Scope currentScope) => throw new FunctionRunner.ReturnValue(returnValue.Evaluate(currentScope));
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

        public class ProgramParser
        {
            List<Token> tokens;
            int crnt = 0;
            public ProgramParser(List<Token> tokens)
            {
                this.tokens = tokens;
            }
            List<Token> nextTokens => tokens[crnt..];

            public List<Instruction> ParseProgram()
            {
                List<Instruction> ret = [];
                while(nextTokens.Count > 0)
                {
                    if (nextTokens[0].Type == Token.TokenType.Keyword)
                    {
                        if (nextTokens[0].Value == "if")
                        {
                            // find matching 'el' and 'fi' keywords
                            ++crnt;
                            var expr = ParseExpression();
                            int depth = 1;
                            int match_el = 0, match_fi = 0;
                            for (int i = 0; i < nextTokens.Count; i++)
                            {
                                if (nextTokens[i].Type != Token.TokenType.Keyword) continue;
                                if (nextTokens[i].Value == "if")
                                    ++depth;
                                else if (nextTokens[i].Value == "fi")
                                    --depth;
                                if (depth > 1) continue;
                                if (nextTokens[i].Value == "el" && depth == 1 && match_el == 0)
                                    match_el = i;
                                if (nextTokens[i].Value == "fi" && match_fi == 0)
                                {
                                    match_fi = i;
                                    break;
                                }
                            }
                            if (match_fi == 0)
                                throw new Exceptions.MissingTokenException("Couldn't find matching 'fi' keyword");
                            if (match_el == 0)
                            {
                                // only parse true block
                                var true_block = new ProgramParser(nextTokens[..(match_fi - 1)]).ParseProgram();
                                crnt = match_fi + 1;
                                ret.Add(new IfBlocksInstruction(expr, true_block, null));
                            }
                            else
                            {
                                var true_block = new ProgramParser(nextTokens[..(match_el - 1)]).ParseProgram();
                                var false_block = new ProgramParser(nextTokens[(match_el + 1)..(match_fi - 1)]).ParseProgram();
                                crnt = match_fi + 1;
                                ret.Add(new IfBlocksInstruction(expr, true_block, false_block));
                            }
                            continue;
                        }
                        if (nextTokens[0].Value == "while")
                        {
                            ++crnt;
                            var expr = ParseExpression();
                            int depth = 1;
                            int match_ewhil = 0;
                            for(int i = 0; i < nextTokens.Count; i++)
                            {
                                if (nextTokens[i].Type != Token.TokenType.Keyword)
                                    continue;
                                if (nextTokens[i].Value == "while")
                                    ++depth;
                                else if (nextTokens[i].Value == "ewhil")
                                    --depth;
                                if(depth == 0)
                                {
                                    match_ewhil = i;
                                    break;
                                }
                            }
                            if (match_ewhil == 0)
                                throw new Exceptions.MissingTokenException("Couldn't find matching 'ewhil' keyword");
                            var body = new ProgramParser(nextTokens[..(match_ewhil - 1)]).ParseProgram();
                            ret.Add(new WhileBlocksInstruction(expr, body));
                            crnt = match_ewhil + 1;
                            continue;
                        }
                        if (nextTokens[0].Value == "for")
                        {
                            ++crnt;
                            var variable = ParseVariable();
                            var expr = ParseExpression();
                            int depth = 1;
                            int match_rfo = 0;
                            for(int i = 0; i < nextTokens.Count; i++)
                            {
                                if (nextTokens[i].Type != Token.TokenType.Keyword)
                                    continue;
                                if (nextTokens[i].Value == "for")
                                    ++depth;
                                if (nextTokens[i].Value == "rfo")
                                    --depth;
                                if (depth == 0)
                                {
                                    match_rfo = i;
                                    break;
                                }
                            }
                            if (match_rfo == 0)
                                throw new Exceptions.MissingTokenException("Couldn't find matching 'rfo' keyword");
                            var body = new ProgramParser(nextTokens[..(match_rfo - 1)]).ParseProgram();
                            ret.Add(new ForBlocksInstruction(variable, expr, body));
                            crnt = match_rfo + 1;
                            continue;
                        }
                        throw new Exceptions.UnexpectedTokenException("Unexpected keyword " + nextTokens[0].Value);
                    }
                    // next instruction can be assignment, or fn_call
                    // TODO: implement
                }
                return ret;
            }

            public IEvaluable ParseExpression()
            {
                // check for prefix operators
                if (nextTokens[0].Type == Token.TokenType.Operator)
                {
                    if (nextTokens[0].Value == "+")
                    {
                        ++crnt;
                        return new Expression(ParseExpression(), Expression.ExpressionOperator.Plus, null);
                    }
                    if (nextTokens[0].Value == "-")
                    {
                        ++crnt;
                        return new Expression(ParseExpression(), Expression.ExpressionOperator.Minus, null);
                    }
                    if (nextTokens[0].Value == "!")
                    {
                        ++crnt;
                        return new Expression(ParseExpression(), Expression.ExpressionOperator.LogicalNot, null);
                    }
                    throw new Exceptions.UnexpectedTokenException($"Expected unary operator but found '{nextTokens[0].Value}'");
                }
                // parse LHS
                IEvaluable? lhs = null;
                // first check for bracketed expression
                if (nextTokens[0].Type == Token.TokenType.RoundBracket)
                {
                    if (nextTokens[0].Value == ")")
                        throw new Exceptions.UnexpectedTokenException("Expected '(' but found ')' instead");
                    ++crnt;
                    // parse inner expression
                    lhs = ParseExpression();
                    if (nextTokens[0].Type != Token.TokenType.RoundBracket)
                        throw new Exceptions.MissingTokenException("Expected round bracket but found " + nextTokens[0]);
                    if (nextTokens[0].Value != ")")
                        throw new Exceptions.UnexpectedTokenException("Expected ')' but found '(' instead");
                    ++crnt;
                }
                // check for function definition
                if (nextTokens[0].Type == Token.TokenType.Keyword && nextTokens[0].Value == "fun")
                {
                    ++crnt;
                    List<(VarResolver, BaseValue.ValueType)> arguments = ParseFunctionHead();
                    // find matching nfu
                    int depth = 1;
                    int matching_nfu = 0;
                    for (int i = 0; i < nextTokens.Count; i++)
                    {
                        if (nextTokens[i].Type != Token.TokenType.Keyword)
                            continue;
                        if (nextTokens[i].Value == "fun")
                            ++depth;
                        if (nextTokens[i].Value == "nfu")
                            --depth;
                        if (depth == 0)
                        {
                            matching_nfu = i;
                        }
                    }
                    if (matching_nfu == 0)
                        throw new Exceptions.MissingTokenException("Couldn't find matching 'nfu' keyword");
                    var body = new ProgramParser(nextTokens[..(matching_nfu - 1)]).ParseProgram();
                    crnt = matching_nfu + 1;
                    lhs = new FunctionValue()
                }
                
                if (nextTokens[0].Type == Token.TokenType.Constant)
                {
                    ++crnt;
                    if (nextTokens[0].Value[0] == '"')
                        return new StringValue(nextTokens[0].Value[1..^1]);
                    return new NumberValue(double.Parse(nextTokens[0].Value));
                }
                if (nextTokens[0].Type == Token.TokenType.EmptyObject)
                {
                    ++crnt;
                    return new ObjectValue();
                }
                if (nextTokens[0].Type == Token.TokenType.VarType && nextTokens[0].Value == "nil")
                {
                    ++crnt;
                    return NilValue.Nil;
                }
                // check for function definition

            }

            public VarResolver ParseVariable()
            {
                VarResolver current;
                if (nextTokens[0].Type == Token.TokenType.Variable)
                {
                    ++crnt;
                    current = VariableExpander.FromString(nextTokens[0].Value);
                    while (nextTokens[0].Type == Token.TokenType.SquareBracket)
                    {
                        if (nextTokens[0].Value == "]")
                            throw new Exceptions.UnexpectedTokenException("Expected '[' but found ']' instead");
                        ++crnt;
                        var expr = ParseExpression();
                        if (nextTokens[0].Type != Token.TokenType.SquareBracket)
                            throw new Exceptions.MissingTokenException("Expected square bracket but found " + nextTokens[0]);
                        if (nextTokens[0].Value != "]")
                            throw new Exceptions.UnexpectedTokenException("Expected ']' but found '[' instead");
                        ++crnt;
                        current = new VarObjectResolver(current, expr);
                    }
                    return current;
                }
                else
                {
                    var baseExpr = ParseExpression();
                    if (nextTokens[0].Type != Token.TokenType.SquareBracket)
                        throw new Exceptions.MissingTokenException("Expected square bracket but found " + nextTokens[0]);
                    // manually parse first accessor
                    if (nextTokens[0].Value == "]")
                        throw new Exceptions.UnexpectedTokenException("Expected '[' but found ']' instead");
                    ++crnt;
                    var baseAccessor = ParseExpression();
                    if (nextTokens[0].Type != Token.TokenType.SquareBracket)
                        throw new Exceptions.MissingTokenException("Expected square bracket but found " + nextTokens[0]);
                    if (nextTokens[0].Value != "]")
                        throw new Exceptions.UnexpectedTokenException("Expected ']' but found '[' instead");
                    ++crnt;
                    current = new ObjectAccessor(baseExpr, baseAccessor);
                    while (nextTokens[0].Type == Token.TokenType.SquareBracket)
                    {
                        if (nextTokens[0].Value == "]")
                            throw new Exceptions.UnexpectedTokenException("Expected '[' but found ']' instead");
                        ++crnt;
                        var accessor = ParseExpression();
                        if (nextTokens[0].Type != Token.TokenType.SquareBracket)
                            throw new Exceptions.MissingTokenException("Expected square bracket but found " + nextTokens[0]);
                        if (nextTokens[0].Value != "]")
                            throw new Exceptions.UnexpectedTokenException("Expected ']' but found '[' instead");
                        ++crnt;
                        current = new ObjectAccessor(current, accessor);
                    }
                    return current;
                }
            }
        }

        public static class VariableExpander
        {
            public static VarResolver FromString(string full_var_name)
            {
                full_var_name = full_var_name[1..]; // skip over '$'
                // check for simple name
                int obj_acc = full_var_name.LastIndexOf(':');
                if (obj_acc <= 0)
                    return new VarNameResolver(full_var_name); // not object accessor
                var split = full_var_name.Split(':');
                if (split[0].Length == 0)
                {
                    split = split[1..];
                    split[0] = ":" + split[0];
                }
                VarResolver baseVar = new VarNameResolver(split[0]);
                for (int i = 1; i < split.Length; i++)
                    baseVar = new VarObjectResolver(baseVar, new StringValue(split[i]));
                return baseVar;
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

        [Serializable]
        public class MissingTokenException : Exception
        {
            public MissingTokenException(string message) : base(message) { }
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
