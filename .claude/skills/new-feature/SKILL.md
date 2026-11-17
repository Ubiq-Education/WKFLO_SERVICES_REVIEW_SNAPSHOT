---
name: new-feature
description: Scaffold a complete feature (Command or Query) following the WKFLO Clean Architecture pattern. Use when creating new application functionality end-to-end.
---

# New Feature Scaffold

Create a complete feature following the WKFLO architecture patterns. A feature is either a **Command** (write operation) or a **Query** (read operation).

The user will provide: feature name, target service (MD or AI), area/entity (e.g. Agents, Tools), and whether it is a Command or Query.

## Architecture Layers

```
[Service].Application/
  [Area]/
    Commands/[FeatureName]/
      [FeatureName]Command.cs
      [FeatureName]CommandHandler.cs
    Queries/[FeatureName]/
      [FeatureName]Query.cs
      [FeatureName]QueryHandler.cs
    Models/
      [FeatureName]Result.cs

[Service].WebAPI/
  EndPoints/[Scope]/[Area]/
    [FeatureName].cs
```

## 1. Command (write operation)

### `[FeatureName]Command.cs`
```csharp
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AutoMapper;
using MediatR;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.[Area].Commands.[FeatureName]
{
    public class [FeatureName]Command : IRequest<Result>, IMap
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(300, ErrorMessage = ErrorConst.MaximumLength)]
        public string ExampleProperty { get; set; }

        public void Mapping(Profile profile)
        {
            profile.CreateMap<[FeatureName]Command, [Entity]>();
        }
    }
}
```

### `[FeatureName]CommandHandler.cs`
```csharp
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.[Area].Models;
using AutoMapper;
using MediatR;

namespace AMAIS.WKFLO.MD.Application.[Area].Commands.[FeatureName]
{
    public class [FeatureName]CommandHandler : IRequestHandler<[FeatureName]Command, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IMapper _mapper;
        private readonly TenantContext _tenantContext;

        public [FeatureName]CommandHandler(IMasterDataDbContext db, IMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async Task<Result> Handle([FeatureName]Command command, CancellationToken cancellationToken)
        {
            var entity = _mapper.Map<[Entity]>(command);

            await _db.[DbSet].AddAsync(entity, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<[FeatureName]Result>(_mapper.Map<[FeatureName]Result>(entity));
        }
    }
}
```

## 2. Query (read operation)

### `[FeatureName]Query.cs`
```csharp
using AMAIS.WKFLO.Common.Models.Results;
using MediatR;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.[Area].Queries.[FeatureName]
{
    public class [FeatureName]Query : IRequest<Result>
    {
        [Required]
        public int Id { get; set; }
    }
}
```

### `[FeatureName]QueryHandler.cs`
```csharp
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.[Area].Models;
using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.[Area].Queries.[FeatureName]
{
    public class [FeatureName]QueryHandler : IRequestHandler<[FeatureName]Query, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IMapper _mapper;
        private readonly TenantContext _tenantContext;

        public [FeatureName]QueryHandler(IMasterDataDbContext db, IMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async Task<Result> Handle([FeatureName]Query query, CancellationToken cancellationToken)
        {
            var entity = await _db.[DbSet]
                .AsNoTracking()
                .Where(x => x.Id == query.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (entity == null)
                return new NotFoundResult();

            return new SuccessResult<[FeatureName]Result>(_mapper.Map<[FeatureName]Result>(entity));
        }
    }
}
```

## 3. Result Model

### `[FeatureName]Result.cs`
Place in `Application/[Area]/Models/`:
```csharp
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AutoMapper;

namespace AMAIS.WKFLO.MD.Application.[Area].Models
{
    public class [FeatureName]Result : IMapFrom<[Entity]>
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public void Mapping(Profile profile)
        {
            profile.CreateMap<[Entity], [FeatureName]Result>();
        }
    }
}
```

## 4. Endpoint

Scope is one of: `Tenant`, `Marketplace`, `Public`. Choose based on who calls the endpoint.

### Command endpoint (POST/PUT/DELETE)
```csharp
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.[Area].Commands.[FeatureName];
using AMAIS.WKFLO.MD.Application.[Area].Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.[Scope].[Area]
{
    public class [FeatureName] : BaseAsyncEndpoint<[FeatureName]Command, Result>
    {
        public [FeatureName](IMediator mediator) : base(mediator) { }

        [Authorize]
        [HttpPost("/api/v1/[scope]/[resource]")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [ProducesResponseType(typeof(SuccessResult<[FeatureName]Result>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [SwaggerOperation(
            Summary = "Summary here",
            Description = "Description here",
            OperationId = "[Area].[Action]",
            Tags = new[] { "Main Area", "[Area]" })
        ]
        public override async Task<ActionResult<Result>> HandleAsync([FeatureName]Command command, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
```

### Endpoint with route param (e.g. GET/DELETE by id)
Use `BaseAsyncEndpoint<int, Result>` and add `[FromRoute] int id` parameter.

## Rules

- Always use `Result` return type (never throw exceptions for flow control)
- Use `SuccessResult<T>`, `NotFoundResult`, `InvalidResult`, `UnauthorizedResult` as appropriate
- Always inject `TenantContext` in handlers (multi-tenant system)
- Use `AsNoTracking()` on all queries
- Use `SaveChangesAsync(cancellationToken)` never `SaveChanges()`
- Validation via `DataAnnotations` on the Command/Query using `ErrorConst` constants
- Mapping defined via `IMap` / `IMapFrom<T>` interfaces directly on the model class, never separate mapping files
- MediatR is auto-registered, no manual handler registration needed
- After creating all files, remind the user to run tests with `/write-tests`
