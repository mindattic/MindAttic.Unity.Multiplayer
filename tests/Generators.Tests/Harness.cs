using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;

namespace MindAttic.Unity.Multiplayer.Generators.Tests
{
    /// <summary>What one generator run produced.</summary>
    internal sealed class RunResult
    {
        public Compilation Output;
        public ImmutableArray<Diagnostic> GeneratorDiagnostics;
        public IReadOnlyDictionary<string, string> Sources;

        /// <summary>Compiler errors and warnings in the compilation with the generated code added (suppressions applied).</summary>
        public ImmutableArray<Diagnostic> CompileDiagnostics;

        public IEnumerable<Diagnostic> Errors
        {
            get { return GeneratorDiagnostics.Concat(CompileDiagnostics).Where(d => d.Severity == DiagnosticSeverity.Error); }
        }

        public string Source(string hintName)
        {
            string text;
            Assert.That(Sources.TryGetValue(hintName, out text), Is.True, "no generated file " + hintName + "; got: " + string.Join(", ", Sources.Keys));
            return text;
        }
    }

    /// <summary>Runs the generator, analyzer and suppressor over test sources the way Unity's compiler does (C# 9).</summary>
    internal static class Harness
    {
        public static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);

        private static readonly Lazy<string> s_RepoRoot = new Lazy<string>(FindRepoRoot);
        private static readonly Lazy<ImmutableArray<MetadataReference>> s_References = new Lazy<ImmutableArray<MetadataReference>>(LoadReferences);

        public static string RepoRoot
        {
            get { return s_RepoRoot.Value; }
        }

        public static string PackagePath(string relative)
        {
            return Path.Combine(RepoRoot, "unity", "com.mindattic.unity.multiplayer", relative.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>The stand-ins plus the package's real Core sources the generated code and game code use.</summary>
        public static IEnumerable<SyntaxTree> SupportTrees()
        {
            yield return Parse(ReadStub("EngineStubs.cs"), "EngineStubs.cs");
            foreach (string core in new[] { "Runtime/Core/Rpc/RpcAttributes.cs", "Runtime/Core/Rpc/NetSender.cs", "Runtime/Core/Approval/NetProtocolAttribute.cs" })
            {
                yield return Parse(File.ReadAllText(PackagePath(core)), Path.GetFileName(core));
            }
        }

        public static SyntaxTree Parse(string source, string path)
        {
            return CSharpSyntaxTree.ParseText(source.Replace("\r\n", "\n"), ParseOptions, path);
        }

        public static CSharpCompilation Compile(params string[] sources)
        {
            IEnumerable<SyntaxTree> trees = SupportTrees().Concat(sources.Select((s, i) => Parse(s, "Game" + i + ".cs")));
            return CSharpCompilation.Create(
                "Game",
                trees,
                s_References.Value,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Disable));
        }

        public static RunResult Run(params string[] sources)
        {
            return Run(Compile(sources));
        }

        public static RunResult Run(CSharpCompilation compilation)
        {
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new NetEntityGenerator().AsSourceGenerator() },
                parseOptions: ParseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);

            GeneratorDriverRunResult run = driver.GetRunResult();
            var sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (GeneratedSourceResult generated in run.Results.SelectMany(r => r.GeneratedSources))
            {
                sources[generated.HintName] = generated.SourceText.ToString();
            }

            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new SyncedFieldAnalyzer(), new SyncedFieldSuppressor());
            ImmutableArray<Diagnostic> compile = output.WithAnalyzers(analyzers).GetAllDiagnosticsAsync().GetAwaiter().GetResult()
                .Where(d => !d.IsSuppressed && d.Severity >= DiagnosticSeverity.Warning)
                .ToImmutableArray();

            return new RunResult { Output = output, GeneratorDiagnostics = diagnostics, Sources = sources, CompileDiagnostics = compile };
        }

        /// <summary>Every diagnostic from the generator plus the analyzer, ids only.</summary>
        public static IEnumerable<string> Ids(RunResult result)
        {
            return result.GeneratorDiagnostics.Concat(result.CompileDiagnostics).Select(d => d.Id);
        }

        public static string Format(IEnumerable<Diagnostic> diagnostics)
        {
            return string.Join("\n", diagnostics.Select(d => d.ToString()));
        }

        /// <summary>
        /// Compares <paramref name="actual"/> with Snapshots/<paramref name="name"/>. UPDATE_SNAPSHOTS=1 rewrites the file;
        /// a missing file is written and the test fails so the new snapshot gets reviewed.
        /// </summary>
        public static void MatchesSnapshot(string actual, string name, [CallerFilePath] string caller = "")
        {
            string path = Path.Combine(Path.GetDirectoryName(caller), "Snapshots", name);
            actual = actual.Replace("\r\n", "\n");
            bool update = Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1";
            if (update || !File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, actual, new UTF8Encoding(false));
                if (!update)
                {
                    Assert.Fail("New snapshot written to " + path + "; review it and run again.");
                }

                return;
            }

            string expected = File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.That(actual, Is.EqualTo(expected), "generated code differs from " + path + " (UPDATE_SNAPSHOTS=1 rewrites it)");
        }

        private static string ReadStub(string name)
        {
            using (Stream stream = typeof(Harness).Assembly.GetManifestResourceStream("Stubs." + name))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private static string FindRepoRoot()
        {
            for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "unity", "com.mindattic.unity.multiplayer")))
                {
                    return dir.FullName;
                }
            }

            throw new InvalidOperationException("Could not find the repository root above " + AppContext.BaseDirectory);
        }

        private static ImmutableArray<MetadataReference> LoadReferences()
        {
            var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator);
            return paths
                .Where(p =>
                {
                    string name = Path.GetFileNameWithoutExtension(p);
                    return name == "netstandard" || name == "mscorlib" || name == "System" || name.StartsWith("System.", StringComparison.Ordinal);
                })
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .ToImmutableArray();
        }
    }
}
