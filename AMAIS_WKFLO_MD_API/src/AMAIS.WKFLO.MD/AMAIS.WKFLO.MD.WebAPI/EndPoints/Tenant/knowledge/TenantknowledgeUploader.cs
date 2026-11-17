using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Knowledge.Commands.UploadTenantKnowledge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.knowledge
{
    public class TenantknowledgeUploader : BaseAsyncEndpoint<Result>
    {
        private readonly IBlobStorageProvider _blobStorageProvider;
        private readonly TenantContext _tenantContext;

        public TenantknowledgeUploader(IWkfloMediator mediator, IBlobStorageProvider blobStorageProvider, TenantContext tenantContext) : base(mediator)
        {
            _blobStorageProvider = blobStorageProvider;
            _tenantContext = tenantContext;
        }

        [Authorize]
        [HttpPost("/api/v1/tenant/knowledge/uploader")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Knowledge")]
        [EndpointName("TenantKnowledge.Uploader")]
        [EndpointSummary("Uploader for Tenant Knowledge")]
        [EndpointDescription("Uploader for Tenant Knowledge")]
        [ProducesResponseType(typeof(SuccessResult<List<string>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [RequestFormLimits(MultipartBodyLengthLimit = 104857600)] // 100 MB limit
        [RequestSizeLimit(104857600)] // 100 MB limit
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            if (!Request.HasFormContentType || !Request.Form.Files.Any())
            {
                return AmaisWkfloResult(new InvalidResult("Uploader", "No files were uploaded."));
            }

            var files = Request.Form.Files;
            var uploadResults = new List<string>();

            // Get tags if available
            var tags = new List<string>();
            if (Request.Form.TryGetValue("tags", out var tagsValue) && !string.IsNullOrEmpty(tagsValue))
            {
                try
                {
                    tags = JsonConvert.DeserializeObject<List<string>>(tagsValue.ToString());
                }
                catch (JsonException)
                {
                    // If it's not JSON, treat it as a comma-separated string
                    tags = tagsValue.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(t => t.Trim())
                                    .ToList();
                }
            }

            // Get the optional description if available
            string description = null;
            if (Request.Form.TryGetValue("description", out var descriptionValue) && !string.IsNullOrWhiteSpace(descriptionValue))
            {
                description = descriptionValue.ToString();
            }

            foreach (var file in files)
            {
                if (file.Length == 0)
                    continue;

                string base64Content;
                using (var memoryStream = new MemoryStream())
                {
                    await file.CopyToAsync(memoryStream, cancellationToken);
                    byte[] bytes = memoryStream.ToArray();
                    base64Content = Convert.ToBase64String(bytes);

                    // Generate a unique blob name
                    string blobName = $"{_tenantContext.TenantName}/{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
                    string containerName = "tenant-knowledge";

                    // Upload to Blob Storage
                    string blobUrl = await _blobStorageProvider.UploadBase64Async(
                        containerName,
                        blobName,
                        base64Content,
                        file.ContentType,
                        cancellationToken);

                    var command = new UploadTenantKnowledgeCommand
                    {
                        FileName = file.FileName,
                        Title = Path.GetFileNameWithoutExtension(file.FileName),
                        Description = description,
                        ContentType = file.ContentType,
                        Size = file.Length,
                        Base64Content = base64Content,
                        Tags = tags,
                        BlobUrl = blobUrl
                    };

                    var result = await Mediator.Send(command, cancellationToken);
                    if (result is SuccessResult<string> success)
                    {
                        uploadResults.Add(success.Data);
                    }
                    else if (result is UnexpectedResult error)
                    {
                        uploadResults.AddRange(error.Errors.Select(m => string.Concat(m.Property, " : ", m.Code)));
                    }
                }
            }

            return AmaisWkfloResult(new SuccessResult<List<string>>(uploadResults));
        }
    }
}
