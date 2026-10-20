using Menhir.Translator.CodeGen;
using Menhir.Translator.TypeSystem;

namespace Menhir.Translator.Tests;

public sealed class TranslatorTests(NativeProgramFixture fixture) : IClassFixture<NativeProgramFixture>
{
    [Theory]
    [InlineData(1.5, false, "1.5")]
    [InlineData(2.0, false, "2.0")]
    [InlineData(0.1, false, "0.1")]
    [InlineData(1e300, false, "1E+300")]
    [InlineData(0.25, true, "0.25f")]
    [InlineData(double.NaN, false, "m_r8_bits(0xfff8000000000000ull)")]
    [InlineData(double.PositiveInfinity, true, "m_r4_bits(0x7f800000u)")]
    public void FloatLiteral_IsExact(double value, bool single, string expected) =>
        Assert.Equal(expected, Il2cTranslator.FloatLiteral(value, single));

    [Fact]
    public void CString_EscapesAndEncodesUtf8()
    {
        Assert.Equal("\"a\\\"b\\\\c\\303\\251\"", Il2cTranslator.CString("a\"b\\cé"));
    }

    [Fact]
    public void GeneratedCode_NamesRuntimeSymbolsPredictably()
    {
        string code = File.ReadAllText(fixture.CFile);
        Assert.Contains("const MType System_String__type = {", code);
        Assert.Contains("const MType System_Object__type = {", code);
        Assert.Contains("void System_Console__WriteString(System_String* a0);", code);
        Assert.Contains("int main(int argc, char** argv)", code);
    }

    [Fact]
    public void ReferencesToTheDotnetLibraries_AreRefusedWithAnExplanation()
    {
        string dotnetPrograms = Path.GetDirectoryName(fixture.DotnetProgram)!;
        string coreLib = Path.Combine(fixture.TranslatorDirectory, "TestPrograms", "Menhir", "bin",
            new DirectoryInfo(dotnetPrograms).Parent!.Name, new DirectoryInfo(dotnetPrograms).Name, "Menhir.CoreLib.dll");
        using var context = new TypeSystemContext();
        context.Load(coreLib, [fixture.DotnetProgram]);
        var error = Assert.Throws<TranslationException>(() => new Il2cTranslator(context).Translate(null));
        Assert.Contains("Menhir.CoreLib", error.Message);
    }
}
