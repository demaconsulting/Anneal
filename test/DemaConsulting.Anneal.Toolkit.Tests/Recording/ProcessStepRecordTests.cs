using System.Text.Json;
using System.Text.Json.Serialization;
using DemaConsulting.Anneal.Toolkit.Recording;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Recording;

/// <summary>
///     Interior test proving <see cref="RecordStore" /> can append a <see cref="ProcessStepRecord" /> and read it
///     back correctly, mirroring how the existing invocation-record round trip is exercised.
/// </summary>
public class ProcessStepRecordTests
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Append_ProcessStepRecord_CanBeReadBackUnchanged()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var store = new RecordStore(root);
            var at = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var record = new ProcessStepRecord(at, "invocation-123", "Oracle", "Succeeded", 2, null);

            // Act
            store.Append(record);
            var path = RecordStore.ProcessStepsPathFor(root);
            var lines = File.ReadAllLines(path);
            var roundTripped = JsonSerializer.Deserialize<ProcessStepRecord>(lines.Single(), ReadOptions);

            // Assert: the file exists at the resolved path, holds exactly one line, and reads back unchanged
            Assert.Multiple(
                () => Assert.Single(lines),
                () => Assert.Equal(record, roundTripped));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Append_ProcessStepRecord_DoesNotTouchInvocationsFile()
    {
        // Arrange: additive means the existing stream is left alone
        var root = CreateTemporaryDirectory();
        try
        {
            var store = new RecordStore(root);
            var record = new ProcessStepRecord(DateTimeOffset.UtcNow, "invocation-123", "Research", "Refused", null, null);

            // Act
            store.Append(record);

            // Assert: the process-step file exists, the invocation file was never created
            Assert.Multiple(
                () => Assert.True(File.Exists(RecordStore.ProcessStepsPathFor(root))),
                () => Assert.False(File.Exists(RecordStore.InvocationsPathFor(root))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Append_ProcessStepRecord_NullRecord_Throws()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var store = new RecordStore(root);

            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => store.Append((ProcessStepRecord)null!));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-process-steps-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
