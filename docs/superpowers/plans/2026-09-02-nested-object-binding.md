# Nested Object Binding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the generator bind nested object schemas to named models from the map, refuse any object it cannot bind, verify every reuse site structurally, bind `format: date-time` to `Instant`, and prove all of it on `/v2/reference/news`.

**Architecture:** A property row in `specs/endpoints.map.json` gains one key, `model`, naming another `models` row whose pointer runs through the parent (`results/items/publisher`). `Emitter` composes `T`, `T?`, `T[]`, or `T[]?` from the spec at that site, and `Spec.StructuralDifferences` compares the site against the schema the model was generated from. `TypeBinding.FromSchema` loses its default for objects, recurses into arrays, and binds `date-time` strings to `Instant`, read by a new byte-level `InstantJsonConverter`. A new `tests/MassiveDotNet.CodeGen.Tests` project feeds the generator inline fragments and asserts each refusal's message. One endpoint, `ListNews`, maps into a new `Reference` group with three models.

**Tech Stack:** .NET 10, C# latest, xUnit v3, System.Text.Json source generation, NodaTime 3.3.3. No new package dependencies.

**Spec:** `docs/superpowers/specs/2026-09-02-nested-object-binding-design.md`

## Global Constraints

Copied from `CLAUDE.md` and the spec. Every task inherits these.

- **Rule 3** — No reflection-based serialization in shipped code. `System.Text.Json` source generation only. The converter in Task 1 is a plain `JsonConverter<Instant>` registered on the generated context; nothing reflects.
- **Rule 5** — `*.g.cs` files are never hand-edited. Change `tools/MassiveDotNet.CodeGen` and regenerate with `dotnet run --project tools/MassiveDotNet.CodeGen`.
- **Rule 6** — The generator is deterministic. Emission decisions use order-independent tests (`Exists`, `Any`); diagnostics list differences in the description's declaration order.
- **Rule 7** — `MassiveDotNet` (core) references no external package other than NodaTime.
- **Rule 9** — `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on. An **unused `using` fails the build** (IDE0005). `AnalysisLevel` is `latest-recommended`: **CA1305** (pass a format provider), **CA1307/CA1310** (pass a `StringComparison` to `Contains`, `StartsWith`, `IndexOf`), **CA1861** (hoist constant arrays to `static readonly`), and the naming rules apply to test code too.
- **Rule 10** — Every public member carries XML documentation, or CS1591 fails the build. Internal members are documented too, by convention.
- **Rule 12** — NodaTime only. No BCL `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, or `TimeSpan` may be *named* anywhere in `src`, `tests`, `samples`, or `tools`. `TemporalTypeTests` scans every one of those directories, including the new test project. The temporal types this plan touches are `Instant`, `LocalDate`, `LocalDateTime`, `Offset`, and `Duration`.
- **Rule 13** — CI runs offline only. The live test in Task 7 derives from `LiveApiTest`, which carries `[Trait("Category", "Integration")]`, and lives in `tests/MassiveDotNet.IntegrationTests`. CI asserts that `--list-tests` under the exclusion filter prints nothing containing `LiveTests`, so no offline test class may carry that substring in its name.
- **Spec D-N2** — A property row carries `model` or `type`, never both. The generator composes nullability and `[]` from the spec at the site.
- **Spec D-N3** — An object, an array of objects, or an array of arrays with no `model` and no `type` fails generation: on a model property, an envelope property, or a parameter. Messages name the operation, the model, the property, and the row to add.
- **Spec D-N4** — Reuse sites are compared on property names (exact), model-required properties (must be required at the site), recursively; never on scalar types.
- **Spec D-N6** — The `required` modifier is decided by a closed value-type set: `int`, `long`, `double`, `bool`, `LocalDate`, `Instant`, any `struct` model, and their nullable forms. Everything else is a reference type.
- **Spec D-N7** — On models, `format: date-time` is `Instant`. On parameters it has no default and the map must choose.
- **Style** — Explicit types, never `var`; collection expressions (`[]`, `[.. x]`); `is not { } x` null patterns; file-scoped namespaces; raw string literals for JSON. Match the surrounding code.
- **Convention** — Do not commit or push unless asked. Steps below include commits; confirm with the user before the first one. Commit messages end with the trailer `Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB`.

**Verification commands** (from CLAUDE.md, "Before opening a PR"):

```bash
dotnet build MassiveDotNet.slnx                                              # must be warning-free
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/
dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release   # zero IL warnings
```

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `src/MassiveDotNet/Serialization/InstantJsonConverter.cs` | **Create.** Byte-level RFC 3339 parser to `Instant`; writes `InstantPattern.ExtendedIso`. | 1 |
| `tests/MassiveDotNet.Rest.Tests/InstantJsonConverterTests.cs` | **Create.** Converter read and write, driven through `Utf8JsonReader`. | 1 |
| `tests/MassiveDotNet.CodeGen.Tests/MassiveDotNet.CodeGen.Tests.csproj` | **Create.** xUnit v3 project referencing the generator. | 2 |
| `tests/MassiveDotNet.CodeGen.Tests/Harness.cs` | **Create.** Builds minimal spec and map documents; runs the emitter; captures refusals. | 2 |
| `tests/MassiveDotNet.CodeGen.Tests/HarnessTests.cs` | **Create.** Proves the harness generates a model from a fragment. | 2 |
| `tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs` | **Create.** D-N3 and D-N7 on parameters; array elements on parameters. | 3 |
| `tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs` | **Create.** D-N2, D-N3, D-N5, D-N6, D-N7 on models and envelopes. | 3, 4 |
| `tests/MassiveDotNet.CodeGen.Tests/ReuseVerificationTests.cs` | **Create.** D-N4. | 5 |
| `MassiveDotNet.slnx` | **Modify.** Add the test project. | 2 |
| `tools/MassiveDotNet.CodeGen/MassiveDotNet.CodeGen.csproj` | **Modify.** `InternalsVisibleTo` the test project. | 2 |
| `tools/MassiveDotNet.CodeGen/Map.cs` | **Modify.** `MapProperty.Model`; `Map.Parse(string)`. | 2 |
| `tools/MassiveDotNet.CodeGen/Spec.cs` | **Modify.** `Spec.Parse(string)`; `SchemaShape` and `Spec.Shape`; `Spec.Describe`; `Spec.StructuralDifferences`. | 2, 3, 5 |
| `tools/MassiveDotNet.CodeGen/TypeBinding.cs` | **Modify.** Objects have no default; arrays by element; `date-time` → `Instant`; `NeedsModel`; parameter refusals. | 3 |
| `tools/MassiveDotNet.CodeGen/Argument.cs` | **Modify.** Pass the operation id to `TypeBinding.Resolve`. | 3 |
| `tools/MassiveDotNet.CodeGen/Emitter.cs` | **Modify.** Register `InstantJsonConverter`; `PropertyType`, `ModelReferenceType`, `EnvelopeType`; value-type set; reuse check. | 3, 4, 5 |
| `src/MassiveDotNet.Rest/Generated/` | **Regenerate.** Never hand-edit. | 3, 6 |
| `specs/endpoints.map.json` | **Modify.** `Reference` group; `NewsArticle`, `NewsPublisher`, `NewsInsight`; `ListNews`. | 6 |
| `src/MassiveDotNet.Rest/ReferenceGroup.cs` | **Create.** Hand-written half of the group struct. | 6 |
| `src/MassiveDotNet.Rest/MassiveRestClient.cs` | **Modify.** `Reference` property. | 6 |
| `tests/MassiveDotNet.Rest.Tests/Fixtures.cs` | **Modify.** Published news sample; a scripted last page. | 6 |
| `tests/MassiveDotNet.Rest.Tests/ReferenceNewsTests.cs` | **Create.** The generated surface, end to end through the stub. | 6 |
| `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` | **Modify.** `CoverageBaseline` 2 → 3. | 6 |
| `samples/MassiveDotNet.AotSmokeTest/Program.cs` | **Modify.** Root the nested graph and the converter with a news call. | 7 |
| `tests/MassiveDotNet.IntegrationTests/ReferenceNewsLiveTests.cs` | **Create.** One live test: ticker plus date window. | 7 |
| `CLAUDE.md` | **Modify.** D16, the Models convention, the `Instant` vocabulary row, the test tier table. | 7 |

---

### Task 1: `InstantJsonConverter`

**Files:**
- Create: `src/MassiveDotNet/Serialization/InstantJsonConverter.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/InstantJsonConverterTests.cs`

**Interfaces:**
- Consumes: `NodaTime.Instant`, `LocalDateTime`, `Offset`, `InstantPattern`; the pattern of `LocalDateJsonConverter` in the same directory.
- Produces: `public sealed class InstantJsonConverter : JsonConverter<Instant>` in namespace `MassiveDotNet.Serialization`, with a parameterless constructor. Task 3 registers it on the generated context.

- [ ] **Step 1: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/InstantJsonConverterTests.cs`:

```csharp
using System.Buffers;
using System.Text;
using System.Text.Json;
using MassiveDotNet.Serialization;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Driven directly through <see cref="Utf8JsonReader"/> for the same reason as
/// <see cref="LocalDateJsonConverterTests"/>: reflection-based serialization is disabled here, so
/// there is no <c>JsonSerializer.Deserialize&lt;Instant&gt;</c> to call without a context. The
/// end-to-end path through a generated model is covered in <c>ReferenceNewsTests</c>.
/// </summary>
public sealed class InstantJsonConverterTests
{
    private static readonly InstantJsonConverter Converter = new();
    private static readonly JsonSerializerOptions Options = new();
    private static readonly Instant Published = Instant.FromUtc(2024, 6, 24, 18, 33, 53);

    private static Instant Read(string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        Assert.True(reader.Read(), "The test JSON should contain one token.");
        return Converter.Read(ref reader, typeof(Instant), Options);
    }

    private static string Write(Instant value)
    {
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            Converter.Write(writer, value, Options);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    [Fact]
    public void ReadsAZuluTimestamp()
    {
        Assert.Equal(Published, Read("\"2024-06-24T18:33:53Z\""));
    }

    [Fact]
    public void ReadsAZeroOffsetAsZulu()
    {
        Assert.Equal(Published, Read("\"2024-06-24T18:33:53+00:00\""));
    }

    [Fact]
    public void AppliesANegativeOffset()
    {
        Assert.Equal(Instant.FromUtc(2024, 6, 24, 22, 33, 53), Read("\"2024-06-24T18:33:53-04:00\""));
    }

    [Fact]
    public void AppliesAPositiveOffsetWithMinutes()
    {
        Assert.Equal(Instant.FromUtc(2024, 6, 24, 13, 3, 53), Read("\"2024-06-24T18:33:53+05:30\""));
    }

    [Fact]
    public void ReadsOneFractionDigit()
    {
        Assert.Equal(Published + Duration.FromMilliseconds(500), Read("\"2024-06-24T18:33:53.5Z\""));
    }

    [Fact]
    public void ReadsNineFractionDigits()
    {
        Assert.Equal(Published + Duration.FromNanoseconds(123456789), Read("\"2024-06-24T18:33:53.123456789Z\""));
    }

    [Fact]
    public void AcceptsLowercaseDesignators()
    {
        // RFC 3339 permits lowercase t and z; the service emits uppercase.
        Assert.Equal(Published, Read("\"2024-06-24t18:33:53z\""));
    }

    [Fact]
    public void ReadsAnEscapedTimestampThroughTheSlowPath()
    {
        // - is a hyphen. An escaped value forces the copy-and-unescape path, which the
        // unescaped fast path never exercises.
        Assert.Equal(Published, Read("\"2024\\u002D06-24T18:33:53Z\""));
    }

    [Theory]
    [InlineData("\"2024-06-24\"")]
    [InlineData("\"2024-06-24T18:33:53\"")]
    [InlineData("\"2024-06-24 18:33:53Z\"")]
    [InlineData("\"2024-06-24T18:33:53.Z\"")]
    [InlineData("\"2024-06-24T18:33:53.1234567890Z\"")]
    [InlineData("\"2024-06-24T18:33:53+0400\"")]
    [InlineData("\"2024-06-24T18:33:53+19:00\"")]
    [InlineData("\"2024-13-24T18:33:53Z\"")]
    [InlineData("\"2024-06-24T24:00:00Z\"")]
    [InlineData("\"2024-06-24T18:33:53ZZ\"")]
    [InlineData("1719253200000")]
    [InlineData("null")]
    public void RejectsAnythingThatIsNotAnRfc3339Timestamp(string json)
    {
        Assert.Throws<JsonException>(() => Read(json));
    }

    [Fact]
    public void WritesTheExtendedIsoForm()
    {
        Assert.Equal("\"2024-06-24T18:33:53Z\"", Write(Published));
    }

    [Fact]
    public void WritesTheFractionOnlyWhenPresent()
    {
        Assert.Equal("\"2024-06-24T18:33:53.5Z\"", Write(Published + Duration.FromMilliseconds(500)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~InstantJsonConverterTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'InstantJsonConverter' could not be found`.

- [ ] **Step 3: Write the converter**

Create `src/MassiveDotNet/Serialization/InstantJsonConverter.cs`:

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Text;

namespace MassiveDotNet.Serialization;

/// <summary>
/// Reads and writes a moment on the global timeline in the RFC 3339 form the Massive API uses for
/// <c>format: date-time</c> fields, such as <c>2024-06-24T18:33:53Z</c>.
/// </summary>
/// <remarks>
/// <para>
/// Parses straight from the reader's UTF-8 bytes: no intermediate string, and none of the
/// <c>ParseResult</c> allocation NodaTime's pattern API incurs per value. A thousand-article news
/// page would otherwise allocate a thousand objects that are discarded immediately.
/// </para>
/// <para>
/// Accepts <c>YYYY-MM-DDTHH:MM:SS</c>, an optional fraction of one to nine digits, then <c>Z</c>
/// or a numeric <c>±HH:MM</c> offset. The <c>T</c> and <c>Z</c> designators may be lowercase, as
/// RFC 3339 permits. Anything else is a <see cref="JsonException"/> naming the value.
/// </para>
/// <para>
/// Registered once on the generated REST serialization context, so every <see cref="Instant"/>
/// property on every model uses it without a per-property attribute. A nullable
/// <c>Instant?</c> property resolves to this converter through the serializer's own nullable
/// wrapper.
/// </para>
/// </remarks>
public sealed class InstantJsonConverter : JsonConverter<Instant>
{
    // YYYY-MM-DDTHH:MM:SS is 19 bytes; the shortest valid value adds a Z.
    private const int DateTimeLength = 19;
    private const int MinLength = DateTimeLength + 1;

    // Longer than any escaped spelling of a timestamp. A raw value past this cannot be one, so it
    // is rejected before any buffer is sized from it.
    private const int MaxRawLength = 64;

    // Indexed by the number of fraction digits read, so the digits scale to nanoseconds.
    private static readonly long[] NanosecondScale =
        [1_000_000_000, 100_000_000, 10_000_000, 1_000_000, 100_000, 10_000, 1_000, 100, 10, 1];

    /// <inheritdoc />
    public override Instant Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected an RFC 3339 timestamp string, found a {reader.TokenType} token.");
        }

        // The fast path reads the bytes in place. A timestamp never needs an escape, and a value
        // split across buffer segments is rare, so the copying path below is for correctness only.
        if (!reader.HasValueSequence && !reader.ValueIsEscaped)
        {
            return Parse(reader.ValueSpan);
        }

        long rawLength = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;

        if (rawLength > MaxRawLength)
        {
            throw new JsonException("Expected an RFC 3339 timestamp string, found a longer value.");
        }

        Span<byte> buffer = stackalloc byte[MaxRawLength];
        int written = reader.CopyString(buffer);

        return Parse(buffer[..written]);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Instant value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(InstantPattern.ExtendedIso.Format(value));
    }

    /// <summary>
    /// Parses an RFC 3339 timestamp from a span of UTF-8 bytes.
    /// </summary>
    /// <param name="utf8">The bytes to parse.</param>
    /// <returns>The parsed instant.</returns>
    /// <exception cref="JsonException">The span does not contain a valid RFC 3339 timestamp.</exception>
    private static Instant Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length < MinLength
            || utf8[4] != (byte)'-'
            || utf8[7] != (byte)'-'
            || (utf8[10] | 0x20) != 't'
            || utf8[13] != (byte)':'
            || utf8[16] != (byte)':'
            || !TryDigits(utf8[..4], out int year)
            || !TryDigits(utf8.Slice(5, 2), out int month)
            || !TryDigits(utf8.Slice(8, 2), out int day)
            || !TryDigits(utf8.Slice(11, 2), out int hour)
            || !TryDigits(utf8.Slice(14, 2), out int minute)
            || !TryDigits(utf8.Slice(17, 2), out int second))
        {
            throw Malformed(utf8);
        }

        int position = DateTimeLength;
        long nanoseconds = 0;

        if (utf8[position] == (byte)'.')
        {
            int start = ++position;

            while (position < utf8.Length && utf8[position] is >= (byte)'0' and <= (byte)'9')
            {
                position++;
            }

            int digits = position - start;

            if (digits is < 1 or > 9 || !TryDigits(utf8.Slice(start, digits), out int fraction))
            {
                throw Malformed(utf8);
            }

            nanoseconds = fraction * NanosecondScale[digits];
        }

        if (position >= utf8.Length)
        {
            throw Malformed(utf8);
        }

        int offsetSeconds = 0;
        byte designator = utf8[position];

        if ((designator | 0x20) == 'z')
        {
            position++;
        }
        else if (designator is (byte)'+' or (byte)'-')
        {
            if (utf8.Length - position != 6
                || utf8[position + 3] != (byte)':'
                || !TryDigits(utf8.Slice(position + 1, 2), out int offsetHours)
                || !TryDigits(utf8.Slice(position + 4, 2), out int offsetMinutes))
            {
                throw Malformed(utf8);
            }

            offsetSeconds = ((offsetHours * 3600) + (offsetMinutes * 60)) * (designator == (byte)'-' ? -1 : 1);
            position += 6;
        }
        else
        {
            throw Malformed(utf8);
        }

        if (position != utf8.Length)
        {
            throw Malformed(utf8);
        }

        try
        {
            LocalDateTime local = new LocalDateTime(year, month, day, hour, minute, second).PlusNanoseconds(nanoseconds);
            return local.WithOffset(Offset.FromSeconds(offsetSeconds)).ToInstant();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JsonException($"'{Encoding.UTF8.GetString(utf8)}' is not a valid timestamp.", ex);
        }
    }

    private static JsonException Malformed(ReadOnlySpan<byte> utf8) =>
        new($"Expected an RFC 3339 timestamp such as 2024-06-24T18:33:53Z, found '{Encoding.UTF8.GetString(utf8)}'.");

    /// <summary>
    /// Attempts to parse a decimal number from a span of at most nine UTF-8 digits.
    /// </summary>
    /// <param name="utf8">The bytes to parse as digits.</param>
    /// <param name="value">The parsed value if successful; otherwise zero.</param>
    /// <returns><c>true</c> if all bytes are ASCII digits; otherwise <c>false</c>.</returns>
    private static bool TryDigits(ReadOnlySpan<byte> utf8, out int value)
    {
        value = 0;

        foreach (byte b in utf8)
        {
            if (b is < (byte)'0' or > (byte)'9')
            {
                return false;
            }

            value = (value * 10) + (b - '0');
        }

        return true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~InstantJsonConverterTests"`
Expected: PASS, 22 tests (10 facts plus 12 theory cases).

- [ ] **Step 5: Run the whole offline suite and the build**

Run: `dotnet build MassiveDotNet.slnx && dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: 0 warnings; every test passes. `TemporalTypeTests` in particular must pass: the new file names only NodaTime types.

- [ ] **Step 6: Commit**

```bash
git add src/MassiveDotNet/Serialization/InstantJsonConverter.cs tests/MassiveDotNet.Rest.Tests/InstantJsonConverterTests.cs
git commit -m "feat: add a byte-level RFC 3339 converter for Instant

Parses YYYY-MM-DDTHH:MM:SS, an optional one-to-nine digit fraction, and
Z or a numeric offset straight from the reader's UTF-8 bytes, the way
LocalDateJsonConverter does for calendar dates, so a page of timestamps
allocates nothing per value. Not yet registered on the generated
context; that arrives with the date-time binding in the generator.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 2: Generator test harness, `Map.Parse`, `Spec.Parse`, `model` key, `SchemaShape`

**Files:**
- Create: `tests/MassiveDotNet.CodeGen.Tests/MassiveDotNet.CodeGen.Tests.csproj`
- Create: `tests/MassiveDotNet.CodeGen.Tests/Harness.cs`
- Create: `tests/MassiveDotNet.CodeGen.Tests/HarnessTests.cs`
- Modify: `MassiveDotNet.slnx`
- Modify: `tools/MassiveDotNet.CodeGen/MassiveDotNet.CodeGen.csproj`
- Modify: `tools/MassiveDotNet.CodeGen/Map.cs`
- Modify: `tools/MassiveDotNet.CodeGen/Spec.cs`

**Interfaces:**
- Consumes: `Emitter(Spec, Map).Emit()` returning `Dictionary<string, string>` keyed by relative path (`Models/Thing.g.cs`, `Envelopes.g.cs`, `ReferenceGroup.g.cs`, `MassiveRestJsonContext.g.cs`).
- Produces: `Map.Parse(string json)`; `Spec.Parse(string json)`; `MapProperty(string? Name, string? Type, string? Model, string? Summary)`; `internal enum SchemaShape { Scalar, Object, Array, ArrayOfObjects, ArrayOfArrays }`; `Spec.Shape(JsonElement) → SchemaShape`; the test helpers `Harness.Document(params Operation[])`, `Harness.Envelope(string items)`, `Harness.MapDocument(string models, string endpoints)`, `Harness.Endpoint(string operationId, string model, string parameters = "{}")`, `Harness.Generate(string spec, string map)`, `Harness.Refusal(string spec, string map)`, and the record `Operation(string Id, string Path, string Envelope, string Parameters = "[]")`. Tasks 3, 4, and 5 write every test against these.

- [ ] **Step 1: Create the test project**

Create `tests/MassiveDotNet.CodeGen.Tests/MassiveDotNet.CodeGen.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>MassiveDotNet.CodeGen.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <!-- The generator is an executable project; referencing one is supported and copies its assembly. -->
    <ProjectReference Include="../../tools/MassiveDotNet.CodeGen/MassiveDotNet.CodeGen.csproj" />
  </ItemGroup>

</Project>
```

Add the project to `MassiveDotNet.slnx`, in the `/tests/` folder, before the integration tests so the folder stays alphabetical:

```xml
  <Folder Name="/tests/">
    <Project Path="tests/MassiveDotNet.CodeGen.Tests/MassiveDotNet.CodeGen.Tests.csproj" />
    <Project Path="tests/MassiveDotNet.IntegrationTests/MassiveDotNet.IntegrationTests.csproj" />
    <Project Path="tests/MassiveDotNet.Rest.Tests/MassiveDotNet.Rest.Tests.csproj" />
  </Folder>
```

Add to `tools/MassiveDotNet.CodeGen/MassiveDotNet.CodeGen.csproj`, after the `PropertyGroup`:

```xml
  <ItemGroup>
    <!-- Every generator type is internal; the diagnostic tests need to construct them. -->
    <InternalsVisibleTo Include="MassiveDotNet.CodeGen.Tests" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing harness test**

Create `tests/MassiveDotNet.CodeGen.Tests/HarnessTests.cs`:

```csharp
using System.Text.Json;
using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>
/// The harness generates from inline documents. This proves the plumbing before any diagnostic
/// test depends on it.
/// </summary>
public sealed class HarnessTests
{
    [Fact]
    public void GeneratesAModelFromAnInlineFragment()
    {
        string spec = Harness.Document(new Operation("ListThings", "/v1/things", Harness.Envelope("""
            {
              "type": "object",
              "required": ["name"],
              "properties": {
                "name": { "type": "string" },
                "score": { "type": "number" }
              }
            }
            """)));

        string map = Harness.MapDocument(
            """
            "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" } }
            """,
            Harness.Endpoint("ListThings", "Thing"));

        Dictionary<string, string> files = Harness.Generate(spec, map);

        string model = files[Path.Combine("Models", "Thing.g.cs")];
        Assert.Contains("public required string Name { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public double? Score { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public Task<Thing[]> ListThingsAsync(", files["ReferenceGroup.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsAModelReferenceFromAPropertyRow()
    {
        Map map = Map.Parse(Harness.MapDocument(
            """
            "Thing": {
              "schema": { "operationId": "ListThings", "pointer": "results/items" },
              "properties": { "publisher": { "name": "Publisher", "model": "Publisher" } }
            }
            """,
            Harness.Endpoint("ListThings", "Thing")));

        MapProperty row = map.Models[0].Properties["publisher"];
        Assert.Equal("Publisher", row.Model);
        Assert.Null(row.Type);
    }

    // The expected shape travels as a string: SchemaShape is internal to the generator, and an
    // internal type cannot appear in a public test method's signature.
    [Theory]
    [InlineData("""{ "type": "string" }""", "Scalar")]
    [InlineData("""{ "enum": ["asc", "desc"] }""", "Scalar")]
    [InlineData("""{ "type": "object" }""", "Object")]
    [InlineData("""{ "properties": { "a": { "type": "string" } } }""", "Object")]
    [InlineData("""{ "allOf": [ { "properties": { "a": { "type": "string" } } } ] }""", "Object")]
    [InlineData("""{ "type": "array", "items": { "type": "string" } }""", "Array")]
    [InlineData("""{ "type": "array" }""", "Array")]
    [InlineData("""{ "type": "array", "items": { "type": "object" } }""", "ArrayOfObjects")]
    [InlineData("""{ "type": "array", "items": { "properties": { "a": { "type": "string" } } } }""", "ArrayOfObjects")]
    [InlineData("""{ "type": "array", "items": { "type": "array", "items": { "type": "string" } } }""", "ArrayOfArrays")]
    public void ClassifiesSchemaShapes(string schema, string expected)
    {
        using JsonDocument document = JsonDocument.Parse(schema);
        Assert.Equal(Enum.Parse<SchemaShape>(expected), Spec.Shape(document.RootElement));
    }
}
```

- [ ] **Step 3: Write the harness**

Create `tests/MassiveDotNet.CodeGen.Tests/Harness.cs`:

```csharp
using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>One GET operation for <see cref="Harness.Document"/>.</summary>
/// <param name="Id">The operation id.</param>
/// <param name="Path">The route.</param>
/// <param name="Envelope">The JSON schema of the 200 response.</param>
/// <param name="Parameters">The JSON array of parameter objects.</param>
internal sealed record Operation(string Id, string Path, string Envelope, string Parameters = "[]");

/// <summary>
/// Builds the smallest OpenAPI document and map the generator accepts, so each test states only
/// the schema and rows it is about.
/// </summary>
internal static class Harness
{
    /// <summary>A document declaring the given operations and nothing else.</summary>
    public static string Document(params Operation[] operations)
    {
        IEnumerable<string> paths = operations.Select(operation => $$"""
            "{{operation.Path}}": {
              "get": {
                "operationId": "{{operation.Id}}",
                "parameters": {{operation.Parameters}},
                "responses": {
                  "200": { "content": { "application/json": { "schema": {{operation.Envelope}} } } }
                }
              }
            }
            """);

        return $$"""
            {
              "components": { "parameters": {} },
              "paths": { {{string.Join(",\n", paths)}} }
            }
            """;
    }

    /// <summary>An envelope whose <c>results</c> is an array of the given item schema.</summary>
    public static string Envelope(string items) => $$"""
        {
          "type": "object",
          "properties": {
            "results": { "type": "array", "items": {{items}} },
            "status": { "type": "string" }
          }
        }
        """;

    /// <summary>A map with one group, <c>Reference</c>, plus the given model and endpoint rows.</summary>
    public static string MapDocument(string models, string endpoints) => $$"""
        {
          "groups": { "Reference": { "summary": "Test group." } },
          "models": { {{models}} },
          "endpoints": [ {{endpoints}} ]
        }
        """;

    /// <summary>An endpoint row returning <c>results</c> as an array of the model.</summary>
    public static string Endpoint(string operationId, string model, string parameters = "{}") => $$"""
        {
          "operationId": "{{operationId}}",
          "group": "Reference",
          "method": "List{{model}}s",
          "result": { "kind": "array", "model": "{{model}}", "property": "results" },
          "parameters": {{parameters}}
        }
        """;

    /// <summary>Runs the emitter over inline documents.</summary>
    public static Dictionary<string, string> Generate(string spec, string map) =>
        new Emitter(Spec.Parse(spec), Map.Parse(map)).Emit();

    /// <summary>The message of the refusal generation raises for these documents.</summary>
    public static string Refusal(string spec, string map) =>
        Assert.Throws<InvalidOperationException>(() => Generate(spec, map)).Message;
}
```

- [ ] **Step 4: Add `Map.Parse` and the `model` key**

In `tools/MassiveDotNet.CodeGen/Map.cs`, change the property record:

```csharp
/// <summary>A property row: a .NET name, and either a verbatim type or a model the schema binds to (D-N2).</summary>
internal sealed record MapProperty(string? Name, string? Type, string? Model, string? Summary);
```

Replace the `Load` method's signature and opening so the file can also be parsed from a string:

```csharp
    public static Map Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Parses a map document. The generator loads from disk; tests hand in fragments.</summary>
    public static Map Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
```

The rest of the body is unchanged except the property row, which reads the new key:

```csharp
                    properties[property.Name] = new MapProperty(
                        String(property.Value, "name"),
                        String(property.Value, "type"),
                        String(property.Value, "model"),
                        String(property.Value, "summary"));
```

- [ ] **Step 5: Add `Spec.Parse`, `SchemaShape`, and `Spec.Shape`**

In `tools/MassiveDotNet.CodeGen/Spec.cs`, add the enum after the `ParameterSlot` record:

```csharp
/// <summary>What a schema node is, as far as binding is concerned (D-N3, D-N5).</summary>
internal enum SchemaShape
{
    /// <summary>A string, number, integer, or boolean, or a node with no type, which defaults to string.</summary>
    Scalar,

    /// <summary>An object, whether or not it declares properties.</summary>
    Object,

    /// <summary>An array of scalars, or an array with no item schema.</summary>
    Array,

    /// <summary>An array whose items are objects.</summary>
    ArrayOfObjects,

    /// <summary>An array whose items are arrays. None exists in the description; refused if one arrives.</summary>
    ArrayOfArrays,
}
```

Replace the constructor with a path constructor, a string factory, and a private document constructor:

```csharp
    public Spec(string path)
        : this(JsonDocument.Parse(File.ReadAllBytes(path)))
    {
    }

    /// <summary>Parses a description. The generator loads from disk; tests hand in fragments.</summary>
    public static Spec Parse(string json) => new(JsonDocument.Parse(json));

    private Spec(JsonDocument document)
    {
        _document = document;
        JsonElement root = _document.RootElement;

        _componentParameters = root.GetProperty("components").GetProperty("parameters");

        foreach (JsonProperty pathItem in root.GetProperty("paths").EnumerateObject())
        {
            if (!pathItem.Value.TryGetProperty("get", out JsonElement operation))
            {
                continue;
            }

            if (!operation.TryGetProperty("operationId", out JsonElement operationId))
            {
                continue;
            }

            string id = operationId.GetString()!;
            _operations[id] = new SpecOperation(id, pathItem.Name, operation);
        }
    }
```

Add `Shape` after `Navigate`:

```csharp
    /// <summary>Classifies a schema node for binding.</summary>
    /// <remarks>
    /// An object is anything typed <c>object</c>, or anything that declares <c>properties</c> or
    /// composes them through <c>allOf</c>, since the description omits the type on some composed
    /// nodes. A free-form object with no properties is still an object: it has no default binding
    /// and the map must name a type for it (D-N6).
    /// </remarks>
    public static SchemaShape Shape(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return SchemaShape.Scalar;
        }

        if (IsObject(schema))
        {
            return SchemaShape.Object;
        }

        if (!schema.TryGetProperty("type", out JsonElement type) || type.GetString() != "array")
        {
            return SchemaShape.Scalar;
        }

        if (!schema.TryGetProperty("items", out JsonElement items))
        {
            return SchemaShape.Array;
        }

        return Shape(items) switch
        {
            SchemaShape.Object => SchemaShape.ArrayOfObjects,
            SchemaShape.Scalar => SchemaShape.Array,
            _ => SchemaShape.ArrayOfArrays,
        };
    }

    private static bool IsObject(JsonElement schema) =>
        (schema.TryGetProperty("type", out JsonElement type) && type.GetString() == "object")
        || schema.TryGetProperty("properties", out _)
        || schema.TryGetProperty("allOf", out _);
```

- [ ] **Step 6: Run the harness tests**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests`
Expected: PASS, 12 tests (2 facts plus 10 theory cases).

- [ ] **Step 7: Confirm the generator's own output is unchanged and the suite is green**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/ && dotnet build MassiveDotNet.slnx && dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: no diff under `src/`; 0 warnings; every test passes, including the new project.

- [ ] **Step 8: Commit**

```bash
git add MassiveDotNet.slnx tools/MassiveDotNet.CodeGen tests/MassiveDotNet.CodeGen.Tests
git commit -m "test: add a generator test project and the model key on property rows

Spec and Map can now be parsed from a string, so tests feed the emitter
inline fragments instead of temp files. SchemaShape classifies a node
as scalar, object, array, array of objects, or array of arrays, which
the binding decisions that follow key on. A property row may name a
model; nothing reads it yet.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 3: `TypeBinding`: no default for objects, arrays by element, `date-time` → `Instant`, parameter refusals

**Files:**
- Modify: `tools/MassiveDotNet.CodeGen/TypeBinding.cs`
- Modify: `tools/MassiveDotNet.CodeGen/Argument.cs`
- Modify: `tools/MassiveDotNet.CodeGen/Emitter.cs` (`EmitJsonContext` only)
- Create: `tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs`
- Create: `tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs`
- Regenerate: `src/MassiveDotNet.Rest/Generated/MassiveRestJsonContext.g.cs`

**Interfaces:**
- Consumes: `Spec.Shape`, `SchemaShape` (Task 2); `InstantJsonConverter` (Task 1).
- Produces: `Spec.Describe(SchemaShape) → string`; `TypeBinding.NeedsModel(JsonElement) → bool`; `TypeBinding.Resolve(string? mapType, JsonElement schema, string operationId, string parameterName)`; `TypeBinding.FromSchema` returning `string[]`, `double[]`, `LocalDate[]`, `Instant`, and throwing on any shape `NeedsModel` is true for. `Argument.Create(SpecParameter, MapParameter?, string operationId)`.

- [ ] **Step 1: Write the failing parameter tests**

Create `tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs`:

```csharp
using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>Parameters with no default binding are refused, naming the row to fix (D-N3, D-N7).</summary>
public sealed class ParameterBindingTests
{
    private const string Item = """{ "type": "object", "properties": { "name": { "type": "string" } } }""";

    private static string Document(string parameters) =>
        Harness.Document(new Operation("ListThings", "/v1/things", Harness.Envelope(Item), parameters));

    private static string MapDocument(string parameters = "{}") => Harness.MapDocument(
        """
        "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" } }
        """,
        Harness.Endpoint("ListThings", "Thing", parameters));

    [Fact]
    public void AnObjectParameterHasNoDefaultBinding()
    {
        string spec = Document("""[ { "name": "filter", "in": "query", "schema": { "type": "object" } } ]""");

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("Operation 'ListThings'", message, StringComparison.Ordinal);
        Assert.Contains("parameter 'filter' is an object", message, StringComparison.Ordinal);
        Assert.Contains("Set \"type\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADateTimeParameterHasNoDefaultBinding()
    {
        string spec = Document("""[ { "name": "since", "in": "query", "schema": { "type": "string", "format": "date-time" } } ]""");

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("parameter 'since' is a date-time string", message, StringComparison.Ordinal);
        Assert.Contains("RFC 3339", message, StringComparison.Ordinal);
        Assert.Contains("Set \"type\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADateTimeComparatorGroupHasNoDefaultBinding()
    {
        // All four range suffixes, so the group resolves to RangeFilter before the element type
        // is asked for; a lone suffix would be refused earlier as an unrecognised shape.
        string spec = Document("""
            [
              { "name": "since",     "in": "query", "schema": { "type": "string", "format": "date-time" } },
              { "name": "since.gt",  "in": "query", "schema": { "type": "string", "format": "date-time" } },
              { "name": "since.gte", "in": "query", "schema": { "type": "string", "format": "date-time" } },
              { "name": "since.lt",  "in": "query", "schema": { "type": "string", "format": "date-time" } },
              { "name": "since.lte", "in": "query", "schema": { "type": "string", "format": "date-time" } }
            ]
            """);

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("parameter 'since' is a date-time string", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMapTypeSatisfiesTheRefusal()
    {
        string spec = Document("""[ { "name": "since", "in": "query", "schema": { "type": "string", "format": "date-time" } } ]""");

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument("""{ "since": { "type": "LocalDate" } }"""));

        Assert.Contains("LocalDate? since = null", files["ReferenceGroup.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void AnArrayOfStringsStaysAStringArray()
    {
        string spec = Document("""[ { "name": "tickers", "in": "query", "schema": { "type": "array", "items": { "type": "string" } } } ]""");

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument());

        Assert.Contains("string[]? tickers = null", files["ReferenceGroup.g.cs"], StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Write the failing model tests for arrays and date-time**

Create `tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs` with these first cases (Task 4 appends more to the same class):

```csharp
using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>How a model property's schema becomes a C# type (D-N2, D-N3, D-N5, D-N6, D-N7).</summary>
public sealed class ModelBindingTests
{
    /// <summary>A document with one operation whose result items have the given schema.</summary>
    private static string Document(string item) =>
        Harness.Document(new Operation("ListThings", "/v1/things", Harness.Envelope(item)));

    /// <summary>A map declaring <c>Thing</c> over the result items, with the given property rows and extra models.</summary>
    private static string MapDocument(string thingProperties = "{}", string otherModels = "") => Harness.MapDocument(
        $$"""
        "Thing": {
          "schema": { "operationId": "ListThings", "pointer": "results/items" },
          "properties": {{thingProperties}}
        }{{(otherModels.Length == 0 ? "" : "," + otherModels)}}
        """,
        Harness.Endpoint("ListThings", "Thing"));

    private static string Thing(Dictionary<string, string> files) => files[Path.Combine("Models", "Thing.g.cs")];

    [Fact]
    public void BindsArraysByTheirElement()
    {
        string spec = Document("""
            {
              "type": "object",
              "required": ["tags"],
              "properties": {
                "tags":   { "type": "array", "items": { "type": "string" } },
                "scores": { "type": "array", "items": { "type": "number" } },
                "counts": { "type": "array", "items": { "type": "integer", "format": "int64" } },
                "dates":  { "type": "array", "items": { "type": "string", "format": "date" } },
                "loose":  { "type": "array" }
              }
            }
            """);

        string model = Thing(Harness.Generate(spec, MapDocument()));

        Assert.Contains("public required string[] Tags { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public double[]? Scores { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public long[]? Counts { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public LocalDate[]? Dates { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public string[]? Loose { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("using NodaTime;", model, StringComparison.Ordinal);
    }

    [Fact]
    public void BindsADateTimeStringToInstant()
    {
        string spec = Document("""
            {
              "type": "object",
              "required": ["published"],
              "properties": {
                "published": { "type": "string", "format": "date-time" },
                "updated":   { "type": "string", "format": "date-time" }
              }
            }
            """);

        string model = Thing(Harness.Generate(spec, MapDocument()));

        // A value type: required, but no modifier (D-N6).
        Assert.Contains("public Instant Published { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public Instant? Updated { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("using NodaTime;", model, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistersBothConvertersOnTheContext()
    {
        string context = Harness.Generate(Document("""{ "type": "object" }"""), MapDocument())["MassiveRestJsonContext.g.cs"];

        Assert.Contains("typeof(LocalDateJsonConverter), typeof(InstantJsonConverter)", context, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 3: Run the new tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests`
Expected: `AnObjectParameterHasNoDefaultBinding`, `ADateTimeParameterHasNoDefaultBinding`, and `ADateTimeComparatorGroupHasNoDefaultBinding` FAIL (no exception is thrown, or the message differs); `BindsArraysByTheirElement` FAILS on `double[]?`; `BindsADateTimeStringToInstant` FAILS on `Instant`; `RegistersBothConvertersOnTheContext` FAILS. `AMapTypeSatisfiesTheRefusal` and `AnArrayOfStringsStaysAStringArray` already pass.

- [ ] **Step 4: Rewrite `FromSchema`, add `NeedsModel`, and add the parameter refusals**

In `tools/MassiveDotNet.CodeGen/TypeBinding.cs`, replace `Resolve`, `ResolveFilter`'s first line, and `FromSchema`:

```csharp
    /// <summary>Resolves the binding for a parameter, honouring a map-supplied override.</summary>
    /// <param name="mapType">The map row's <c>type</c>, or <see langword="null"/> to derive one.</param>
    /// <param name="schema">The parameter's schema.</param>
    /// <param name="operationId">The operation, named in any diagnostic.</param>
    /// <param name="parameterName">The parameter's wire name, named in any diagnostic.</param>
    public static TypeBinding Resolve(string? mapType, JsonElement schema, string operationId, string parameterName)
    {
        string type = mapType ?? DefaultParameterType(schema, operationId, parameterName);

        return type switch
        {
            // Enum wire values are fixed literals, so they need no percent-escaping.
            "AggregateTimespan" or "SortOrder" or "MarketType" =>
                new TypeBinding(type, "AppendPathLiteral", "ToWireValue()"),

            "DateOrTimestamp" =>
                new TypeBinding(type, "AppendPathSegment", "ToString()"),

            // NodaTime types are the SDK's temporal vocabulary (constitution rule 12).
            // Their wire forms are fixed literals, so they need no percent-escaping.
            "LocalDate" or "Instant" =>
                new TypeBinding(type, "AppendPathLiteral", "ToWireValue()"),

            // Numeric segments use the builder's numeric overloads, which format in place.
            "int" or "long" =>
                new TypeBinding(type, "AppendPathSegment", null),

            _ =>
                new TypeBinding(type, "AppendPathSegment", null),
        };
    }
```

In `ResolveFilter`, replace `string element = mapType ?? FromSchema(schema);` with:

```csharp
        string element = mapType ?? DefaultParameterType(schema, operationId, group.BaseName);
```

Replace `FromSchema` and add the two helpers:

```csharp
    /// <summary>
    /// Whether a schema binds only through the map: an object, an array of objects, or an array
    /// of arrays (D-N3). Callers check this before <see cref="FromSchema"/> and raise a diagnostic
    /// that names the operation and field.
    /// </summary>
    public static bool NeedsModel(JsonElement schema) =>
        Spec.Shape(schema) is SchemaShape.Object or SchemaShape.ArrayOfObjects or SchemaShape.ArrayOfArrays;

    /// <summary>Derives the default C# type for a scalar or array-of-scalar schema node.</summary>
    /// <exception cref="InvalidOperationException">
    /// The node is a shape with no default. This is a generator bug, not a map error: every caller
    /// checks <see cref="NeedsModel"/> first and produces a diagnostic with context.
    /// </exception>
    public static string FromSchema(JsonElement schema)
    {
        if (NeedsModel(schema))
        {
            throw new InvalidOperationException(
                "An object or nested array schema has no default binding. The caller must check NeedsModel "
                + "and raise a diagnostic naming the operation and field.");
        }

        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out JsonElement type))
        {
            // Some parameters declare only an enum, with no explicit type (for example `sort`).
            return "string";
        }

        string format = schema.TryGetProperty("format", out JsonElement formatValue)
            ? formatValue.GetString() ?? string.Empty
            : string.Empty;

        return type.GetString() switch
        {
            "integer" => format == "int64" ? "long" : "int",
            "number" => "double",
            "boolean" => "bool",
            // An array binds by its element (D-N5); one with no item schema defaults like a typeless node.
            "array" => schema.TryGetProperty("items", out JsonElement items) ? $"{FromSchema(items)}[]" : "string[]",
            // A calendar date is a LocalDate on parameters and models alike (D-F7, D-F9); a
            // timestamp is an Instant on models (D-N7). Parameters never reach the Instant arm:
            // DefaultParameterType refuses a date-time parameter before asking here.
            "string" => format switch
            {
                "date" => "LocalDate",
                "date-time" => "Instant",
                _ => "string",
            },
            _ => "string",
        };
    }

    /// <summary>
    /// The default type for a parameter, refusing the two shapes that have none (D-N3, D-N7).
    /// </summary>
    /// <remarks>
    /// A date-time parameter is refused because the parameter-side <c>Instant</c> renders Unix
    /// milliseconds (<c>ToWireValue</c>), which is not RFC 3339. The map chooses the form; the
    /// only such parameters in the description are the news <c>published_utc</c> family, which
    /// carry no top-level type and so default to string, and which the map binds to LocalDate.
    /// </remarks>
    private static string DefaultParameterType(JsonElement schema, string operationId, string parameterName)
    {
        if (NeedsModel(schema))
        {
            throw new InvalidOperationException(
                $"Operation '{operationId}': parameter '{parameterName}' is {Spec.Describe(Spec.Shape(schema))}, which has no "
                + "default binding. Set \"type\" on its row in specs/endpoints.map.json.");
        }

        if (schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("format", out JsonElement format)
            && format.GetString() == "date-time")
        {
            throw new InvalidOperationException(
                $"Operation '{operationId}': parameter '{parameterName}' is a date-time string, which has no default "
                + "binding on a parameter: Instant renders Unix milliseconds on the wire, not RFC 3339. Set \"type\" "
                + "on its row in specs/endpoints.map.json (LocalDate for the calendar-date form).");
        }

        return FromSchema(schema);
    }
```

`Spec.Describe` does not exist yet. Add it to `tools/MassiveDotNet.CodeGen/Spec.cs` after `Shape`:

```csharp
    /// <summary>A shape as it reads in a diagnostic: "an object", "an array of objects".</summary>
    public static string Describe(SchemaShape shape) => shape switch
    {
        SchemaShape.Object => "an object",
        SchemaShape.ArrayOfObjects => "an array of objects",
        SchemaShape.ArrayOfArrays => "an array of arrays",
        SchemaShape.Array => "an array of scalars",
        _ => "a scalar",
    };
```

- [ ] **Step 5: Thread the operation id through `Argument.Create`**

In `tools/MassiveDotNet.CodeGen/Argument.cs`, the slot overload's first line becomes:

```csharp
        if (slot.Group is null)
        {
            return Create(slot.Parameter, mapped, operationId);
        }
```

and the plain overload becomes:

```csharp
    public static Argument Create(SpecParameter parameter, MapParameter? mapped, string operationId) => new(
        parameter.Name,
        mapped?.Name ?? parameter.Name,
        parameter.Required,
        parameter.In,
        Prose.Clean(parameter.Description),
        TypeBinding.Resolve(mapped?.Type, parameter.Schema, operationId, parameter.Name));
```

There are no other callers of `Create(SpecParameter, MapParameter?)`; `Emitter.Arguments` already calls the slot overload with the operation id.

- [ ] **Step 6: Register the converter on the generated context**

In `tools/MassiveDotNet.CodeGen/Emitter.cs`, `EmitJsonContext`, replace the remarks and the attribute line:

```csharp
        writer.Doc("remarks", "Calendar dates are read by <see cref=\"LocalDateJsonConverter\"/> and RFC 3339 timestamps by <see cref=\"InstantJsonConverter\"/>, registered here once so no model property needs its own attribute.", preserveMarkup: true);
        writer.Line("[JsonSourceGenerationOptions(Converters = new[] { typeof(LocalDateJsonConverter), typeof(InstantJsonConverter) })]");
```

- [ ] **Step 7: Run the generator tests**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests`
Expected: PASS, all tests.

- [ ] **Step 8: Regenerate and run everything**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git status --short src/ && dotnet build MassiveDotNet.slnx && dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: exactly one file changed under `src/`, `MassiveDotNet.Rest/Generated/MassiveRestJsonContext.g.cs`, now naming both converters. `Agg.g.cs`, `Dividend.g.cs`, `Envelopes.g.cs`, and `StocksGroup.g.cs` are byte-identical: no existing property is an object, array, or date-time. 0 warnings; every test passes.

- [ ] **Step 9: Commit**

```bash
git add tools/MassiveDotNet.CodeGen tests/MassiveDotNet.CodeGen.Tests src/MassiveDotNet.Rest/Generated
git commit -m "feat: bind arrays by element and date-time to Instant; refuse unbound parameters

FromSchema no longer has a default for an object, an array of objects,
or an array of arrays: NeedsModel identifies those and every caller is
expected to raise a diagnostic with context. Arrays bind by their item
schema instead of always string[]. A date-time string is an Instant on
a model, read by InstantJsonConverter, now registered on the context.
A parameter that is an object or a date-time string is refused with the
row to set, since the parameter-side Instant renders epoch milliseconds.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 4: `Emitter`: model composition, unbound-object diagnostics, value-type set

**Files:**
- Modify: `tools/MassiveDotNet.CodeGen/Emitter.cs`
- Modify: `tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs`

**Interfaces:**
- Consumes: `MapProperty.Model` (Task 2), `TypeBinding.NeedsModel`, `Spec.Shape`, `Spec.Describe` (Task 3).
- Produces: private `Emitter.PropertyType(MapModel, SpecProperty, MapProperty?)`, `Emitter.ModelReferenceType(MapModel owner, SpecProperty, string modelName)` (Task 5 inserts the reuse check into it), `Emitter.EnvelopeType(MapEndpoint, SpecProperty)`, and the `ValueTypes` set.

- [ ] **Step 1: Append the failing tests**

Append to the `ModelBindingTests` class in `tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs`:

```csharp
    private const string Publisher = """
        {
          "type": "object",
          "required": ["name"],
          "properties": {
            "name": { "type": "string" },
            "url":  { "type": "string" }
          }
        }
        """;

    private const string Insight = """
        {
          "type": "object",
          "required": ["ticker"],
          "properties": { "ticker": { "type": "string" } }
        }
        """;

    private static string Article(string requiredNames) => $$"""
        {
          "type": "object",
          "required": [{{requiredNames}}],
          "properties": {
            "title":     { "type": "string" },
            "publisher": {{Publisher}},
            "insights":  { "type": "array", "items": {{Insight}} }
          }
        }
        """;

    private const string NestedModels = """
        "Publisher": { "schema": { "operationId": "ListThings", "pointer": "results/items/publisher" } },
        "Insight":   { "schema": { "operationId": "ListThings", "pointer": "results/items/insights/items" } }
        """;

    private const string NestedRows = """
        { "publisher": { "model": "Publisher" }, "insights": { "model": "Insight" } }
        """;

    [Fact]
    public void AnUnboundObjectPropertyNamesTheRowToAdd()
    {
        string message = Harness.Refusal(Document(Article("\"publisher\"")), MapDocument());

        Assert.Contains("Operation 'ListThings': property 'publisher' of model 'Thing' is an object with no binding", message, StringComparison.Ordinal);
        Assert.Contains("\"schema\": { \"operationId\": \"ListThings\", \"pointer\": \"results/items/publisher\" }", message, StringComparison.Ordinal);
        Assert.Contains("set \"model\": \"<Name>\" on the 'publisher' row of 'Thing'", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnboundArrayOfObjectsNamesTheItemsPointer()
    {
        string map = MapDocument("""{ "publisher": { "model": "Publisher" } }""", NestedModels);

        string message = Harness.Refusal(Document(Article("")), map);

        Assert.Contains("property 'insights' of model 'Thing' is an array of objects with no binding", message, StringComparison.Ordinal);
        Assert.Contains("\"pointer\": \"results/items/insights/items\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnboundEnvelopeObjectPointsAtSingularResults()
    {
        string spec = Harness.Document(new Operation("ListThings", "/v1/things", """
            {
              "type": "object",
              "properties": {
                "results": { "type": "array", "items": { "type": "object", "properties": { "name": { "type": "string" } } } },
                "meta":    { "type": "object", "properties": { "count": { "type": "integer" } } }
              }
            }
            """));

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("Operation 'ListThings': envelope property 'meta' is an object", message, StringComparison.Ordinal);
        Assert.Contains("#31", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposesARequiredObjectAndAnOptionalArray()
    {
        Dictionary<string, string> files = Harness.Generate(Document(Article("\"publisher\"")), MapDocument(NestedRows, NestedModels));

        string thing = Thing(files);
        Assert.Contains("public required Publisher Publisher { get; init; }", thing, StringComparison.Ordinal);
        Assert.Contains("public Insight[]? Insights { get; init; }", thing, StringComparison.Ordinal);

        string publisher = files[Path.Combine("Models", "Publisher.g.cs")];
        Assert.Contains("public sealed partial record Publisher", publisher, StringComparison.Ordinal);
        Assert.Contains("public required string Name { get; init; }", publisher, StringComparison.Ordinal);
        Assert.Contains("public string? Url { get; init; }", publisher, StringComparison.Ordinal);
        Assert.Contains("public required string Ticker { get; init; }", files[Path.Combine("Models", "Insight.g.cs")], StringComparison.Ordinal);
    }

    [Fact]
    public void ComposesAnOptionalObjectAndARequiredArray()
    {
        string thing = Thing(Harness.Generate(Document(Article("\"insights\"")), MapDocument(NestedRows, NestedModels)));

        Assert.Contains("public Publisher? Publisher { get; init; }", thing, StringComparison.Ordinal);
        Assert.Contains("public required Insight[] Insights { get; init; }", thing, StringComparison.Ordinal);
    }

    [Fact]
    public void AStructModelIsNeverMarkedRequired()
    {
        string models = """
            "Publisher": { "kind": "struct", "schema": { "operationId": "ListThings", "pointer": "results/items/publisher" } },
            "Insight":   { "schema": { "operationId": "ListThings", "pointer": "results/items/insights/items" } }
            """;

        string thing = Thing(Harness.Generate(Document(Article("\"publisher\"")), MapDocument(NestedRows, models)));

        Assert.Contains("public Publisher Publisher { get; init; }", thing, StringComparison.Ordinal);
        Assert.DoesNotContain("required Publisher", thing, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelAndTypeOnOneRowConflict()
    {
        string map = MapDocument("""{ "publisher": { "model": "Publisher", "type": "Publisher" }, "insights": { "model": "Insight" } }""", NestedModels);

        string message = Harness.Refusal(Document(Article("")), map);

        Assert.Contains("property 'publisher' carries both \"model\" and \"type\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AModelMustBeDeclared()
    {
        string map = MapDocument("""{ "publisher": { "model": "Nope" }, "insights": { "model": "Insight" } }""", NestedModels);

        string message = Harness.Refusal(Document(Article("")), map);

        Assert.Contains("property 'publisher' names model 'Nope', which is not declared", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AModelOnAScalarIsRefused()
    {
        string map = MapDocument("""{ "title": { "model": "Publisher" }, "publisher": { "model": "Publisher" }, "insights": { "model": "Insight" } }""", NestedModels);

        string message = Harness.Refusal(Document(Article("")), map);

        Assert.Contains("property 'title' names model 'Publisher', but its schema is a scalar", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFreeFormObjectTakesAVerbatimTypeAndIsARequiredReference()
    {
        string spec = Document("""
            {
              "type": "object",
              "required": ["counts"],
              "properties": { "counts": { "type": "object", "description": "A map of exchange id to size." } }
            }
            """);

        string thing = Thing(Harness.Generate(spec, MapDocument("""{ "counts": { "type": "Dictionary<string, double>" } }""")));

        Assert.Contains("public required Dictionary<string, double> Counts { get; init; }", thing, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueTypeIsNeverMarkedRequired()
    {
        string spec = Document("""
            {
              "type": "object",
              "required": ["when", "count", "flag", "ratio"],
              "properties": {
                "when":  { "type": "string", "format": "date" },
                "count": { "type": "integer" },
                "flag":  { "type": "boolean" },
                "ratio": { "type": "number" }
              }
            }
            """);

        string thing = Thing(Harness.Generate(spec, MapDocument()));

        Assert.Contains("public LocalDate When { get; init; }", thing, StringComparison.Ordinal);
        Assert.Contains("public int Count { get; init; }", thing, StringComparison.Ordinal);
        Assert.Contains("public bool Flag { get; init; }", thing, StringComparison.Ordinal);
        Assert.Contains("public double Ratio { get; init; }", thing, StringComparison.Ordinal);
        Assert.DoesNotContain("required", thing, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests`
Expected: the eleven new tests FAIL. The unbound cases currently surface `FromSchema`'s generator-bug message rather than the diagnostic; the composition cases throw for the same reason; `AFreeFormObjectTakesAVerbatimTypeAndIsARequiredReference` emits no `required`.

- [ ] **Step 3: Compose types and raise the diagnostics in `EmitModel`**

In `tools/MassiveDotNet.CodeGen/Emitter.cs`, `EmitModel`, replace the member projection's type expression `mapped?.Type ?? DefaultPropertyType(property)` with `PropertyType(model, property, mapped)`:

```csharp
        List<(SpecProperty Property, string Name, string Type, string? Summary)> members = [.. properties.Select(property =>
        {
            model.Properties.TryGetValue(property.Name, out MapProperty? mapped);

            return (
                property,
                mapped?.Name ?? Naming.Pascal(property.Name),
                PropertyType(model, property, mapped),
                mapped?.Summary ?? Prose.Clean(property.Description));
        })];
```

Add these methods next to `DefaultPropertyType`:

```csharp
    /// <summary>
    /// The C# type of a model property: the row's verbatim <c>type</c>, the model its row names
    /// (D-N2), or the schema's default. An object with neither is refused (D-N3).
    /// </summary>
    private string PropertyType(MapModel model, SpecProperty property, MapProperty? mapped)
    {
        if (mapped is { Model: not null, Type: not null })
        {
            throw new InvalidOperationException(
                $"Model '{model.Name}' (operation '{model.SchemaOperationId}'): property '{property.Name}' carries both "
                + "\"model\" and \"type\". A row names a model, whose nullability and array-ness the spec supplies, "
                + "or a verbatim type, never both.");
        }

        if (mapped?.Model is { } modelName)
        {
            return ModelReferenceType(model, property, modelName);
        }

        if (mapped?.Type is { } type)
        {
            return type;
        }

        if (TypeBinding.NeedsModel(property.Schema))
        {
            throw Unbound(model, property);
        }

        return DefaultPropertyType(property);
    }

    /// <summary>
    /// The diagnostic for an object with no binding: it names the row to add and the row to
    /// point at it, so the fix is a paste rather than a search (D-N3).
    /// </summary>
    private static InvalidOperationException Unbound(MapModel model, SpecProperty property)
    {
        SchemaShape shape = Spec.Shape(property.Schema);
        string described = Spec.Describe(shape);

        if (shape == SchemaShape.ArrayOfArrays)
        {
            return new InvalidOperationException(
                $"Operation '{model.SchemaOperationId}': property '{property.Name}' of model '{model.Name}' is {described}, "
                + "which has no binding. Set \"type\" on its row in specs/endpoints.map.json.");
        }

        string pointer = shape == SchemaShape.ArrayOfObjects
            ? $"{model.SchemaPointer}/{property.Name}/items"
            : $"{model.SchemaPointer}/{property.Name}";

        return new InvalidOperationException(
            $"Operation '{model.SchemaOperationId}': property '{property.Name}' of model '{model.Name}' is {described} with no binding.\n"
            + "Add a row to \"models\" in specs/endpoints.map.json:\n"
            + $"  \"<Name>\": {{ \"schema\": {{ \"operationId\": \"{model.SchemaOperationId}\", \"pointer\": \"{pointer}\" }} }}\n"
            + $"and set \"model\": \"<Name>\" on the '{property.Name}' row of '{model.Name}'. "
            + "A free-form object with no declared properties takes \"type\" instead, such as \"Dictionary<string, double>\" (D-N6).");
    }

    /// <summary>
    /// Composes the type for a property whose row names a model: the model, or an array of it,
    /// nullable when the schema does not require the property (D-N2).
    /// </summary>
    private string ModelReferenceType(MapModel owner, SpecProperty property, string modelName)
    {
        MapModel target = map.Models.Find(m => m.Name == modelName)
            ?? throw new InvalidOperationException(
                $"Model '{owner.Name}' (operation '{owner.SchemaOperationId}'): property '{property.Name}' names model "
                + $"'{modelName}', which is not declared in \"models\" in specs/endpoints.map.json.");

        SchemaShape shape = Spec.Shape(property.Schema);

        if (shape is not (SchemaShape.Object or SchemaShape.ArrayOfObjects))
        {
            throw new InvalidOperationException(
                $"Model '{owner.Name}' (operation '{owner.SchemaOperationId}'): property '{property.Name}' names model "
                + $"'{modelName}', but its schema is {Spec.Describe(shape)}, not an object or an array of objects. "
                + "Use \"type\" for anything else.");
        }

        string type = shape == SchemaShape.ArrayOfObjects ? $"{target.Name}[]" : target.Name;

        return property.Required ? type : $"{type}?";
    }
```

- [ ] **Step 4: Refuse envelope-level objects**

In `EmitEnvelopes`, replace `NullableEnvelopeType(property)` in the members projection with `EnvelopeType(endpoint, property)`, and add:

```csharp
    /// <summary>
    /// The type of an envelope property other than the result. Envelopes have no map rows, so an
    /// object here has nowhere to be bound until singular results are designed (D-N3, #31).
    /// </summary>
    private static string EnvelopeType(MapEndpoint endpoint, SpecProperty property)
    {
        if (TypeBinding.NeedsModel(property.Schema))
        {
            throw new InvalidOperationException(
                $"Operation '{endpoint.OperationId}': envelope property '{property.Name}' is {Spec.Describe(Spec.Shape(property.Schema))}. "
                + "Envelope-level objects have no binding until singular results are designed (issue #31); only the "
                + "result property, named by the endpoint's \"result\" row, is bound.");
        }

        return NullableEnvelopeType(property);
    }
```

- [ ] **Step 5: Invert the `required` check to a value-type set**

Replace `NeedsRequiredModifier` and its remarks:

```csharp
    /// <summary>
    /// Whether a schema-required property needs the C# <c>required</c> modifier to satisfy
    /// nullable reference analysis.
    /// </summary>
    /// <remarks>
    /// A required value type already has a non-null default and needs nothing; a required
    /// reference type does not: the compiler-synthesized constructor exits without assigning it,
    /// which is CS8618 under this repository's warnings-as-errors build. The schema calling the
    /// field required is the fact being encoded -- <c>required</c> states it honestly, rather than
    /// an initializer such as <c>= null!</c> that pretends a value exists before one is read, or a
    /// nullable annotation that pretends the field might be absent when the schema says it never
    /// is. Value types are a closed set (D-N6): the scalars the generator emits, the NodaTime
    /// types, and any <c>struct</c> model. Everything else -- <c>string</c>, arrays, class models,
    /// a map-supplied <c>Dictionary&lt;string, T&gt;</c> -- is a reference type and gets the
    /// modifier, which is the safe direction: a spurious modifier and a missing one are both
    /// compile errors in this build, so neither can ship. Read from the resolved type string
    /// alone, so the check is order-independent and rule 6 holds.
    /// </remarks>
    /// <param name="required">Whether the schema declares the property required.</param>
    /// <param name="type">The property's resolved C# type, nullable annotation included.</param>
    /// <returns><see langword="true"/> when the property needs the <c>required</c> modifier.</returns>
    private bool NeedsRequiredModifier(bool required, string type) =>
        required
        && !type.EndsWith('?')
        && !ValueTypes.Contains(type)
        && !map.Models.Exists(m => m.Name == type && m.Kind == "struct");

    /// <summary>The value types the generator emits on models (D-N6). Extend with the vocabulary, never ad hoc.</summary>
    private static readonly HashSet<string> ValueTypes = new(StringComparer.Ordinal)
    {
        "int",
        "long",
        "double",
        "bool",
        "LocalDate",
        "Instant",
    };
```

- [ ] **Step 6: Run the generator tests**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests`
Expected: PASS, all tests.

- [ ] **Step 7: Regenerate and run everything**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/ && dotnet build MassiveDotNet.slnx && dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: no diff under `src/` (the inverted check classifies every existing property the same way); 0 warnings; every test passes.

- [ ] **Step 8: Commit**

```bash
git add tools/MassiveDotNet.CodeGen tests/MassiveDotNet.CodeGen.Tests
git commit -m "feat: compose nested model types from the map and refuse unbound objects

A property row's model key names a models row; the emitter composes T,
T?, T[], or T[]? from the schema at the site. An object, an array of
objects, or an array of arrays with neither model nor type fails
generation naming the operation, the model, the property, and the exact
row to paste, on models and on envelopes alike. The required-modifier
check now keys on a closed value-type set, so a map-supplied dictionary
is treated as the reference type it is.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 5: Reuse verification (D-N4)

**Files:**
- Modify: `tools/MassiveDotNet.CodeGen/Spec.cs`
- Modify: `tools/MassiveDotNet.CodeGen/Emitter.cs` (`ModelReferenceType`)
- Create: `tests/MassiveDotNet.CodeGen.Tests/ReuseVerificationTests.cs`

**Interfaces:**
- Consumes: `Spec.Properties`, `Spec.Shape`, `Spec.Describe`, `Spec.Navigate`, `Spec.SuccessSchema`; `Emitter.ModelReferenceType` (Task 4).
- Produces: `Spec.StructuralDifferences(JsonElement model, JsonElement site) → List<string>`, empty when the site matches.

- [ ] **Step 1: Write the failing tests**

Create `tests/MassiveDotNet.CodeGen.Tests/ReuseVerificationTests.cs`:

```csharp
using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>
/// A model name may cover only one shape. Every site that names a model is compared with the
/// schema the model was generated from: names exactly, model-required properties required at the
/// site, recursively; never scalar types (D-N4).
/// </summary>
public sealed class ReuseVerificationTests
{
    /// <summary>The publisher <c>Publisher</c> is generated from, on the first operation.</summary>
    private const string OriginPublisher = """
        {
          "type": "object",
          "required": ["name"],
          "properties": {
            "name":    { "type": "string" },
            "url":     { "type": "string" },
            "address": { "type": "object", "properties": { "city": { "type": "string" }, "zip": { "type": "string" } } }
          }
        }
        """;

    private static string Item(string publisher) => $$"""
        { "type": "object", "properties": { "title": { "type": "string" }, "publisher": {{publisher}} } }
        """;

    /// <summary>Two operations: things, whose publisher defines the model, and others, which reuse it.</summary>
    private static string Document(string sitePublisher) => Harness.Document(
        new Operation("ListThings", "/v1/things", Harness.Envelope(Item(OriginPublisher))),
        new Operation("ListOthers", "/v1/others", Harness.Envelope(Item(sitePublisher))));

    private static readonly string MapDocument = Harness.MapDocument(
        """
        "Thing":     { "schema": { "operationId": "ListThings", "pointer": "results/items" }, "properties": { "publisher": { "model": "Publisher" } } },
        "Other":     { "schema": { "operationId": "ListOthers", "pointer": "results/items" }, "properties": { "publisher": { "model": "Publisher" } } },
        "Publisher": { "schema": { "operationId": "ListThings", "pointer": "results/items/publisher" }, "properties": { "address": { "model": "Address" } } },
        "Address":   { "schema": { "operationId": "ListThings", "pointer": "results/items/publisher/address" } }
        """,
        Harness.Endpoint("ListThings", "Thing") + "," + Harness.Endpoint("ListOthers", "Other"));

    [Fact]
    public void ASiteIdenticalToTheModelPasses()
    {
        Dictionary<string, string> files = Harness.Generate(Document(OriginPublisher), MapDocument);

        Assert.Contains("public Publisher? Publisher { get; init; }", files[Path.Combine("Models", "Other.g.cs")], StringComparison.Ordinal);
    }

    [Fact]
    public void ASiteWhoseScalarTypesDifferPasses()
    {
        // The model row is the curated truth for types; the description disagrees with itself at
        // sites that are plainly the same thing.
        string site = OriginPublisher.Replace("\"url\":     { \"type\": \"string\" }", "\"url\":     { \"type\": \"integer\" }", StringComparison.Ordinal);

        Dictionary<string, string> files = Harness.Generate(Document(site), MapDocument);

        Assert.Contains("Other.g.cs", string.Join(";", files.Keys), StringComparison.Ordinal);
    }

    [Fact]
    public void ASiteRequiringMoreThanTheModelPasses()
    {
        string site = OriginPublisher.Replace("\"required\": [\"name\"]", "\"required\": [\"name\", \"url\"]", StringComparison.Ordinal);

        Dictionary<string, string> files = Harness.Generate(Document(site), MapDocument);

        Assert.Contains("Other.g.cs", string.Join(";", files.Keys), StringComparison.Ordinal);
    }

    [Fact]
    public void AnExtraPropertyAtTheSiteIsRefused()
    {
        string site = OriginPublisher.Replace("\"url\":     { \"type\": \"string\" },", "\"url\": { \"type\": \"string\" }, \"extra\": { \"type\": \"string\" },", StringComparison.Ordinal);

        string message = Harness.Refusal(Document(site), MapDocument);

        Assert.Contains("Model 'Other' (operation 'ListOthers'): property 'publisher' names model 'Publisher'", message, StringComparison.Ordinal);
        Assert.Contains("generated from operation 'ListThings' at 'results/items/publisher'", message, StringComparison.Ordinal);
        Assert.Contains("'extra' is declared at the site but not on the model", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingPropertyAtTheSiteIsRefused()
    {
        string site = OriginPublisher.Replace("\"url\":     { \"type\": \"string\" },", "", StringComparison.Ordinal);

        string message = Harness.Refusal(Document(site), MapDocument);

        Assert.Contains("'url' is on the model but not declared at the site", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AModelRequiredPropertyOptionalAtTheSiteIsRefused()
    {
        string site = OriginPublisher.Replace("\"required\": [\"name\"],", "", StringComparison.Ordinal);

        string message = Harness.Refusal(Document(site), MapDocument);

        Assert.Contains("'name' is required on the model but optional at the site", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMismatchTwoLevelsDownIsRefused()
    {
        string site = OriginPublisher.Replace(", \"zip\": { \"type\": \"string\" }", "", StringComparison.Ordinal);

        string message = Harness.Refusal(Document(site), MapDocument);

        Assert.Contains("'address/zip' is on the model but not declared at the site", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AShapeMismatchIsRefused()
    {
        string site = OriginPublisher.Replace(
            "\"address\": { \"type\": \"object\", \"properties\": { \"city\": { \"type\": \"string\" }, \"zip\": { \"type\": \"string\" } } }",
            "\"address\": { \"type\": \"string\" }",
            StringComparison.Ordinal);

        string message = Harness.Refusal(Document(site), MapDocument);

        Assert.Contains("'address' is an object on the model but a scalar at the site", message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDifferenceIsListedTogether()
    {
        string site = OriginPublisher
            .Replace("\"required\": [\"name\"],", "", StringComparison.Ordinal)
            .Replace(", \"zip\": { \"type\": \"string\" }", "", StringComparison.Ordinal);

        string message = Harness.Refusal(Document(site), MapDocument);

        Assert.Contains("'name' is required on the model but optional at the site", message, StringComparison.Ordinal);
        Assert.Contains("'address/zip' is on the model but not declared at the site", message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests --filter "FullyQualifiedName~ReuseVerificationTests"`
Expected: the three passing cases PASS already; the six refusal cases FAIL because generation succeeds.

- [ ] **Step 3: Add `Spec.StructuralDifferences`**

In `tools/MassiveDotNet.CodeGen/Spec.cs`, after `Describe`:

```csharp
    /// <summary>
    /// The ways a reuse site's schema differs from the schema a model was generated from (D-N4).
    /// Property names must match exactly; a property the model's schema requires must be required
    /// at the site; both comparisons recurse through nested objects and arrays of objects. Scalar
    /// types are not compared: the model row is the curated truth, and the description disagrees
    /// with itself at sites that are plainly the same thing. Empty when the site matches.
    /// </summary>
    /// <param name="model">The schema at the model's own pointer.</param>
    /// <param name="site">The schema at the property that names the model.</param>
    /// <returns>One sentence per difference, in the description's declaration order.</returns>
    public static List<string> StructuralDifferences(JsonElement model, JsonElement site)
    {
        List<string> differences = [];
        Compare(model, site, "", differences);
        return differences;
    }

    private static void Compare(JsonElement model, JsonElement site, string path, List<string> differences)
    {
        List<SpecProperty> modelProperties = Properties(model);
        List<SpecProperty> siteProperties = Properties(site);

        foreach (SpecProperty extra in siteProperties.Where(s => !modelProperties.Exists(m => m.Name == s.Name)))
        {
            differences.Add($"'{path}{extra.Name}' is declared at the site but not on the model");
        }

        foreach (SpecProperty expected in modelProperties)
        {
            SpecProperty? actual = siteProperties.Find(s => s.Name == expected.Name);

            if (actual is null)
            {
                differences.Add($"'{path}{expected.Name}' is on the model but not declared at the site");
                continue;
            }

            if (expected.Required && !actual.Required)
            {
                differences.Add($"'{path}{expected.Name}' is required on the model but optional at the site");
            }

            SchemaShape modelShape = Shape(expected.Schema);
            SchemaShape siteShape = Shape(actual.Schema);

            if (modelShape == SchemaShape.Object && siteShape == SchemaShape.Object)
            {
                Compare(expected.Schema, actual.Schema, $"{path}{expected.Name}/", differences);
            }
            else if (modelShape == SchemaShape.ArrayOfObjects && siteShape == SchemaShape.ArrayOfObjects)
            {
                Compare(
                    expected.Schema.GetProperty("items"),
                    actual.Schema.GetProperty("items"),
                    $"{path}{expected.Name}/items/",
                    differences);
            }
            else if (modelShape != siteShape && (IsStructured(modelShape) || IsStructured(siteShape)))
            {
                differences.Add(
                    $"'{path}{expected.Name}' is {Describe(modelShape)} on the model but {Describe(siteShape)} at the site");
            }
        }
    }

    private static bool IsStructured(SchemaShape shape) =>
        shape is SchemaShape.Object or SchemaShape.ArrayOfObjects;
```

- [ ] **Step 4: Run the check from `ModelReferenceType`**

In `tools/MassiveDotNet.CodeGen/Emitter.cs`, `ModelReferenceType`, insert between the shape check and the `string type = ...` line:

```csharp
        // A name may cover only one shape. The site is compared with the schema the model was
        // generated from, so a reused model is proven identical everywhere it appears (D-N4).
        // The comparison also runs where the site is the origin itself; a schema always matches
        // itself, so that costs nothing and needs no special case.
        JsonElement site = shape == SchemaShape.ArrayOfObjects ? property.Schema.GetProperty("items") : property.Schema;
        JsonElement origin = Spec.Navigate(Spec.SuccessSchema(spec.Operation(target.SchemaOperationId)), target.SchemaPointer);
        List<string> differences = Spec.StructuralDifferences(origin, site);

        if (differences.Count > 0)
        {
            throw new InvalidOperationException(
                $"Model '{owner.Name}' (operation '{owner.SchemaOperationId}'): property '{property.Name}' names model "
                + $"'{target.Name}', generated from operation '{target.SchemaOperationId}' at '{target.SchemaPointer}', "
                + "but the schema at this site differs:\n  "
                + string.Join("\n  ", differences)
                + "\nA model name may cover only one shape. Declare a second model for this site, or fix the row.");
        }
```

- [ ] **Step 5: Run the generator tests**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests`
Expected: PASS, all tests.

- [ ] **Step 6: Regenerate and run everything**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/ && dotnet build MassiveDotNet.slnx && dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: no diff under `src/`; 0 warnings; every test passes.

- [ ] **Step 7: Commit**

```bash
git add tools/MassiveDotNet.CodeGen tests/MassiveDotNet.CodeGen.Tests
git commit -m "feat: verify every model reuse site against the schema the model came from

Names must match exactly, a property the model requires must be
required at the site, and both checks recurse through nested objects
and arrays of objects. Scalar types are not compared; the model row is
the curated truth. A mismatch fails generation listing every difference
with its path.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 6: Map `ListNews` into a new `Reference` group

**Files:**
- Modify: `specs/endpoints.map.json`
- Create: `src/MassiveDotNet.Rest/ReferenceGroup.cs`
- Modify: `src/MassiveDotNet.Rest/MassiveRestClient.cs`
- Regenerate: `src/MassiveDotNet.Rest/Generated/` (adds `Models/NewsArticle.g.cs`, `Models/NewsPublisher.g.cs`, `Models/NewsInsight.g.cs`, `ReferenceGroup.g.cs`; extends `Envelopes.g.cs` and `MassiveRestJsonContext.g.cs`)
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceNewsTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1 to 5.
- Produces: `MassiveRestClient.Reference` returning `ReferenceGroup`; `ReferenceGroup.ListNewsAsync(RangeFilter<string>? ticker = null, RangeFilter<LocalDate>? publishedUtc = null, SortOrder? order = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default) → Task<MassivePage<NewsArticle>>`; `ReferenceGroup.EnumerateNewsAsync(...)` with the same parameters returning `IAsyncEnumerable<NewsArticle>`; models `NewsArticle`, `NewsPublisher`, `NewsInsight` in `MassiveDotNet.Rest.Models`. Task 7 calls these.

- [ ] **Step 1: Add the fixtures**

Append to the `Fixtures` class in `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, before the `Unauthorized` member:

```csharp
    /// <summary>
    /// The documented sample for GET /v2/reference/news, verbatim. Its <c>next_url</c> names the
    /// origin with an explicit <c>:443</c>, which the same-origin check (D14) must treat as the
    /// configured base address.
    /// </summary>
    public const string ReferenceNews = """
        {
          "count": 1,
          "next_url": "https://api.massive.com:443/v2/reference/news?cursor=eyJsaW1pdCI6MSwic29ydCI6InB1Ymxpc2hlZF91dGMiLCJvcmRlciI6ImFzY2VuZGluZyIsInRpY2tlciI6e30sInB1Ymxpc2hlZF91dGMiOnsiZ3RlIjoiMjAyMS0wNC0yNiJ9LCJzZWFyY2hfYWZ0ZXIiOlsxNjE5NDA0Mzk3MDAwLG51bGxdfQ",
          "request_id": "831afdb0b8078549fed053476984947a",
          "results": [
            {
              "amp_url": "https://m.uk.investing.com/news/stock-market-news/markets-are-underestimating-fed-cuts-ubs-3559968?ampMode=1",
              "article_url": "https://uk.investing.com/news/stock-market-news/markets-are-underestimating-fed-cuts-ubs-3559968",
              "author": "Sam Boughedda",
              "description": "UBS analysts warn that markets are underestimating the extent of future interest rate cuts by the Federal Reserve, as the weakening economy is likely to justify more cuts than currently anticipated.",
              "id": "8ec638777ca03b553ae516761c2a22ba2fdd2f37befae3ab6fdab74e9e5193eb",
              "image_url": "https://i-invdn-com.investing.com/news/LYNXNPEC4I0AL_L.jpg",
              "insights": [
                {
                  "sentiment": "positive",
                  "sentiment_reasoning": "UBS analysts are providing a bullish outlook on the extent of future Federal Reserve rate cuts, suggesting that markets are underestimating the number of cuts that will occur.",
                  "ticker": "UBS"
                }
              ],
              "keywords": [
                "Federal Reserve",
                "interest rates",
                "economic data"
              ],
              "published_utc": "2024-06-24T18:33:53Z",
              "publisher": {
                "favicon_url": "https://s3.massive.com/public/assets/news/favicons/investing.ico",
                "homepage_url": "https://www.investing.com/",
                "logo_url": "https://s3.massive.com/public/assets/news/logos/investing.png",
                "name": "Investing.com"
              },
              "tickers": [
                "UBS"
              ],
              "title": "Markets are underestimating Fed cuts: UBS By Investing.com - Investing.com UK"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// A hand-written final page in the news envelope's shape, with no <c>next_url</c>, so a
    /// traversal that starts from <see cref="ReferenceNews"/> ends after two requests. The service
    /// cannot be asked for "the page after the published sample", which is why this is written
    /// rather than captured.
    /// </summary>
    public const string ReferenceNewsLastPage = """
        {
          "count": 1,
          "request_id": "0d5b6f1e9f3c4a7b8e2d1c0f9a8b7c6d",
          "results": [
            {
              "article_url": "https://example.com/second",
              "author": "Second Author",
              "id": "second",
              "published_utc": "2024-06-25T09:00:00Z",
              "publisher": {
                "homepage_url": "https://example.com/",
                "logo_url": "https://example.com/logo.png",
                "name": "Example News"
              },
              "tickers": ["UBS"],
              "title": "Second article"
            }
          ],
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing endpoint tests**

Create `tests/MassiveDotNet.Rest.Tests/ReferenceNewsTests.cs`:

```csharp
using System.Text.Json;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The first mapped operation with nested objects: a required <c>publisher</c>, an optional array
/// of <c>insights</c>, arrays of strings, and an RFC 3339 timestamp, end to end through the stub.
/// </summary>
public sealed class ReferenceNewsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Instant Published = Instant.FromUtc(2024, 6, 24, 18, 33, 53);

    /// <summary>An article carrying only the schema-required scalars: no publisher at all.</summary>
    private const string ArticleWithoutPublisher = """
        {
          "results": [
            {
              "id": "1",
              "title": "Title",
              "author": "Author",
              "article_url": "https://example.com/a",
              "published_utc": "2024-06-24T18:33:53Z",
              "tickers": ["UBS"]
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>An article with its required publisher and nothing optional.</summary>
    private const string ArticleWithoutInsights = """
        {
          "results": [
            {
              "id": "1",
              "title": "Title",
              "author": "Author",
              "article_url": "https://example.com/a",
              "published_utc": "2024-06-24T18:33:53Z",
              "tickers": ["UBS"],
              "publisher": { "name": "Example", "homepage_url": "https://example.com/", "logo_url": "https://example.com/l.png" }
            }
          ],
          "status": "OK"
        }
        """;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestPathWithNoFilters()
    {
        StubHandler handler = new(Fixtures.ReferenceNews);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListNewsAsync(cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v2/reference/news", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task RendersATickerAndADateWindowInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceNews);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListNewsAsync(
                ticker: "AAPL",
                publishedUtc: RangeFilter.Between(new LocalDate(2024, 6, 1), new LocalDate(2024, 6, 30)),
                order: SortOrder.Descending,
                limit: 10,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v2/reference/news"
                + "?ticker=AAPL"
                + "&published_utc.gte=2024-06-01&published_utc.lte=2024-06-30"
                + "&order=desc"
                + "&limit=10",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceNews);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<NewsArticle> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListNewsAsync(ticker: "UBS", cancellationToken: Ct);
        }

        NewsArticle article = Assert.Single(page.Results);

        Assert.Equal("8ec638777ca03b553ae516761c2a22ba2fdd2f37befae3ab6fdab74e9e5193eb", article.Id);
        Assert.Equal("Markets are underestimating Fed cuts: UBS By Investing.com - Investing.com UK", article.Title);
        Assert.Equal("Sam Boughedda", article.Author);
        Assert.Equal("https://uk.investing.com/news/stock-market-news/markets-are-underestimating-fed-cuts-ubs-3559968", article.ArticleUrl);
        Assert.Equal("https://m.uk.investing.com/news/stock-market-news/markets-are-underestimating-fed-cuts-ubs-3559968?ampMode=1", article.AmpUrl);
        Assert.Equal("https://i-invdn-com.investing.com/news/LYNXNPEC4I0AL_L.jpg", article.ImageUrl);
        Assert.StartsWith("UBS analysts warn", article.Description, StringComparison.Ordinal);
        Assert.Equal(Published, article.PublishedUtc);
        Assert.Equal(["UBS"], article.Tickers);
        Assert.Equal(["Federal Reserve", "interest rates", "economic data"], article.Keywords);

        Assert.Equal("Investing.com", article.Publisher.Name);
        Assert.Equal("https://www.investing.com/", article.Publisher.HomepageUrl);
        Assert.Equal("https://s3.massive.com/public/assets/news/logos/investing.png", article.Publisher.LogoUrl);
        Assert.Equal("https://s3.massive.com/public/assets/news/favicons/investing.ico", article.Publisher.FaviconUrl);

        NewsInsight insight = Assert.Single(article.Insights!);
        Assert.Equal("UBS", insight.Ticker);
        Assert.Equal("positive", insight.Sentiment);
        Assert.StartsWith("UBS analysts are providing a bullish outlook", insight.SentimentReasoning, StringComparison.Ordinal);

        Assert.True(page.HasMore);
        Assert.Equal("831afdb0b8078549fed053476984947a", page.RequestId);
    }

    [Fact]
    public async Task AnArticleWithoutAPublisherIsRejected()
    {
        // publisher is required by the schema, so NewsArticle.Publisher carries the required
        // modifier (D-F10 through D-N2). Its absence is a JsonException wrapped in a
        // MassiveApiException, not a null reference discovered later by the caller.
        StubHandler handler = new(ArticleWithoutPublisher);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Reference.ListNewsAsync(cancellationToken: Ct));

            Assert.IsType<JsonException>(exception.InnerException);
        }
    }

    [Fact]
    public async Task OptionalNestedMembersAreNullWhenAbsent()
    {
        StubHandler handler = new(ArticleWithoutInsights);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<NewsArticle> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListNewsAsync(cancellationToken: Ct);
        }

        NewsArticle article = Assert.Single(page.Results);
        Assert.Null(article.Insights);
        Assert.Null(article.Keywords);
        Assert.Null(article.Publisher.FaviconUrl);
        Assert.Equal("Example", article.Publisher.Name);
    }

    [Fact]
    public async Task AMalformedTimestampIsRejected()
    {
        string body = Fixtures.ReferenceNews.Replace("2024-06-24T18:33:53Z", "2024-06-24 18:33:53", StringComparison.Ordinal);
        StubHandler handler = new(body);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Reference.ListNewsAsync(cancellationToken: Ct));

            Assert.IsType<JsonException>(exception.InnerException);
        }
    }

    [Fact]
    public async Task EnumerateFollowsThePublishedCursorVerbatim()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceNews, Fixtures.ReferenceNewsLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string> ids = [];

        using (client)
        using (transport)
        {
            await foreach (NewsArticle article in client.Reference.EnumerateNewsAsync(ticker: "UBS", cancellationToken: Ct))
            {
                ids.Add(article.Id);
            }
        }

        Assert.Equal(["8ec638777ca03b553ae516761c2a22ba2fdd2f37befae3ab6fdab74e9e5193eb", "second"], ids);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/v2/reference/news", handler.Requests[1].AbsolutePath);
        Assert.StartsWith("?cursor=eyJsaW1pdCI6MSw", handler.Requests[1].Query, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ReferenceNewsTests"`
Expected: build FAILS with `CS1061: 'MassiveRestClient' does not contain a definition for 'Reference'` and `CS0246` for `NewsArticle`.

- [ ] **Step 4: Add the hand-written group and the client property**

Create `src/MassiveDotNet.Rest/ReferenceGroup.cs`:

```csharp
using MassiveDotNet.Http;

namespace MassiveDotNet.Rest;

/// <summary>
/// Reference data across asset classes: tickers, news, corporate actions, exchanges, and
/// conditions. Reached through <see cref="MassiveRestClient.Reference"/>.
/// </summary>
/// <remarks>
/// This is a <see langword="struct"/> wrapping the shared transport, so navigating to a group
/// costs no allocation. Endpoint methods live in the generated half of this partial type.
/// </remarks>
public readonly partial struct ReferenceGroup
{
    private readonly MassiveHttpTransport _transport;

    internal ReferenceGroup(MassiveHttpTransport transport) => _transport = transport;
}
```

In `src/MassiveDotNet.Rest/MassiveRestClient.cs`, after the `Stocks` property:

```csharp
    /// <summary>Reference data: tickers, news, corporate actions, exchanges, and conditions.</summary>
    public ReferenceGroup Reference => new(_transport);
```

and extend the class summary's example: `for example <see cref="Stocks"/> and <see cref="Reference"/>.`

- [ ] **Step 5: Add the group, the models, and the endpoint to the map**

In `specs/endpoints.map.json`, add to `groups` after `Stocks`:

```json
    "Reference": {
      "summary": "Reference data across asset classes: tickers, news, corporate actions, exchanges, and conditions."
    }
```

Add to `models` after `Dividend`:

```json
    "NewsArticle": {
      "summary": "A news article associated with one or more tickers, with its publisher and per-ticker sentiment insights.",
      "remarks": "Reference data, so a class rather than a struct (decision D4). <see cref=\"PublishedUtc\"/> is a moment on the global timeline, hence <see cref=\"NodaTime.Instant\"/>; the nested <see cref=\"Publisher\"/> and <see cref=\"Insights\"/> are models of their own (decision D16).",
      "schema": { "operationId": "ListNews", "pointer": "results/items" },
      "properties": {
        "id":            { "name": "Id" },
        "title":         { "name": "Title" },
        "author":        { "name": "Author" },
        "published_utc": { "name": "PublishedUtc" },
        "article_url":   { "name": "ArticleUrl" },
        "amp_url":       { "name": "AmpUrl" },
        "image_url":     { "name": "ImageUrl" },
        "description":   { "name": "Description" },
        "tickers":       { "name": "Tickers" },
        "keywords":      { "name": "Keywords" },
        "publisher":     { "name": "Publisher", "model": "NewsPublisher" },
        "insights":      { "name": "Insights",  "model": "NewsInsight" }
      }
    },

    "NewsPublisher": {
      "summary": "The outlet that published a news article: its name, homepage, and branding assets.",
      "schema": { "operationId": "ListNews", "pointer": "results/items/publisher" },
      "properties": {
        "name":         { "name": "Name" },
        "homepage_url": { "name": "HomepageUrl" },
        "logo_url":     { "name": "LogoUrl" },
        "favicon_url":  { "name": "FaviconUrl" }
      }
    },

    "NewsInsight": {
      "summary": "Sentiment attributed to one ticker mentioned in a news article, with the reasoning behind it.",
      "schema": { "operationId": "ListNews", "pointer": "results/items/insights/items" },
      "properties": {
        "ticker":              { "name": "Ticker" },
        "sentiment":           { "name": "Sentiment" },
        "sentiment_reasoning": { "name": "SentimentReasoning" }
      }
    }
```

Add to `endpoints` after `get_stocks_v1_dividends`:

```json
    {
      "operationId": "ListNews",
      "group": "Reference",
      "method": "ListNews",
      "summary": "Retrieves recent news articles associated with a ticker, with each article's publisher and per-ticker sentiment insights.",
      "remarks": "Filter by <paramref name=\"ticker\"/> for one symbol or a lexical range, and by <paramref name=\"publishedUtc\"/> for a calendar-date window. Results sort by <c>published_utc</c> only; set <paramref name=\"order\"/> to choose the direction.",
      "result": { "kind": "array", "model": "NewsArticle", "property": "results" },
      "parameters": {
        "ticker":        { "name": "ticker" },
        "published_utc": { "name": "publishedUtc", "type": "LocalDate" },
        "order":         { "name": "order", "type": "SortOrder" },
        "limit":         { "name": "limit" },
        "sort":          { "name": "sort" }
      }
    }
```

Also update the map's leading `"//"` comment array: change `"  * property names for anonymous result schemas (no operation uses $ref)"` to `"  * property names for anonymous result schemas, and a name for every nested object"`.

- [ ] **Step 6: Regenerate and inspect**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git status --short src/`
Expected: new files `Generated/Models/NewsArticle.g.cs`, `Generated/Models/NewsPublisher.g.cs`, `Generated/Models/NewsInsight.g.cs`, `Generated/ReferenceGroup.g.cs`; modified `Envelopes.g.cs` (adds `ListNewsResponse : IPagedEnvelope<NewsArticle>`) and `MassiveRestJsonContext.g.cs` (adds `[JsonSerializable(typeof(ListNewsResponse))]`). The generator prints `coverage 3/147`.

Confirm in `NewsArticle.g.cs`:

```csharp
    [JsonPropertyName("published_utc")]
    public Instant PublishedUtc { get; init; }
    ...
    [JsonPropertyName("tickers")]
    public required string[] Tickers { get; init; }
    ...
    [JsonPropertyName("publisher")]
    public required NewsPublisher Publisher { get; init; }

    [JsonPropertyName("insights")]
    public NewsInsight[]? Insights { get; init; }
```

and in `ReferenceGroup.g.cs`:

```csharp
    public Task<MassivePage<NewsArticle>> ListNewsAsync(
        RangeFilter<string>? ticker = null,
        RangeFilter<LocalDate>? publishedUtc = null,
        SortOrder? order = null,
        int? limit = null,
        string? sort = null,
        CancellationToken cancellationToken = default)
```

- [ ] **Step 7: Raise the coverage baseline**

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`:

```csharp
    private const int CoverageBaseline = 3;
```

- [ ] **Step 8: Run the tests**

Run: `dotnet build MassiveDotNet.slnx && dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: 0 warnings; every test passes, including the seven in `ReferenceNewsTests`. If `EnumerateFollowsThePublishedCursorVerbatim` fails on the origin check, the failure names `https://api.massive.com:443`; that would contradict the verified behaviour of `Uri.Compare` with `UriComponents.SchemeAndServer`, so report it rather than editing the fixture.

- [ ] **Step 9: Confirm idempotency**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: clean.

- [ ] **Step 10: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest tests/MassiveDotNet.Rest.Tests
git commit -m "feat: map the news endpoint into a Reference group with nested models

ListNews is the first operation with nested objects: a required
publisher, an optional array of insights, arrays of strings, and an RFC
3339 timestamp. NewsArticle, NewsPublisher, and NewsInsight are named in
the map and generated from the spec. The published sample is the
fixture; its next_url names the origin with an explicit :443, which the
same-origin check accepts. Coverage baseline rises to 3.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 7: AOT smoke, live test, and CLAUDE.md

**Files:**
- Modify: `samples/MassiveDotNet.AotSmokeTest/Program.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/ReferenceNewsLiveTests.cs`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: `MassiveRestClient.Reference`, `ReferenceGroup.ListNewsAsync`, `NewsArticle`, `NewsPublisher`, `NewsInsight` (Task 6).
- Produces: nothing new.

- [ ] **Step 1: Root the nested graph in the AOT sample**

In `samples/MassiveDotNet.AotSmokeTest/Program.cs`, after the dividends assertions and before `Console.WriteLine($"\nrequests: {handler.Requests}");`, add:

```csharp
// Nested models and the RFC 3339 converter are reachable only through the news envelope, so a
// clean publish says nothing about them unless something here deserializes one. This is the
// first call whose result carries a required nested object, an array of nested objects, and an
// Instant parsed from a string rather than an epoch number.
Console.WriteLine("\nnews, with a nested publisher and insights:");

MassivePage<NewsArticle> news = await client.Reference.ListNewsAsync(
    ticker: "UBS",
    publishedUtc: RangeFilter.Gte(new LocalDate(2024, 6, 1)),
    limit: 1);

Console.WriteLine($"request : {handler.LastRequestUri}");

foreach (NewsArticle item in news.Results)
{
    Console.WriteLine($"  {InstantPattern.ExtendedIso.Format(item.PublishedUtc)}  {item.Publisher.Name}  {item.Title}");
}

const string ExpectedNewsQuery = "?ticker=UBS&published_utc.gte=2024-06-01&limit=1";

if (handler.LastRequestUri?.Query != ExpectedNewsQuery)
{
    Console.Error.WriteLine($"FAIL: expected the news query {ExpectedNewsQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (news.Results is not [NewsArticle article]
    || article.Publisher.Name != "Investing.com"
    || article.PublishedUtc != Instant.FromUtc(2024, 6, 24, 18, 33, 53)
    || article.Insights is not [{ Ticker: "UBS", Sentiment: "positive" }]
    || article.Keywords is not { Length: 3 })
{
    Console.Error.WriteLine("FAIL: expected one article from Investing.com published 2024-06-24T18:33:53Z with one positive UBS insight and three keywords.");
    return 1;
}
```

Change the final request-count check and its comment:

```csharp
// Two pages of the enumeration, the single-page aggregates call, the dividends call, and the
// news call.
if (enumerated != 3 || handler.Requests != 5)
{
    Console.Error.WriteLine(
        $"FAIL: expected 3 enumerated bars over 5 requests; got {enumerated} over {handler.Requests}.");
    return 1;
}
```

In the `StubHandler` class, add a `News` constant after `Dividends`, holding the published sample verbatim:

```csharp
    private const string News = """
        {
          "count": 1,
          "next_url": "https://api.massive.com:443/v2/reference/news?cursor=eyJsaW1pdCI6MSwic29ydCI6InB1Ymxpc2hlZF91dGMiLCJvcmRlciI6ImFzY2VuZGluZyIsInRpY2tlciI6e30sInB1Ymxpc2hlZF91dGMiOnsiZ3RlIjoiMjAyMS0wNC0yNiJ9LCJzZWFyY2hfYWZ0ZXIiOlsxNjE5NDA0Mzk3MDAwLG51bGxdfQ",
          "request_id": "831afdb0b8078549fed053476984947a",
          "results": [
            {
              "amp_url": "https://m.uk.investing.com/news/stock-market-news/markets-are-underestimating-fed-cuts-ubs-3559968?ampMode=1",
              "article_url": "https://uk.investing.com/news/stock-market-news/markets-are-underestimating-fed-cuts-ubs-3559968",
              "author": "Sam Boughedda",
              "description": "UBS analysts warn that markets are underestimating the extent of future interest rate cuts by the Federal Reserve, as the weakening economy is likely to justify more cuts than currently anticipated.",
              "id": "8ec638777ca03b553ae516761c2a22ba2fdd2f37befae3ab6fdab74e9e5193eb",
              "image_url": "https://i-invdn-com.investing.com/news/LYNXNPEC4I0AL_L.jpg",
              "insights": [
                {
                  "sentiment": "positive",
                  "sentiment_reasoning": "UBS analysts are providing a bullish outlook on the extent of future Federal Reserve rate cuts, suggesting that markets are underestimating the number of cuts that will occur.",
                  "ticker": "UBS"
                }
              ],
              "keywords": [
                "Federal Reserve",
                "interest rates",
                "economic data"
              ],
              "published_utc": "2024-06-24T18:33:53Z",
              "publisher": {
                "favicon_url": "https://s3.massive.com/public/assets/news/favicons/investing.ico",
                "homepage_url": "https://www.investing.com/",
                "logo_url": "https://s3.massive.com/public/assets/news/logos/investing.png",
                "name": "Investing.com"
              },
              "tickers": [
                "UBS"
              ],
              "title": "Markets are underestimating Fed cuts: UBS By Investing.com - Investing.com UK"
            }
          ],
          "status": "OK"
        }
        """;
```

and route it:

```csharp
        if (request.RequestUri?.AbsolutePath == "/stocks/v1/dividends")
        {
            body = Dividends;
        }
        else if (request.RequestUri?.AbsolutePath == "/v2/reference/news")
        {
            body = News;
        }
        else
        {
```

- [ ] **Step 2: Run the sample under the ordinary runtime, then publish it**

Run: `dotnet run --project samples/MassiveDotNet.AotSmokeTest`
Expected: prints the news line `2024-06-24T18:33:53Z  Investing.com  Markets are underestimating Fed cuts: ...` and ends with `AOT smoke test passed.`

Run: `dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release 2>&1 | tee aot.log; grep -E ': (warning|error) (IL|AOT|Trim)?[0-9]{4}' aot.log; echo "exit=$?"` (use `linux-x64` on Linux)
Expected: the grep prints nothing and reports `exit=1` (no match). Then run the published binary, `samples/MassiveDotNet.AotSmokeTest/bin/Release/net10.0/osx-arm64/publish/MassiveDotNet.AotSmokeTest`, and confirm it ends with `AOT smoke test passed.` Delete `aot.log` afterwards.

- [ ] **Step 3: Write the live test**

Create `tests/MassiveDotNet.IntegrationTests/ReferenceNewsLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Exercises the first nested-object endpoint against the live service.
/// </summary>
/// <remarks>
/// The fixture proves the SDK reads the published sample. This is what proves the service still
/// sends a publisher on every article, that its timestamps parse, and that the calendar-date form
/// of <c>published_utc</c> is accepted as a filter, none of which the OpenAPI description states.
/// </remarks>
public sealed class ReferenceNewsLiveTests : LiveApiTest
{
    // A fixed historical window, so the assertions do not depend on when the suite is run.
    private static readonly LocalDate WindowStart = new(2024, 6, 1);
    private static readonly LocalDate WindowEnd = new(2024, 6, 30);

    [Fact]
    public async Task ReturnsArticlesWithPublishersInsideADateWindow()
    {
        NewsArticle[] articles = (await Client.Reference.ListNewsAsync(
            ticker: "AAPL",
            publishedUtc: RangeFilter.Between(WindowStart, WindowEnd),
            limit: 5,
            cancellationToken: Ct)).Results;

        Assert.NotEmpty(articles);

        Instant lower = WindowStart.AtMidnight().InUtc().ToInstant();
        Instant upper = WindowEnd.PlusDays(1).AtMidnight().InUtc().ToInstant();

        foreach (NewsArticle article in articles)
        {
            Assert.False(string.IsNullOrWhiteSpace(article.Publisher.Name), "Every article names its publisher.");
            Assert.Contains("AAPL", article.Tickers);
            Assert.InRange(article.PublishedUtc, lower, upper);
        }
    }
}
```

- [ ] **Step 4: Run the live test locally**

Run: `set -a; source .env; set +a; dotnet test tests/MassiveDotNet.IntegrationTests --filter "FullyQualifiedName~ReferenceNewsLiveTests"`
Expected: PASS. Do not print the key, and do not add it to any file other than the gitignored `.env`. If the test fails because the service rejects the date form of `published_utc`, report the response body's error text; that is a finding for the spec's D-N7, not something to patch here.

- [ ] **Step 5: Update CLAUDE.md**

In the **Architecture decisions** table, add after the D15 row:

```markdown
| D16 | Nested object schemas bind to **named models in the map**, generated from the spec and verified structurally at every site that names them. An object with no binding fails generation. | 184 nested sites across 55 operations collapse to 60 shapes, and their public names (`Greeks`, `NewsPublisher`) are worth a human's row in the map: path-derived names would give the three stocks snapshot operations three identical `Day` types. A name may cover only one shape, so every reuse site is checked — property names must match exactly and the model may not require what the site makes optional — while scalar types are trusted from the model row, because the description's own formats disagree at sites that are plainly the same thing. Failing on an unbound object is what stops a required nested object from shipping as a `string` that throws at deserialization. |
```

In **Layout**, change the `tests/` line to:

```
tests/                      Unit tests, the endpoint-coverage contract tests, and the generator's diagnostic tests.
```

In **Adding endpoints**, extend step 1 with one sentence after "property names for anonymous result schemas.":

```markdown
   A nested object, or the element of a nested array of objects, needs its own `models` row with a
   pointer through its parent and a `model` reference on the parent's property row (D16).
```

In **Temporal types › Vocabulary**, change the `Instant` row's example to:

```markdown
| A moment on the global timeline | `Instant` | `Agg.Timestamp`, trade and quote SIP timestamps, `NewsArticle.PublishedUtc` |
```

In **Testing**, add a row to the tier table after the Offline row:

```markdown
| Offline | `MassiveDotNet.CodeGen.Tests` | yes | no |
```

In **Conventions**, add after the **Filters** bullet:

```markdown
- **Models**: a nested object, or the element of a nested array of objects, is its own `models`
  row with a pointer through its parent (`results/items/publisher`,
  `results/items/insights/items`), and the parent's property row names it with `model`; the
  generator composes `T`, `T?`, `T[]`, or `T[]?` from the spec, so the map never restates
  requiredness or array-ness. A free-form object with no declared properties takes an explicit
  `type` of `Dictionary<string, T>`. `format: date-time` properties are `Instant`, read by
  `InstantJsonConverter`. Names are domain nouns, prefixed by family only where it
  disambiguates; reuse a model across operations only where the generator's structural check
  passes (D16).
```

- [ ] **Step 6: Run the full verification set**

```bash
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/
```

Expected: 0 warnings; every offline test passes across all three test projects; no diff.

- [ ] **Step 7: Commit**

```bash
git add samples/MassiveDotNet.AotSmokeTest/Program.cs tests/MassiveDotNet.IntegrationTests/ReferenceNewsLiveTests.cs CLAUDE.md
git commit -m "feat: root nested models in the AOT smoke test and record decision D16

The sample now deserializes the news envelope, so the publish proves
the nested model graph and the RFC 3339 converter are AOT clean. One
live test confirms the service sends a publisher on every article and
accepts the calendar-date filter form. CLAUDE.md records D16, the
Models convention, and the generator test tier.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```
