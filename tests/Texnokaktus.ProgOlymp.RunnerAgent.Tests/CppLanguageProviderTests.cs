using Texnokaktus.ProgOlymp.RunnerAgent.Languages;

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
        Assert.That(new CppLanguageProvider().Supports(language, version), Is.EqualTo(expected));
    }

    [Test]
    public void UsesFixedCpp23CommandAndDynamicLinking()
    {
        var provider = new CppLanguageProvider();

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
}
