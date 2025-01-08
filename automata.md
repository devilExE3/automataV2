# Variables
Variables are loosly typed, but strongly enforced. They can be of three types: string, number, function and object.  
A variable can also be `nil`, meaning it exists but it has no value.
## Variable scoping
By default, variables are scoped in the current block and shadow any variables with the same name outside of the current block.  
In order to shadow a variable in an outer scope, prefix it's name with `!`. Function parameter variable names are automatically placed on its scope, shadowing any outer variables.
Variables can also be global, if their name starts with `:`  
Another thing to keep in mind, the global context may be shared by the backend with multiple scripts. automata will automatically scope the variables declared in the root block to the program.
## Examples
Basic variable types
```
$a = 1
$b = "Hello, world!"
```
Object variable
```
$obj = {}
$obj:a = 123
$obj:b = "str"
$obj:c = {}
$obj:c["other_name"] = "hello, nested"
```
Note that you cannot initialize an object at declaration, you have to declare every child by itself.
Global variables  
Also note that you can access a nested variable by it's name provided you use `[]`
```
$:global_string = "Hello, global string!"
$:bar = {}
$:bar:foo = "nested string"
```
Shadowing outer scoped variables
```
$my_var = "a"
$fun = fun()
    $!my_var = "b" # '!' indicates to define the variable in the current scope
    $:print($my_var)
nfu
$fun()
$:print($my_var)

# output: ba
```
```
$a = "Hello"
$fun = fun($a string)
    $:print($a)
nfu
$fun("World!")
$:print($a)

# output: World!Hello
```
Variables can be deleted by assigning `nil` to them
```
$:my_var = "a"
$:my_var = nil # deletes the variable and frees up space on the scope
```
Function variables are detaild in the **Functions** paragraph below.
## Array convention
Using the object variable type, you can create an array.  
The convention for such object is that values are stored on nested variables numerically numbered starting from 0.  
You also need to have a nested variable called `length` which indicates the amount of variables in the array.  
### Example
```
$my_array = {}
$my_array[0] = "hello, "
$my_array[1] = "world!"
$my_array:length = 2
```
# Functions
Functions are blocks of code that can be executed arbitrarly. A function contains a head and a body.  
The function's body denotes the paramaters. The function's body denotes the code to be run when called.  
The function can also return a value by `return`-ing it. By default the function returns the `nil` variable.  
The function's parameters can also be type-enforced, meaning automata will error out when calling with invalid parameter types.  
Another thing to keep in mind, your program will be ran as it were a function block.
## Examples
Loosly enforced types. May error if called with function, object or `nil` type variable
```
fun($a, $b)
    return $a + $b
nfu
```
Strongly enforced types. Automata makes sure that the function is only called with numbers.
```
fun($a number, $b number)
    return $a + $b
nfu
```
## Defining and calling a function
Functions can be called as-is, without registering them anywhere, but most use cases need the function to be called multiple times.
```
$:add = fun($a number, $b number)
    return $a + $b
nfu

# calling the function
$:add(2, 2)
```
Note that in this case the function doesn't neceseraly need to be explicitly registered on the global context.
# Alternative structure (if statement)
The `if` statement can be used to run certain blocks only if a certain condition is met.  
The `if` statement will only run if the expression provided is not `nil` and not 0. (basically any non-zero variable will be considered 'true')
## Example
```
# if structure
if $a == $b
    $:print("a = b")
fi

# if-else structure
if $a == $b
    $:print("a = b")
el
    $:print("a != b")
fi
```
Note that there is no else-if structure. You will need to nest the `if` in the `el`se block.
# Repetitive structure (while statement)
The `while` statement can be used to run a certain block while a certain condition is met.  
## Example
```
$i = 0
while $i < 10
    $:print($i)
    $i = $i + 1
ewhil
```
# Iterative structure (for / foreach statement)
The `for` statement can be used to iterate through an array-convention object like this.
## Example
```
# creating a basic array programatically
$my_array = {}
$i = 0
while $i < 10
    $my_array[$i] = $i
    $i = $i + 1
ewhil
$my_array:length = $i

for $x $my_array # foreach $x in $my_array
    $:print($x)
rfo

# iterating with standard functions
for $x $:range(10) # same effect as the code above
    $:print($x)
rfo
```
# Expressions and operators
Note that you may use parantheses in your expressions.
## Binary operators
Between numbers:  
`+`, `-`, `*`, `/` (standard math operators)  
`%` - `a % b` will yield a number in the range `[0,b)` by repetively adding b to a, or subtracting b from a.  
`<`, `<=`, `>`, `>=`, `==`, `!=` (standard comparations) - will return 1 if the condition is met, 0 otherwise.  
There are no base2 operators or power operators because the numbers are real. You may find a power function in the **Standard functions**.  
Between strings:  
`+` - concatenation. Will automatically convert to string if either rhs or lhs is a number.  
`==`, `!=` (standard comparations)  
`<`, `<=`, `>`, `>=` (C#'s string.CompareTo function vs 0 aka lexicographic comparation)
## Unary prefix operators
To numbers:  
`-` - negative value.  
`!` (logical not) - will return 1 if the expression is 0 or `nil`, 0 otherwise  
To strings:
`+` - attempt to parse the string as a number. if the string is not a number, will give `nil`
## Operator order
Expression result is calculated from left to right, by applying operators in the following order:
- unary operators
- comparations
- `+`, `-`
- `*`, `/`, `%`
# Program parsing
## Instruction parsing
Separate instructions should be separated by the `\n` (new line) character. This kind of replaces the `;` character from C-like languages.  
You may split an instruction into multiple lines by adding a `\` character at the end of the line.  
Example:
```
$my_string = "\
Hello, \
from multiple\
lines\
"

# this is the same as
$my_string = "Hello, from multiplelines"
```
Note that you may also leaev comments in your code by prefixing a line with `#`
## String parsing
Escape character in strings is `\`. The following are escape codes:
- `\\` - literal `\`
- `\"` - literal `"`
- `\n` - line break
- `\xAA` - ascii character of hex code 'AA'
# Standard functions
Note that depending on the implementation, some functions may be removed or added. Consult the documentation of whatever implementation you are using.
- `$:print($value)` - print an expression / variable.
- `$:pow($a number, $b number)` - returns $a to the power of $b.
- `$:range($a number)` - will return array-convention with all whole numbers from 0 to but not including `$a`
- `$:range($a number, $b number)` - will return array-convention with all whole numbers from `$a` to but not including `$b`
- `$:range($a number, $b number, $step number)` - will return array-convention with whole numbers starting from `$a` to but not including `$b` with increments of `$step`
- `$:typeof($a)` - will return the type of `$a` as a string: "number", "string", "object", "function" or "nil"
- `$:ascii($a number)` - will return ascii code `$a` character as a string if $a is whole from 0 to 255, otherwise it will return `nil`
- `$:ascii($a string)` - will return ascii code of `$a` if $a is a single character and is ascii, otherwise it will return `nil`
- `$:isarray($a object)` - returns 1 if `$a` is array-convention, 0 otherwise
# Default implementation
## Parsing
### Cleaning the program
- Multi-line instructions are collapsed (`\\\n` -> ` `)
- Comments are removed
- Empty lines are removed

### Tokenization
- Instructions are split by `\n`
- Strings are extracted
- Variables are extracted
- Keywords are extracted
- Operators are extracted
- Numbers are extracted

Should there be any unknown tokens, the tokenizer will throw an exception.

### Converting to BlockedTokens
- Tokens are grouped by blocks (ie `fun`'s, `if`'s, etc..)