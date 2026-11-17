/*
Pre-Deployment Script Template
--------------------------------------------------------------------------------------
 This file contains SQL statements that will be executed before the build script.
 Use SQLCMD syntax to include a file in the pre-deployment script.
 Example:      :r .\myfile.sql
 Use SQLCMD syntax to reference a variable in the pre-deployment script.
 Example:      :setvar TableName MyTable
               SELECT * FROM [$(TableName)]
--------------------------------------------------------------------------------------
*/

-- WI 7317 (2nd regression): backfill [Agent].[EnableSTM]/[EnableLTM]/[EnableSpinUpAgents]
-- BEFORE this deployment's schema sync tightens all three columns to NOT NULL (see Agent.sql).
--
-- This MUST run as a pre-deployment script, not a post-deployment one: SqlPackage/DacFx runs
-- pre-deployment scripts before the generated schema-sync statements and post-deployment scripts
-- after them. The generated ALTER COLUMN ... NOT NULL statements for these three columns would
-- fail outright if any row still has NULL in them at that point, so the backfill has to land
-- first.
--
-- Root cause: all three columns have always allowed NULL at the schema level (BIT NULL), but
-- every C# entity that maps a real value to them (RunnableAgent.EnableSTM/EnableLTM/
-- EnableSpinUpAgents, and - as of this feature - MarketplaceAgent.EnableSTM) declares them as
-- non-nullable `bool`. EF Core throws "Data is Null. This method or property cannot be called on
-- Null values." the moment it materializes any row where one of these is still NULL.
-- TenantAgent/PublishedTenantAgent rows have always explicitly set EnableSTM/EnableLTM/
-- EnableSpinUpAgents on every creation path, so in practice they were never left NULL - this is a
-- real but only latent gap for those two columns, not yet triggered by any existing row.
-- MarketplaceAgent is the one that actually broke: EnableSTM never existed on that AgentType
-- before this feature, so nothing ever wrote to the column for it - every MarketplaceAgent row
-- created before this feature shipped is NULL today, and the very first query that loads one as a
-- full entity (e.g. ShareTenantAgentCommandHandler's lookup by SourcePublishedAgentId, or any
-- Marketplace list/get) crashes on real SQL Server.
--
-- Idempotent and safe to run on every deployment: after the first run, each WHERE ... IS NULL
-- matches zero rows.
UPDATE [dbo].[Agent] SET [EnableSTM] = 0 WHERE [EnableSTM] IS NULL;
UPDATE [dbo].[Agent] SET [EnableLTM] = 0 WHERE [EnableLTM] IS NULL;
UPDATE [dbo].[Agent] SET [EnableSpinUpAgents] = 0 WHERE [EnableSpinUpAgents] IS NULL;
