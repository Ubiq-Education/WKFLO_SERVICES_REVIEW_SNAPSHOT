---
name: new-entity
description: Add a new domain entity to AMAIS.WKFLO.Common.MD.Domain following the WKFLO entity pattern. Use when a new concept needs to be persisted.
---

# New Entity

Add a new domain entity following the WKFLO pattern. The user will provide the entity name and its properties.

A new entity requires changes/additions in **5 places** in `AMAIS_WKFLO/AMAIS_WKFLO_COMMON/`:

```
src/AMAIS.WKFLO.Common.MD.Domain/
  Entities/
    [EntityName].cs                         ← new file
  Data/
    Config/
      [EntityName]Configuration.cs          ← new file
    MasterDataDbContext.cs                  ← add DbSet
  Entities/Interfaces/
    IMasterDataDbContext.cs                 ← add DbSet
src/AMAIS.WKFLO.Common.MD.DB/
  dbo/Tables/
    [EntityName].sql                        ← new file
  AMAIS.WKFLO.Common.MD.DB.sqlproj         ← register the sql file
```

## 1. Entity class

`Entities/[EntityName].cs`:
```csharp
using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class [EntityName] : Entity<int>
    {
        public string Name { get; set; }
        // Add navigation properties for FK relationships:
        // public int? RelatedEntityId { get; set; }
        // public RelatedEntity RelatedEntity { get; set; }
        // public virtual ICollection<OtherEntity> OtherEntities { get; set; }
    }
}
```

- Always extend `Entity<int>` (provides `Id` property)
- Use `int?` for optional FK IDs, `int` for required
- Navigation properties for relationships: both the FK int and the object reference

## 2. EF Core configuration

`Data/Config/[EntityName]Configuration.cs`:
```csharp
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class [EntityName]Configuration : IEntityTypeConfiguration<[EntityName]>
    {
        public void Configure(EntityTypeBuilder<[EntityName]> builder)
        {
            builder.ToTable("[EntityName]");
            builder.HasKey(m => m.Id).IsClustered(true);

            // String lengths
            builder.Property(e => e.Name).HasMaxLength(300);

            // Relationships (examples):
            // builder.HasOne(m => m.RelatedEntity).WithMany(m => m.EntityNames).HasForeignKey(m => m.RelatedEntityId);
        }
    }
}
```

- Always set `ToTable("[EntityName]")` explicitly (SQL Server table name)
- Always set clustered primary key: `HasKey(m => m.Id).IsClustered(true)`
- Define `HasMaxLength` for all string properties
- Define all relationships here, not on the entity class

## 3. Add DbSet to `IMasterDataDbContext`

In `Entities/Interfaces/IMasterDataDbContext.cs`, add the new DbSet property grouped with related entities:
```csharp
DbSet<[EntityName]> [EntityName]s { get; set; }
```

## 4. Add DbSet to `MasterDataDbContext`

In `Data/MasterDataDbContext.cs`, add the DbSet property:
```csharp
public DbSet<[EntityName]> [EntityName]s { get; set; }
```

The `OnModelCreating` method uses `ApplyConfigurationsFromAssembly`, so the configuration class is picked up automatically — no need to add it manually.

## 5. SQL table definition (SSDT)

The project uses **SSDT** (SQL Server Data Tools), not EF Core migrations. Every new entity needs a SQL file and must be registered in the project.

**`AMAIS.WKFLO.Common.MD.DB/dbo/Tables/[EntityName].sql`** (new file):
```sql
CREATE TABLE [dbo].[EntityName]
(
    [Id] INT NOT NULL PRIMARY KEY IDENTITY CLUSTERED,
    [Name] NVARCHAR(300) NULL,
    -- other columns...
    [IsDeleted] BIT NOT NULL DEFAULT 0,
    [CreatedOn] DATETIME NOT NULL DEFAULT GETUTCDATE(),
    [LastUpdatedOn] DATETIME NULL,
    CONSTRAINT [FK_EntityName_ToRelated] FOREIGN KEY ([RelatedId]) REFERENCES [Related]([Id])
)

GO

CREATE INDEX IX_EntityName_RelatedId ON [dbo].[EntityName] ([RelatedId]);
```

Then register it in **`AMAIS.WKFLO.Common.MD.DB.sqlproj`** inside the `<ItemGroup>` with the other `<Build>` entries:
```xml
<Build Include="dbo\Tables\[EntityName].sql" />
```

## Rules

- Never skip the EF Core configuration file — do not use DataAnnotations for DB mapping
- Always group related DbSets together in `IMasterDataDbContext` and `MasterDataDbContext`
- **Never suggest `dotnet ef migrations add`** — the project uses SSDT, not EF Core migrations
- Always create the `.sql` file AND register it in `.sqlproj` — both are required or the table won't be included in the SSDT build
- Do not add business logic to entities — they are plain data containers
- If the entity is multi-tenant (belongs to a Tenant), add `public int? TenantId { get; set; }` and `public Tenant Tenant { get; set; }` and configure the relationship in the configuration file
