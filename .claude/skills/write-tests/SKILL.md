---
name: write-tests
description: Write unit tests for a Command or Query handler following the WKFLO test pattern. Use when adding or modifying application handlers.
---

# Write Tests

Write unit tests for a handler following the WKFLO test pattern. The user will indicate which handler to test.

## Test Structure

Every handler test consists of exactly **2 files** placed in the mirror path under `tests/`:

```
[Service].UnitTests/
  Application/
    [Area]/
      Commands/[FeatureName]/
        [FeatureName]CommandHandlerTests.cs     ← test cases
        [FeatureName]CommandHandlerTesting.cs   ← fixture/setup
      Queries/[FeatureName]/
        [FeatureName]QueryHandlerTests.cs
        [FeatureName]QueryHandlerTesting.cs
```

## File 1: `*Testing.cs` — Fixture

Sets up all mocks and instantiates the handler under test. No tests here.

```csharp
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.[Area].Commands.[FeatureName];
using AutoMapper;
using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.[Area].Commands.[FeatureName]
{
    public class [FeatureName]CommandHandlerTesting
    {
        public [FeatureName]CommandHandler [FeatureName]CommandHandler { get; set; }

        public [FeatureName]CommandHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant()
            {
                Id = 1,
                Name = tenantContext.TenantName
            };

            var configuration = new MapperConfiguration(cfg =>
            {
                cfg.LicenseKey = Environment.GetEnvironmentVariable("LuckyPennySoftwareLicense");
                cfg.AddProfile<MappingProfile>();
            }, new LoggerFactory());
            var mapper = configuration.CreateMapper();

            // Setup in-memory mock DbSets using MockQueryable.Moq
            var entities = new List<[Entity]>() { }.BuildMockDbSet();
            var tenants = new List<Tenant>() { tenant }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.[DbSet]).Returns(entities.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            [FeatureName]CommandHandler = new [FeatureName]CommandHandler
            (
                db.Object,
                mapper,
                tenantContext
            );
        }
    }
}
```

## File 2: `*Tests.cs` — Test cases

Uses `IClassFixture<*Testing>` to receive the fixture. Test cases use `[Theory]` + `[InlineData]`.

```csharp
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.[Area].Commands.[FeatureName];
using AMAIS.WKFLO.MD.Application.[Area].Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.[Area].Commands.[FeatureName]
{
    public class [FeatureName]CommandHandlerTests : IClassFixture<[FeatureName]CommandHandlerTesting>
    {
        private readonly [FeatureName]CommandHandlerTesting _testing;

        public [FeatureName]CommandHandlerTests([FeatureName]CommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("Value1", "Value2", ResultType.Ok)]
        [InlineData("Value3", "Value4", ResultType.Ok)]
        public async Task TestHandler(string param1, string param2, ResultType resultType)
        {
            // Act
            var result = await _testing.[FeatureName]CommandHandler.Handle(new [FeatureName]Command()
            {
                Property1 = param1,
                Property2 = param2
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<[FeatureName]Result> success)
            {
                Assert.NotNull(success.Data);
            }
        }
    }
}
```

## Rules

- **Always 2 files**: `*Testing.cs` (fixture) + `*Tests.cs` (test cases). Never merge them.
- Use `IClassFixture<*Testing>` — not `new` in the test constructor.
- Use `TestContext.Current.CancellationToken` — not `CancellationToken.None` or `default`.
- Use `[Theory]` + `[InlineData]` for parameterized cases. Minimum 2 `[InlineData]` rows.
- Always test at least the happy path (`ResultType.Ok`).
- Mock DbSets with `BuildMockDbSet()` from `MockQueryable.Moq`.
- The `MapperConfiguration` always needs `cfg.LicenseKey = Environment.GetEnvironmentVariable("LuckyPennySoftwareLicense")`.
- Never mock `IMapper` directly — always instantiate `AutoMapper` with `MappingProfile`.
- Use `db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1))` for write operations.
- Comment sections as `// Act` and `// Assert`. No `// Arrange` section — setup belongs in the Testing fixture.
- Do not add extra test scenarios beyond what the handler logic requires. No over-testing.
