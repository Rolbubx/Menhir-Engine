# Translator (IL2C)

IL2C turns a compiled C# program into one C file. That file is then compiled together with the
native runtime in `Engine/Runtime` and the Boehm garbage collector (`Engine/ThirdParty/bdwgc`).

| Folder                     | What                                                              |
|----------------------------|-------------------------------------------------------------------|
| `CoreLib/`                 | `Menhir.CoreLib`, the custom base library programs compile against |
| `Menhir.Translator/`       | The `il2c` command-line translator                                |
| `Menhir.Translator.Tests/` | Unit tests and differential tests (native output vs .NET)         |
| `TestPrograms/`            | C# test programs, built both for .NET and for Menhir.CoreLib      |

## Usage

Programs do not use the .NET base library. They are compiled against `Menhir.CoreLib`:

```xml
<PropertyGroup>
  <NoStdLib>true</NoStdLib>
  <NoCompilerStandardLib>true</NoCompilerStandardLib>
  <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="path/to/Translator/CoreLib/Menhir.CoreLib.csproj" />
</ItemGroup>
```

`TestPrograms/Menhir/TestPrograms.Menhir.csproj` is a complete example. Then:

```sh
dotnet run --project Translator/Menhir.Translator -- MyGame.dll --build -o mygame
./mygame
```

| Option                  | Meaning                                                              |
|-------------------------|----------------------------------------------------------------------|
| `--corlib <dll>`        | Menhir.CoreLib.dll (default: next to the input assembly)             |
| `--entry Ns.Type.Method`| Entry point (default: the assembly's `Main`)                         |
| `-o <path>`             | Output: the `.c` file, or the executable with `--build`              |
| `--build`               | Compile and link with a C compiler (`--cc <compiler>`, default `cc`) |
| `--engine <dir>`        | The `Engine/` folder (found automatically from the repository)       |

Building needs GCC or Clang. The collector is compiled once (`extra/gc.c`) and cached.

## How it works

- The assemblies are read with `System.Reflection.Metadata` (part of .NET).
- Every method is translated to one C function. Evaluation stack slots become C locals (one per
  depth and kind), branches become `goto`.
- Objects start with an `MObject` header that points at their `MType` (name, base type, flags,
  size, vtable). Class fields follow the header, base class fields first. Value types are plain
  C structs.
- Virtual calls go through the vtable. Static constructors run on first use, following the
  `beforefieldinit` rules.
- Integer division, shifts, overflow checks and float→int conversions follow .NET semantics.
  Floats print the same as .NET (shortest round-trip form).
- Internal calls (`[MethodImpl(MethodImplOptions.InternalCall)]` in the corlib) are C functions
  in `Engine/Runtime/src/runtime.c`, named `Namespace_Type__Method`.
- `try`/`finally` works. An exception cannot be caught yet: it ends the program the same way an
  unhandled .NET exception does (message on stderr, exit code 134).

## Tests

`dotnet test Translator/Menhir.Translator.Tests` translates and builds `TestPrograms` once, then
runs every program both natively and on .NET. Their output and exit code must match exactly.

## Limitations and roadmap

Supported today: classes, inheritance, virtual methods, structs, enums, arrays, strings,
static constructors, pointers and `stackalloc`, checked arithmetic, `try`/`finally`.

- **IL2C-2**: catching exceptions, interfaces, generics, delegates and lambdas.
- **IL2C-3**: collections in the corlib (`List<T>`, `Dictionary<K,V>`...), string formatting,
  then the engine's scripting API.

Not supported yet: `Enum.ToString` names, `ValueType.Equals`, string interpolation, reflection,
threads. The generated C uses GCC/Clang built-ins, so MSVC does not work yet.
