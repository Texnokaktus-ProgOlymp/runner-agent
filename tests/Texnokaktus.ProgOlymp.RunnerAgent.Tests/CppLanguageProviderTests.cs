using Texnokaktus.ProgOlymp.RunnerAgent.Languages;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Tests;

public sealed class CppLanguageProviderTests
{
    private static readonly string[] ExpectedArguments =
        ["-std=c++23", "-O2", "-pipe", "-DONLINE_JUDGE", "/box/source.cpp", "-o", "/box/solution"];

    [TestCase("cpp", "23", true)]
    [TestCase("CPP", "23", false)]
    [TestCase("c++", "23", false)]
    [TestCase("cpp", "c++23", false)]
    [TestCase("cpp", "20", false)]
    [TestCase("", "23", false)]
    [TestCase("cpp", "", false)]
    public void MatchesOnlyAgreedProfile(string language, string version, bool expected)
    {
        Assert.That(new ConfiguredLanguageProvider(ReadProfile()).Supports(language, version), Is.EqualTo(expected));
    }

    [Test]
    public void UsesFixedCpp23CommandAndDynamicLinking()
    {
        var provider = new ConfiguredLanguageProvider(ReadProfile());

        Assert.Multiple(() =>
            {
                Assert.That(provider.CompilationCommand.Executable, Is.EqualTo("/usr/bin/g++"));

                Assert.That(
                    provider.CompilationCommand.Arguments,
                    Is.EqualTo(ExpectedArguments)
                );

                Assert.That(provider.CompilationCommand.Arguments, Does.Not.Contain("-static"));
                Assert.That(provider.ExecutionCommand.Executable, Is.EqualTo("/box/solution"));
                Assert.That(provider.ExecutionCommand.Arguments, Is.Empty);
            }
        );
    }

    private static LanguageProfile ReadProfile() => JsonNode.Parse(
                                                                 File.ReadAllText(
                                                                     Path.Combine(
                                                                         AppContext.BaseDirectory,
                                                                         "appsettings.Testing.json"
                                                                     )
                                                                 )
                                                             )!["LanguageProfile"]!
                                                            .Deserialize<LanguageProfile>()!;

    [Test]
    public void RejectsMissingProfile()
    {
        Assert.That(() => new ConfiguredLanguageProvider(new LanguageProfile()), Throws.InvalidOperationException);
    }

    [Test]
    public void FreezesProfileAtStartup()
    {
        var profile = ReadProfile();
        var provider = new ConfiguredLanguageProvider(profile);
        profile.Language = "changed";
        profile.Compilation.Arguments[0] = "changed";
        Assert.That(provider.Supports("cpp", "23"), Is.True);
        Assert.That(provider.CompilationCommand.Arguments, Is.EqualTo(ExpectedArguments));
    }

    [TestCase("../source.cpp")]
    [TestCase("input.txt")]
    [TestCase("..")]
    [TestCase("a\\b")]
    [TestCase("")]
    public void RejectsInvalidLocalFileNames(string name)
    {
        var profile = ReadProfile();
        profile.SourceFileName = name;
        Assert.That(() => new ConfiguredLanguageProvider(profile), Throws.InvalidOperationException);
    }

    [TestCase("g++")]
    [TestCase("")]
    public void RequiresAbsoluteCompilerPath(string executable)
    {
        var profile = ReadProfile();
        profile.Compilation.Executable = executable;
        Assert.That(() => new ConfiguredLanguageProvider(profile), Throws.InvalidOperationException);
    }
}
