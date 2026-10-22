using Menhir.Editor.Build;

namespace Menhir.Editor.Tests.Build;

public sealed class ToolchainTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "menhir-tests", Guid.NewGuid().ToString("N"));

    public ToolchainTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void TheInstallersFile_IsFoundAboveTheEditor_AndReadBack()
    {
        string editor = Path.Combine(_root, "Editor");
        Directory.CreateDirectory(editor);
        Assert.Null(Toolchain.Find(editor));

        new Toolchain { Compiler = @"C:\msys64\ucrt64\bin\gcc.exe", DotnetRoot = @"D:\dotnet" }.Save(Path.Combine(_root, Toolchain.FileName));
        string? found = Toolchain.Find(editor);
        Assert.Equal(Path.Combine(_root, Toolchain.FileName), found);
        Toolchain loaded = Toolchain.Load(found);
        Assert.Equal((@"C:\msys64\ucrt64\bin\gcc.exe", @"D:\dotnet"), (loaded.Compiler, loaded.DotnetRoot));
    }

    [Fact]
    public void ABrokenFile_FallsBackToTheDefaultCompiler()
    {
        string path = Path.Combine(_root, Toolchain.FileName);
        File.WriteAllText(path, "{ not json");
        Assert.Equal(Toolchain.DefaultCompiler, Toolchain.Load(path).Compiler);
        File.WriteAllText(path, "{ \"compiler\": \"  \" }");
        Assert.Equal(Toolchain.DefaultCompiler, Toolchain.Load(path).Compiler);
    }

    [Fact]
    public void AProjectsCompiler_WinsOverTheEditors()
    {
        Assert.Equal("clang", Toolchain.ResolveCompiler(" clang "));
        Assert.Equal(Toolchain.Current.Compiler, Toolchain.ResolveCompiler(""));
        Assert.Equal(Toolchain.Current.Compiler, Toolchain.ResolveCompiler(null));
    }

    [Fact]
    public void TheArchiver_IsTheOneNextToTheCompiler()
    {
        string bin = Path.Combine(_root, "bin");
        Directory.CreateDirectory(bin);
        string exe = OperatingSystem.IsWindows() ? ".exe" : "";
        File.WriteAllText(Path.Combine(bin, "x86_64-w64-mingw32-ar" + exe), "");
        File.WriteAllText(Path.Combine(bin, "llvm-ar" + exe), "");
        Assert.Equal(Path.Combine(bin, "x86_64-w64-mingw32-ar" + exe),
            Toolchain.FindArchiver(Path.Combine(bin, "x86_64-w64-mingw32-gcc" + exe)));
        Assert.Equal(Path.Combine(bin, "llvm-ar" + exe), Toolchain.FindArchiver(Path.Combine(bin, "clang" + exe)));
    }
}
