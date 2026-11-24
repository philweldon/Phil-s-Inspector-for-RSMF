using Rsmf.Core;
using Xunit;

namespace Rsmf.Tests;

[Collection("fixtures")]
public class SampleSetTests
{
    private readonly FixtureFolder _fx;

    public SampleSetTests(FixtureFolder fx) => _fx = fx;

    public static IEnumerable<object[]> SampleNames() => SampleSet.Build().Select(s => new object[] { s.FileName });

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void Each_sample_raises_exactly_its_rule(string fileName)
    {
        var sample = SampleSet.Build().Single(s => s.FileName == fileName);
        var path = Path.Combine(_fx.NewDir("sample"), sample.FileName);
        File.WriteAllBytes(path, sample.Content);
        var options = new InspectionOptions { TimeZoneId = SampleSet.Zone, ParticipantWarningThreshold = sample.ParticipantThreshold };

        var result = FileInspector.Inspect(path, sample.FileName, options, CancellationToken.None);
        var raised = result.Findings.Select(f => f.RuleId).Distinct().OrderBy(r => r, StringComparer.Ordinal).ToArray();

        var expected = (sample.RuleId.Length == 0 ? Array.Empty<string>() : new[] { sample.RuleId })
            .Concat(sample.AlsoExpected).OrderBy(r => r, StringComparer.Ordinal).ToArray();
        Assert.True(expected.SequenceEqual(raised),
            $"{fileName}: expected [{string.Join(", ", expected)}] but got [{string.Join(", ", raised)}]: " +
            string.Join(" | ", result.Findings.Select(f => $"{f.RuleId} {f.Location} {f.Observed}")));
        if (sample.RuleId.Length > 0)
            Assert.Single(result.Findings, f => f.RuleId == sample.RuleId);
    }

    [Fact]
    public void Scanning_the_sample_folder_with_the_sample_settings_matches_the_index()
    {
        var folder = _fx.NewDir("samples-scan");
        SampleSet.WriteAll(folder);
        var options = new InspectionOptions { TimeZoneId = SampleSet.Zone, ParticipantWarningThreshold = SampleSet.ParticipantThreshold };
        var run = ScanRunner.Run(folder, _fx.NewDir("out"), options);

        var samples = SampleSet.Build();
        Assert.Equal(samples.Count + 1, run.Files.Count); // + INDEX.md
        foreach (var sample in samples.Where(s => s.FileName.EndsWith(".rsmf")))
        {
            var file = run.Files.Single(f => f.RelativePath == sample.FileName);
            var expected = (sample.RuleId.Length == 0 ? Array.Empty<string>() : new[] { sample.RuleId })
                .Concat(sample.AlsoExpected).OrderBy(r => r, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, file.Findings.Select(f => f.RuleId).Distinct().OrderBy(r => r, StringComparer.Ordinal).ToArray());
        }
        var st = RunStatistics.From(run);
        Assert.Equal(samples.Count(s => s.FileName.EndsWith(".rsmf")), st.Checked);
        Assert.Equal(2, st.FilesClean);                                // the two good files
        Assert.Equal(2, st.OtherFiles);                                // INDEX.md and the .eml sample
        // every rule with a sample file appears in the explorer index
        var rules = FindingsIndex.Build(run).Select(g => g.Rule.Id).ToHashSet();
        Assert.All(samples.Where(s => s.RuleId.Length > 0 && s.FileName.EndsWith(".rsmf")), s => Assert.Contains(s.RuleId, rules));
    }

    [Fact]
    public void Every_rule_has_a_sample_or_a_stated_reason()
    {
        var covered = SampleSet.Build().Select(s => s.RuleId).Where(r => r.Length > 0)
            .Concat(SampleSet.NoFile.Select(n => n.RuleId)).ToHashSet();
        var missing = RuleCatalog.All.Select(r => r.Id).Where(id => !covered.Contains(id)).ToArray();
        Assert.True(missing.Length == 0, "rules without a sample: " + string.Join(", ", missing));
        Assert.Equal(SampleSet.Build().Count, SampleSet.Build().Select(s => s.FileName).Distinct().Count());
    }

    [Fact]
    public void Sample_folder_is_written_with_an_index()
    {
        var keep = Environment.GetEnvironmentVariable("RSMF_SAMPLES_OUT");
        var folder = string.IsNullOrWhiteSpace(keep) ? _fx.NewDir("samples") : keep;
        SampleSet.WriteAll(folder);
        var samples = SampleSet.Build();
        Assert.All(samples, s => Assert.True(File.Exists(Path.Combine(folder, s.FileName))));
        var index = File.ReadAllText(Path.Combine(folder, "INDEX.md"));
        Assert.All(samples, s => Assert.Contains(s.FileName, index));
        Assert.DoesNotContain(FixtureGenerator.BodyMarker, System.Text.Encoding.UTF8.GetString(samples[0].Content));
    }
}
