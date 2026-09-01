## Summary

<!-- What changed and why. -->

## Checklist

- [ ] `dotnet build MassiveDotNet.slnx` is warning-free
- [ ] `dotnet test MassiveDotNet.slnx` passes
- [ ] Generated code is current (`dotnet run --project tools/MassiveDotNet.CodeGen` leaves no diff)
- [ ] Native AOT publish emits no IL warnings
- [ ] New endpoints raise `CoverageBaseline` and have a deserialization test

<!-- If this changes an architectural decision recorded in CLAUDE.md, say which and why. -->
