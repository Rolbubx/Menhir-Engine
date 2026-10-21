namespace Menhir.Translator.Tests;

/// <summary>
/// Each test program must print exactly the same thing translated to C as it does on .NET.
/// The programs are in Translator/TestPrograms/Programs.
/// </summary>
public sealed class DifferentialTests(NativeProgramFixture fixture) : IClassFixture<NativeProgramFixture>
{
    [Theory]
    [InlineData("hello")]
    [InlineData("arithmetic")]
    [InlineData("floats")]
    [InlineData("strings")]
    [InlineData("classes")]
    [InlineData("structs")]
    [InlineData("arrays")]
    [InlineData("controlflow")]
    [InlineData("enums")]
    [InlineData("pointers")]
    [InlineData("generics")]
    public void NativeOutputMatchesDotnet(string program)
    {
        var expected = fixture.RunDotnet(program);
        var actual = fixture.RunNative(program);

        Assert.Equal(0, expected.ExitCode);
        Assert.Equal(expected.Output, actual.Output);
        Assert.Equal(expected.Errors, actual.Errors);
        Assert.Equal(expected.ExitCode, actual.ExitCode);
    }

    [Fact]
    public void UnknownProgram_ExitCodeMatches()
    {
        Assert.Equal(fixture.RunDotnet("nope"), fixture.RunNative("nope"));
    }

    /// <summary>
    /// Unhandled exceptions: same output before them, same exit code, same first error line
    /// (.NET then prints a stack trace, which native builds do not have).
    /// </summary>
    [Theory]
    [InlineData("throw")]
    [InlineData("index")]
    [InlineData("null")]
    [InlineData("divide")]
    [InlineData("cast")]
    public void UnhandledException_EndsTheProgramLikeDotnet(string program)
    {
        var expected = fixture.RunDotnet(program);
        var actual = fixture.RunNative(program);

        Assert.Equal(expected.Output, actual.Output);
        Assert.Equal(expected.ExitCode, actual.ExitCode);
        string expectedType = FirstLine(expected.Errors).Split(':')[0];
        Assert.StartsWith("Unhandled exception. System.", expectedType);
        Assert.Equal(expectedType, FirstLine(actual.Errors).Split(':')[0]);
    }

    private static string FirstLine(string text) => text.Split('\n')[0];
}
